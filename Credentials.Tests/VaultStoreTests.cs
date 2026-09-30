using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Tests;

public class VaultStoreTests
{
    private const string Master = "Correcta Caballo Pila Grapa";

    [Fact]
    public async Task Create_Lock_Unlock_RoundTrip()
    {
        using var box = Sandbox.Create();
        var store = box.Store;
        var events = new List<string>();
        store.Unlocked += () => events.Add("unlocked");
        store.Locked += () => events.Add("locked");
        store.Changed += () => events.Add("changed");

        Assert.False(store.Exists);
        Assert.False(store.IsUnlocked);
        Assert.Equal(Path.Combine(box.Directory, "vault.soccred"), VaultStore.FilePath);
        Assert.False(VaultStore.Sandbox);

        await store.CreateAsync(Master);
        Assert.True(store.Exists);
        Assert.True(store.IsUnlocked);
        store.Data!.Entries.Add(new Credential { Title = "GitHub", Password = "s3cr3t" });
        await store.SaveAsync();

        var onDisk = File.ReadAllText(VaultStore.FilePath);
        Assert.True(VaultCrypto.IsVault(onDisk));
        Assert.DoesNotContain("s3cr3t", onDisk);
        Assert.True(File.Exists(VaultStore.FilePath + ".bak"));
        Assert.False(File.Exists(VaultStore.FilePath + ".tmp"));

        store.Lock();
        store.Lock(); // dos veces no pasa nada
        Assert.False(store.IsUnlocked);
        Assert.Null(store.Data);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync());

        await store.UnlockAsync(Master);
        Assert.Equal("GitHub", store.Data!.Entries.Single().Title);
        Assert.Equal(["changed", "unlocked", "changed", "locked", "changed", "unlocked"], events);
    }

    [Fact]
    public async Task WrongPassword_Throws_AndStaysLocked()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        box.Store.Lock();

        await Assert.ThrowsAnyAsync<CryptographicException>(() => box.Store.UnlockAsync("otra"));
        Assert.False(box.Store.IsUnlocked);
    }

    [Fact]
    public async Task StoredKey_Biometrics_AndTrustedDevice()
    {
        using var box = Sandbox.Create();
        var store = box.Store;
        await store.CreateAsync(Master);

        Assert.False(store.HasStoredKey);
        await store.RememberKeyAsync(true);
        Assert.True(store.HasStoredKey);
        store.Lock();

        Assert.True(await store.UnlockWithStoredKeyAsync());
        store.Lock();

        // Sin «confiar en este dispositivo» no se abre solo.
        Assert.False(await store.TryTrustedUnlockAsync());
        box.Settings.TrustDevice = true;
        Assert.True(await store.TryTrustedUnlockAsync());
        Assert.True(await store.TryTrustedUnlockAsync()); // ya abierta
        store.Lock();

        // Una clave que no vale (otra contraseña) no abre y no revienta.
        box.Secure.Values["vault.key"] = Convert.ToBase64String(new byte[32]);
        Assert.False(await store.UnlockWithStoredKeyAsync());

        await store.RememberKeyAsync(false);
        Assert.False(store.HasStoredKey);
        Assert.False(await store.UnlockWithStoredKeyAsync());

        box.Secure.Broken = true;
        Assert.False(store.HasStoredKey);
        Assert.False(await store.UnlockWithStoredKeyAsync());
    }

    [Fact]
    public async Task TrustedUnlock_WithoutVault_IsFalse()
    {
        using var box = Sandbox.Create();
        box.Settings.TrustDevice = true;
        Assert.False(await box.Store.TryTrustedUnlockAsync());
    }

    [Fact]
    public async Task IdleLock_FollowsSettings_AndSystemIdle()
    {
        using var box = Sandbox.Create();
        var store = box.Store;
        await store.CreateAsync(Master);
        box.Settings.AutoLockMinutes = 5;

        try
        {
            VaultStore.SystemIdle = () => TimeSpan.FromMinutes(1);
            Assert.False(store.LockIfIdle());
            Assert.True(store.IdleTime <= TimeSpan.FromMinutes(1));

            // El sistema dice que llevamos poco sin tocar nada, aunque la aplicacion lleve mas: manda el menor.
            VaultStore.SystemIdle = () => throw new InvalidOperationException();
            Assert.True(store.IdleTime < TimeSpan.FromSeconds(30));

            box.Settings.AutoLockMinutes = 0;
            Assert.False(store.LockIfIdle());

            // Diez minutos sin tocar la aplicacion y sin saber nada del sistema: con 1 minuto, se cierra.
            box.Settings.AutoLockMinutes = 1;
            VaultStore.SystemIdle = null;
            typeof(VaultStore).GetField("_lastActivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(store, DateTimeOffset.UtcNow.AddMinutes(-10));

            // Pero no si se confia en el dispositivo.
            box.Settings.TrustDevice = true;
            Assert.False(store.LockIfIdle());
            box.Settings.TrustDevice = false;

            Assert.True(store.LockIfIdle());
            Assert.False(store.IsUnlocked);
            Assert.False(store.LockIfIdle()); // ya cerrada
        }
        finally
        {
            VaultStore.SystemIdle = null;
        }
    }

    [Fact]
    public async Task Delete_LeavesATombstoneWithoutSecrets()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        var e = new Credential { Title = "t", Password = "p", Totp = "otpauth://totp/x?secret=JBSWY3DP", Notes = "n" };
        e.Fields.Add(new CustomField { Name = "pin", Value = "1" });
        e.RecoveryCodes.Add(new RecoveryCode { Code = "r" });
        e.History.Add(new PasswordHistoryItem("vieja", DateTimeOffset.UtcNow));
        box.Store.Data!.Entries.Add(e);

        await box.Store.DeleteAsync(Guid.NewGuid()); // no existe: nada
        await box.Store.DeleteAsync(e.Id);

        var tomb = box.Store.Data!.Entries.Single();
        Assert.True(tomb.Deleted);
        Assert.Equal(("", "", ""), (tomb.Password, tomb.Totp, tomb.Notes));
        Assert.Empty(tomb.Fields);
        Assert.Empty(tomb.RecoveryCodes);
        Assert.Empty(tomb.History);
        Assert.Equal("t", tomb.Title);
    }

    [Fact]
    public async Task ChangeMasterPassword_ReEncrypts_AndUpdatesTheRememberedKey()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        await box.Store.RememberKeyAsync(true);
        var oldKey = box.Secure.Values["vault.key"];

        await box.Store.ChangeMasterPasswordAsync("nueva");
        Assert.NotEqual(oldKey, box.Secure.Values["vault.key"]);
        box.Store.Lock();

        await Assert.ThrowsAnyAsync<CryptographicException>(() => box.Store.UnlockAsync(Master));
        await box.Store.UnlockAsync("nueva");
        Assert.True(box.Store.IsUnlocked);
    }

    [Fact]
    public async Task Exports()
    {
        using var box = Sandbox.Create();
        await box.Store.CreateAsync(Master);
        box.Store.Data!.Entries.Add(new Credential { Title = "Exportada" });
        await box.Store.SaveAsync();

        Assert.Equal(File.ReadAllText(VaultStore.FilePath), box.Store.ExportEncrypted());
        var plain = box.Store.ExportPlainJson();
        Assert.Contains("Exportada", plain);
        // Lo exportado en claro se vuelve a importar tal cual.
        Assert.Equal("sOC Credentials", Importers.Parse(plain)!.Source);

        box.Store.Lock();
        Assert.Throws<InvalidOperationException>(() => box.Store.ExportPlainJson());
    }

    // ------------------------------------------------------------------ nube

    private static void GoogleSignInRoutes(Sandbox box, string scope = "openid email https://www.googleapis.com/auth/drive.appdata") =>
        box.Http.OnJson(HttpMethod.Post, "oauth2.googleapis.com/token", new
        {
            access_token = "acc",
            refresh_token = "ref",
            expires_in = 3600,
            id_token = Jwt.Make(new { email = "ana@gmail.com" }),
            scope,
        });

    private sealed class FakeGoogleDrive
    {
        public string? Content { get; set; }

        public FakeGoogleDrive(FakeHttp http)
        {
            http.On(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", _ => FakeHttp.Response(HttpStatusCode.OK,
                Content is null ? "{\"files\":[]}" : "{\"files\":[{\"id\":\"F\",\"modifiedTime\":\"2026-09-30T00:00:00Z\"}]}"));
            http.On(HttpMethod.Get, "drive/v3/files/F?alt=media", _ => FakeHttp.Response(HttpStatusCode.OK, Content!));
            http.On(HttpMethod.Post, "upload/drive/v3/files?uploadType=multipart", r =>
            {
                var body = r.Content!.ReadAsStringAsync().Result;
                Content = body[body.IndexOf(VaultCrypto.Magic, StringComparison.Ordinal)..body.LastIndexOf('\n')] + "\n";
                return FakeHttp.Response(HttpStatusCode.OK, "{}");
            });
            http.On(HttpMethod.Patch, "upload/drive/v3/files/F", r =>
            {
                Content = r.Content!.ReadAsStringAsync().Result;
                return FakeHttp.Response(HttpStatusCode.OK, "{}");
            });
        }
    }

    [Fact]
    public async Task Providers_AreConfiguredFromTheBuild()
    {
        using var box = Sandbox.Create();
        Assert.False(box.Store.IsConfigured(StorageMode.Local));
        Assert.True(box.Store.IsConfigured(StorageMode.GoogleDrive));
        Assert.True(box.Store.IsConfigured(StorageMode.OneDrive));
        Assert.Equal("Microsoft", box.Store.Provider(StorageMode.OneDrive).Name);
        Assert.Equal("secreto-de-prueba", box.Store.Provider(StorageMode.GoogleDrive).ClientSecret);
        Assert.Throws<InvalidOperationException>(() => box.Store.Provider(StorageMode.Local));
        Assert.Equal("com.socratic.credentials.google", OAuthSecrets.GoogleRedirectScheme);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SignIn_SavesTokensAndEmail_SignOutForgets()
    {
        using var box = Sandbox.Create();
        GoogleSignInRoutes(box);

        Assert.Equal("ana@gmail.com", await box.Store.SignInAsync(StorageMode.GoogleDrive));
        Assert.Equal(StorageMode.GoogleDrive, box.Settings.Storage);
        Assert.Equal("ana@gmail.com", box.Settings.AccountEmail);
        Assert.True(box.Secure.Values.ContainsKey("cloud.tokens"));
        Assert.DoesNotContain("acc", box.Settings.AccountEmail);

        box.Store.SignOut();
        Assert.Equal(StorageMode.Local, box.Settings.Storage);
        Assert.Equal(string.Empty, box.Settings.AccountEmail);
        Assert.False(box.Secure.Values.ContainsKey("cloud.tokens"));
    }

    [Fact]
    public async Task SignIn_WithoutTheFolderPermission_IsRefused()
    {
        using var box = Sandbox.Create();
        GoogleSignInRoutes(box, scope: "openid email");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => box.Store.SignInAsync(StorageMode.GoogleDrive));
        Assert.Equal("scope", ex.Message);
        Assert.Equal(StorageMode.Local, box.Settings.Storage);
    }

    [Fact]
    public async Task Sync_FirstUpload_ThenMergeBothWays()
    {
        using var box = Sandbox.Create();
        var cloud = new FakeGoogleDrive(box.Http);
        GoogleSignInRoutes(box);
        await box.Store.CreateAsync(Master);
        await box.Store.SignInAsync(StorageMode.GoogleDrive);
        var statuses = new List<string>();
        box.Store.Status += statuses.Add;

        // Local: nada que sincronizar.
        box.Settings.Storage = StorageMode.Local;
        Assert.Equal(0, await box.Store.SyncAsync());
        box.Settings.Storage = StorageMode.GoogleDrive;

        // La nube esta vacia: se sube lo de aqui.
        box.Store.Data!.Entries.Add(new Credential { Title = "De aqui" });
        Assert.Equal(0, await box.Store.SyncAsync());
        await WaitFor(() => cloud.Content is not null);
        Assert.Contains("cloud:ok:GoogleDrive", statuses);

        // Otro dispositivo (misma boveda, misma clave) añade una entrada.
        var remote = Decrypt(cloud.Content!);
        remote.Entries.Add(new Credential { Title = "De alli", ModifiedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        cloud.Content = Encrypt(remote, cloud.Content!);

        Assert.Equal(1, await box.Store.SyncAsync());
        Assert.Equal(["De aqui", "De alli"], box.Store.Data!.Entries.Select(e => e.Title));
    }

    [Fact]
    public async Task Sync_CloudWithAnotherSalt_AsksForThePassword()
    {
        using var box = Sandbox.Create();
        var cloud = new FakeGoogleDrive(box.Http);
        GoogleSignInRoutes(box);
        await box.Store.CreateAsync(Master);
        await box.Store.SignInAsync(StorageMode.GoogleDrive);

        // Otra instalacion creo la boveda de la nube con la misma contraseña pero otra sal.
        var other = new VaultData();
        other.Entries.Add(new Credential { Title = "Del movil" });
        var header = VaultCrypto.NewHeader(other.VaultId);
        cloud.Content = VaultCrypto.Encrypt(JsonSerializer.Serialize(other), VaultCrypto.DeriveKey(Master, header), header);

        var ex = await Assert.ThrowsAsync<VaultPasswordNeededException>(() => box.Store.SyncAsync());
        Assert.Equal(cloud.Content, ex.RemoteContent);

        Assert.Equal(1, await box.Store.MergeRemoteWithPasswordAsync(ex.RemoteContent, Master));
        Assert.Contains(box.Store.Data!.Entries, e => e.Title == "Del movil");

        await Assert.ThrowsAnyAsync<CryptographicException>(() => box.Store.MergeRemoteWithPasswordAsync(ex.RemoteContent, "mala"));
    }

    [Fact]
    public async Task Sync_SameSaltButOtherKey_AsksForThePassword()
    {
        using var box = Sandbox.Create();
        var cloud = new FakeGoogleDrive(box.Http);
        GoogleSignInRoutes(box);
        await box.Store.CreateAsync(Master);
        await box.Store.SignInAsync(StorageMode.GoogleDrive);
        await box.Store.SyncAsync();
        await WaitFor(() => cloud.Content is not null);

        // Misma cabecera (misma sal) pero cifrada con otra contraseña: la clave de aqui no vale.
        var header = VaultCrypto.ReadHeader(cloud.Content!);
        cloud.Content = VaultCrypto.Encrypt("{}", VaultCrypto.DeriveKey("otra", header), header);
        await Assert.ThrowsAsync<VaultPasswordNeededException>(() => box.Store.SyncAsync());
    }

    [Fact]
    public async Task SyncQuietly_ThrottlesAndReportsInsteadOfThrowing()
    {
        using var box = Sandbox.Create();
        var cloud = new FakeGoogleDrive(box.Http);
        GoogleSignInRoutes(box);
        var statuses = new List<string>();
        box.Store.Status += s => { lock (statuses) statuses.Add(s); };

        await box.Store.SyncQuietlyAsync(TimeSpan.Zero); // cerrada: nada
        await box.Store.CreateAsync(Master);
        await box.Store.SyncQuietlyAsync(TimeSpan.Zero); // sin nube: nada
        Assert.Empty(box.Http.Calls);

        await box.Store.SignInAsync(StorageMode.GoogleDrive);
        await box.Store.SyncQuietlyAsync(TimeSpan.FromMinutes(5));
        await WaitFor(() => cloud.Content is not null);
        var calls = box.Http.Calls.Count;
        await box.Store.SyncQuietlyAsync(TimeSpan.FromMinutes(5)); // hace nada: no repite
        Assert.Equal(calls, box.Http.Calls.Count);

        // Otra sal: se avisa de que hace falta la contraseña.
        var header = VaultCrypto.NewHeader(Guid.NewGuid());
        cloud.Content = VaultCrypto.Encrypt("{}", VaultCrypto.DeriveKey(Master, header), header);
        await box.Store.SyncQuietlyAsync(TimeSpan.Zero);
        Assert.Contains("cloud:error:password", statuses);

        // Un fallo de la nube: se apunta y se avisa, no se lanza.
        box.Http.On(HttpMethod.Get, "drive/v3/files", HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"sin permiso\"}}");
        await box.Store.SyncQuietlyAsync(TimeSpan.Zero);
        Assert.Contains("cloud:error:Google Drive: 403 sin permiso", statuses);
        Assert.Contains(AppLog.Lines, l => l.StartsWith("[sincronizar sola] CloudException: Google Drive 403 GET"));
    }

    [Fact]
    public async Task Save_UploadFailure_IsReportedNotThrown()
    {
        using var box = Sandbox.Create();
        var statuses = new List<string>();
        box.Store.Status += s => { lock (statuses) statuses.Add(s); };
        await box.Store.CreateAsync(Master);

        // Dice que esta en la nube pero no hay sesion guardada.
        box.Settings.Storage = StorageMode.OneDrive;
        await box.Store.SaveAsync();
        await WaitFor(() => { lock (statuses) return statuses.Count > 0; });
        Assert.Equal("cloud:error:Sin sesion en la nube.", statuses[0]);
    }

    [Fact]
    public async Task Sync_RefreshesAnExpiredToken_AndStoresIt()
    {
        using var box = Sandbox.Create();
        var cloud = new FakeGoogleDrive(box.Http);
        box.Http.OnJson(HttpMethod.Post, "login.microsoftonline.com/common/oauth2/v2.0/token", new
        {
            access_token = "ms",
            refresh_token = "r",
            expires_in = 0,
            scope = "openid email offline_access Files.ReadWrite.AppFolder",
        });
        await box.Store.CreateAsync(Master);
        Assert.Equal(string.Empty, await box.Store.SignInAsync(StorageMode.OneDrive));

        box.Http.On(HttpMethod.Get, "approot:/vault.soccred?select", HttpStatusCode.NotFound, "{}");
        box.Http.On(HttpMethod.Put, "approot:/vault.soccred:/content", HttpStatusCode.Created, "{}");
        var before = box.Secure.Values["cloud.tokens"];
        Assert.Equal(0, await box.Store.SyncAsync());
        await WaitFor(() => box.Http.Calls.Any(c => c.Method == HttpMethod.Put));

        Assert.True(box.Http.Calls.Count(c => c.Url.Contains("oauth2/v2.0/token")) >= 2);
        Assert.NotEqual(before, box.Secure.Values["cloud.tokens"]);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "No ha pasado a tiempo.");
    }

    // La nube de la prueba usa la misma clave que la boveda local (misma cabecera): se abre con la
    // contraseña maestra, como haria otro dispositivo.
    private static VaultData Decrypt(string content) =>
        JsonSerializer.Deserialize<VaultData>(VaultCrypto.Decrypt(content, VaultCrypto.DeriveKey(Master, VaultCrypto.ReadHeader(content))))!;

    private static string Encrypt(VaultData data, string like)
    {
        var header = VaultCrypto.ReadHeader(like);
        return VaultCrypto.Encrypt(JsonSerializer.Serialize(data), VaultCrypto.DeriveKey(Master, header), header);
    }
}

public class SettingsAndTextsTests
{
    [Fact]
    public void Settings_DefaultsAndRoundTrip()
    {
        using var box = Sandbox.Create();
        var s = box.Settings;

        Assert.Equal(LocalizationService.SystemLanguage, s.Language);
        Assert.Equal(StorageMode.Local, s.Storage);
        Assert.Equal(string.Empty, s.AccountEmail);
        Assert.Equal(OperatingSystem.IsWindows() ? 0 : 15, s.AutoLockMinutes);
        Assert.False(s.TrustDevice);
        Assert.False(s.Biometrics);
        Assert.Equal(30, s.ClipboardSeconds);
        Assert.Equal("title", s.SortMode);
        Assert.True(s.TrayOnMinimize);
        Assert.True(s.AskExtensions);
        Assert.True(s.AskAutofill);
        Assert.True(s.DesktopAutofill);
        Assert.False(s.TutorialDone);
        Assert.Null(s.ExtensionSeen("edge"));

        s.Language = "es"; s.Storage = StorageMode.OneDrive; s.AccountEmail = "a@b.c"; s.AutoLockMinutes = 3;
        s.TrustDevice = true; s.Biometrics = true; s.ClipboardSeconds = 0; s.SortMode = "recent";
        s.TrayOnMinimize = false; s.AskExtensions = false; s.AskAutofill = false; s.DesktopAutofill = false; s.TutorialDone = true;
        s.SetExtensionSeen("edge");

        Assert.Equal(("es", StorageMode.OneDrive, "a@b.c", 3), (s.Language, s.Storage, s.AccountEmail, s.AutoLockMinutes));
        Assert.True(s.TrustDevice && s.Biometrics && s.TutorialDone);
        Assert.False(s.TrayOnMinimize || s.AskExtensions || s.AskAutofill || s.DesktopAutofill);
        Assert.Equal((0, "recent"), (s.ClipboardSeconds, s.SortMode));
        Assert.InRange(s.ExtensionSeen("edge")!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        Assert.Null(s.ExtensionSeen("firefox"));

        // Nulos y valores raros vuelven al de partida.
        s.Language = null!; s.AccountEmail = null!; s.SortMode = null!;
        Assert.Equal((string.Empty, string.Empty, "title"), (s.Language, s.AccountEmail, s.SortMode));
        Preferences.Set("storage", "Dropbox", null);
        Assert.Equal(StorageMode.Local, s.Storage);

        // Nunca en el contenedor de pruebas del modo sandbox: con Debug y sin SOC_SANDBOX, el normal.
        Assert.All(Preferences.SharedNames, n => Assert.Null(n));
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("en", "en")]
    [InlineData("fr", "en")]
    public void Texts_LanguageResolution(string chosen, string expected)
    {
        using var box = Sandbox.Create();
        var texts = box.Texts(chosen);
        Assert.Equal(expected, texts.CurrentLanguage);
        Assert.Equal(expected, texts.CurrentCulture.Name);
    }

    [Fact]
    public void Texts_FollowTheSystem_AndSwitchHot()
    {
        using var box = Sandbox.Create();
        var texts = box.Texts("");
        Assert.Contains(texts.CurrentLanguage, new[] { "es", "en" });

        var raised = 0;
        texts.LanguageChanged += (_, _) => raised++;
        texts.SetLanguage("es");
        texts.SetLanguage("es"); // igual: no avisa
        Assert.Equal("es", texts.CurrentLanguage);
        texts.SetLanguage("en");
        Assert.InRange(raised, 1, 2);
        Assert.Equal("Socratic", texts["Company"]);
        Assert.Equal("ClaveInexistente", texts["ClaveInexistente"]);
    }

    [Fact]
    public void Texts_MissingSpanish_FallsBackToEnglish()
    {
        using var box = Sandbox.Create();
        var es = box.Texts("es");
        var onlyEnglish = TextTables.English.Keys.Except(TextTables.Spanish.Keys).FirstOrDefault();
        if (onlyEnglish is not null)
            Assert.Equal(TextTables.English[onlyEnglish], es[onlyEnglish]);
        Assert.Equal(TextTables.Spanish["About"], es["About"]);
    }

    [Fact]
    public void AppName_DependsOnPlatform() =>
        Assert.Equal(OperatingSystem.IsAndroid() ? "Credentials" : "sOC Credentials", LocalizationService.AppName);
}
