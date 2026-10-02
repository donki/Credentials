using Credentials.Helpers;
using Credentials.Models;
using Credentials.Pages;
using Credentials.Services;

namespace Credentials.Ui.Tests;

/// <summary>Las reglas sacadas de las paginas: filtros, textos, validaciones y formatos.</summary>
public class VaultQueryTests
{
    private static Credential E(string title, EntryKind kind = EntryKind.Login, string user = "", string url = "", string folder = "",
        bool fav = false, string notes = "", string[]? tags = null, bool deleted = false, int age = 0) =>
        new()
        {
            Title = title, Kind = kind, Username = user, Url = url, Folder = folder, Favorite = fav, Notes = notes,
            Tags = [.. tags ?? []], Deleted = deleted,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-age), ModifiedAt = DateTimeOffset.UtcNow.AddHours(age),
        };

    private static readonly Credential[] Sample =
    [
        E("Banco", user: "ana", url: "https://banco.example", folder: "Personal", tags: ["dinero"], age: 3),
        E("GitHub", user: "ana@example.com", url: "https://github.com", folder: "Trabajo", fav: true, tags: ["dev"], age: 1),
        E("Wifi", EntryKind.App, folder: "personal", age: 2),
        E("Nota", EntryKind.Note, notes: "licencia secreta", age: 5),
        E("Borrada", deleted: true),
    ];

    [Fact]
    public void Sin_filtro_las_vivas_con_favoritas_primero_y_por_titulo()
    {
        var list = VaultQuery.Apply(Sample, VaultQuery.All, string.Empty, "title");
        Assert.Equal(["GitHub", "Banco", "Nota", "Wifi"], list.Select(e => e.Title));
    }

    [Theory]
    [InlineData("fav", new[] { "GitHub" })]
    [InlineData("kind:App", new[] { "Wifi" })]
    [InlineData("folder:PERSONAL", new[] { "Banco", "Wifi" })]
    [InlineData("tag:DEV", new[] { "GitHub" })]
    [InlineData("desconocido", new[] { "GitHub", "Banco", "Nota", "Wifi" })]
    public void Filtros(string filter, string[] expected) =>
        Assert.Equal(expected, VaultQuery.Apply(Sample, filter, string.Empty, "title").Select(e => e.Title));

    [Theory]
    [InlineData("  github ", new[] { "GitHub" })]   // titulo, sin mirar mayusculas ni espacios
    [InlineData("ana", new[] { "GitHub", "Banco" })] // usuario
    [InlineData("banco.example", new[] { "Banco" })] // enlace
    [InlineData("LICENCIA", new[] { "Nota" })]       // notas
    [InlineData("dine", new[] { "Banco" })]          // etiqueta
    [InlineData("nada-de-nada", new string[0])]
    public void Busqueda(string search, string[] expected) =>
        Assert.Equal(expected, VaultQuery.Apply(Sample, VaultQuery.All, search, "title").Select(e => e.Title));

    [Fact]
    public void Orden_por_modificada_y_por_creada()
    {
        Assert.Equal(["Nota", "Banco", "Wifi", "GitHub"], VaultQuery.Apply(Sample, VaultQuery.All, "", "modified").Select(e => e.Title));
        Assert.Equal(["GitHub", "Wifi", "Banco", "Nota"], VaultQuery.Apply(Sample, VaultQuery.All, "", "created").Select(e => e.Title));
    }

    [Fact]
    public void Fichas_segun_lo_que_hay()
    {
        using var app = TestHost.Start();
        var data = new VaultData { Entries = [.. Sample], Folders = ["Vacia", "trabajo"] };
        var chips = VaultQuery.Chips(data, app.Texts);
        Assert.Equal(
            ["all", "fav", "kind:Login", "kind:App", "kind:Note", "folder:Personal", "folder:Trabajo", "folder:Vacia", "tag:dev", "tag:dinero"],
            chips.Select(c => c.Key));
        Assert.Equal(app["AllEntries"], chips[0].Text);
        Assert.Equal("★ " + app["Favorites"], chips[1].Text);
        Assert.Equal("/Personal", chips[5].Text);
        Assert.Equal("#dev", chips[8].Text);

        // Sin favoritas, carpetas ni etiquetas: solo «todas» y las clases.
        var plain = VaultQuery.Chips(new VaultData { Entries = [E("x")] }, app.Texts);
        Assert.Equal(["all", "kind:Login"], plain.Select(c => c.Key));
    }

    [Fact]
    public void Pulsar_ficha_la_activa_o_la_quita()
    {
        Assert.Equal("fav", VaultQuery.Toggle("all", "fav"));
        Assert.Equal("all", VaultQuery.Toggle("fav", "fav"));
        Assert.Equal("tag:x", VaultQuery.Toggle("fav", "tag:x"));
    }

    [Fact]
    public void Carpeta_del_filtro()
    {
        Assert.Equal("Trabajo", VaultQuery.FolderOf("folder:Trabajo"));
        Assert.Equal(string.Empty, VaultQuery.FolderOf("tag:Trabajo"));
        Assert.Equal(string.Empty, VaultQuery.FolderOf("all"));
    }

    [Fact]
    public void Contador_y_codigos()
    {
        using var app = TestHost.Start();
        Assert.Equal("1 entrada", VaultQuery.CountText(1, app.Texts));
        Assert.Equal("0 entradas", VaultQuery.CountText(0, app.Texts));
        Assert.Equal("7 entradas", VaultQuery.CountText(7, app.Texts));
        Assert.Equal("123 456", VaultQuery.SplitCode("123456"));
        Assert.Equal("1234 5678", VaultQuery.SplitCode("12345678"));
        Assert.Equal(string.Empty, VaultQuery.RowCode("no vale: 0189!"));
        var code = VaultQuery.RowCode("otpauth://totp/X?secret=JBSWY3DPEHPK3PXP");
        Assert.Matches(@"^\d{3} \d{3} · \d{1,2}$", code);
    }
}

public class SettingsRulesTests
{
    [Theory]
    [InlineData("", null, "MasterPasswordEmpty")]
    [InlineData("corta", null, "MasterPasswordShort")]
    [InlineData("1234567", "1234567", "MasterPasswordShort")]
    [InlineData("12345678", null, null)]
    [InlineData("12345678", "12345678", null)]
    [InlineData("12345678", "12345679", "MasterPasswordMismatch")]
    [InlineData("12345678", "", "MasterPasswordMismatch")]
    public void Contraseña_maestra(string password, string? repeat, string? problem) =>
        Assert.Equal(problem, MasterPasswordRules.Problem(password, repeat));

    [Fact]
    public void Opciones_de_bloqueo_y_portapapeles()
    {
        using var app = TestHost.Start();
        var locks = SettingsRules.LockLabels(app.Texts);
        Assert.Equal(SettingsRules.LockMinutes.Length, locks.Count);
        Assert.Equal(app["AutoLockNever"], locks[0]);
        Assert.Equal("5 min", locks[3]);
        var clips = SettingsRules.ClipLabels(app.Texts);
        Assert.Equal(app["ClipboardNever"], clips[0]);
        Assert.Equal(string.Format(app["ClipboardSeconds"], 30), clips[2]);
        Assert.Equal(3, SettingsRules.IndexOf(SettingsRules.LockMinutes, 5));
        Assert.Equal(0, SettingsRules.IndexOf(SettingsRules.LockMinutes, 7));   // valor raro: la primera
    }

    [Fact]
    public void Estado_de_la_nube()
    {
        using var app = TestHost.Start();
        Assert.Equal(string.Empty, SettingsRules.StatusText("cloud:ok:3", app.Texts));
        Assert.Equal(string.Format(app["SyncFailed"], "sin red"), SettingsRules.StatusText("cloud:error:sin red", app.Texts));
        Assert.Equal("otra cosa", SettingsRules.StatusText("otra cosa", app.Texts));
        Assert.Equal(string.Format(app["SyncDone"], 4), SettingsRules.SyncText(4, app.Texts));
        Assert.Equal(app["SyncNothing"], SettingsRules.SyncText(0, app.Texts));
    }

    [Theory]
    [InlineData("BORRAR", true)]
    [InlineData("  borrar ", true)]
    [InlineData("Delete", true)]
    [InlineData("borra", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Palabra_de_borrar(string? word, bool ok) => Assert.Equal(ok, SettingsRules.IsDeleteWord(word));

    [Fact]
    public void Nombres_y_marcas()
    {
        Assert.Equal("sOCCredentials-20261002-0905.json", SettingsRules.ExportName(new DateTime(2026, 10, 2, 9, 5, 0), "json"));
        Assert.Equal("Google", SettingsRules.BrandOf(StorageMode.GoogleDrive));
        Assert.Equal("Microsoft", SettingsRules.BrandOf(StorageMode.OneDrive));
    }
}

public class EntryRulesTests
{
    [Fact]
    public void Fuerza()
    {
        Assert.Equal(0, EntryRules.StrengthProgress("", 3));
        Assert.Equal(0.2, EntryRules.StrengthProgress("a", 0), 3);
        Assert.Equal(1.0, EntryRules.StrengthProgress("a", 4), 3);
        Assert.Equal(Color.FromArgb("#BA1A1A"), EntryRules.StrengthColor(0));
        Assert.Equal(Color.FromArgb("#0E9F6E"), EntryRules.StrengthColor(3));
        Assert.Equal(Color.FromArgb("#059669"), EntryRules.StrengthColor(4));
        Assert.Equal(Color.FromArgb("#059669"), EntryRules.StrengthColor(-1));   // como el switch de antes: lo demas, verde
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" github.com ", "https://github.com")]
    [InlineData("http://intranet", "http://intranet")]
    [InlineData("ftp://nas/x", "ftp://nas/x")]
    public void Enlace(string? text, string? url) => Assert.Equal(url, EntryRules.OpenableUrl(text));

    [Fact]
    public void Etiquetas_carpeta_y_codigos()
    {
        Assert.Equal(["dev", "Trabajo"], EntryRules.ParseTags(" dev, Trabajo,,DEV , "));
        Assert.Empty(EntryRules.ParseTags(null));
        Assert.Equal("Trabajo/Proyectos", EntryRules.NormalizeFolder(" /Trabajo/Proyectos/ "));
        Assert.Equal(string.Empty, EntryRules.NormalizeFolder(null));
        Assert.Equal(["aaa", "bbb", "ccc", "ddd", "eee"], EntryRules.ParseRecovery("aaa\r\nbbb, ccc;ddd\t eee \n\n"));
        Assert.Empty(EntryRules.ParseRecovery(null));

        var list = new List<RecoveryCode> { new() { Code = "AAA" } };
        Assert.Equal(2, EntryRules.AddRecovery(list, ["aaa", "bbb", "ccc", "BBB"]));
        Assert.Equal(["AAA", "bbb", "ccc"], list.Select(r => r.Code));
    }

    [Fact]
    public void Resumen_de_codigos()
    {
        using var app = TestHost.Start();
        Assert.Equal(app["RecoveryNone"], EntryRules.RecoverySummary([], app.Texts));
        Assert.Equal(string.Format(app["RecoveryCount"], 3, 2),
            EntryRules.RecoverySummary([new() { Code = "a" }, new() { Code = "b", Used = true }, new() { Code = "c" }], app.Texts));
    }

    [Fact]
    public void Segundo_factor()
    {
        var t = Totp.Parse("otpauth://totp/GitHub:ana?secret=JBSWY3DPEHPK3PXP&issuer=GitHub")!;
        Assert.Equal("GitHub · ana  (SHA1, 6, 30s)", EntryRules.TotpDescription(t));
        var bare = Totp.Parse("JBSWY3DPEHPK3PXP")!;
        Assert.Equal("  (SHA1, 6, 30s)", EntryRules.TotpDescription(bare));
        Assert.Equal("JBSW Y3DP EHPK 3PXP", EntryRules.SeedGroups("JBSWY3DPEHPK3PXP=="));
        Assert.Equal(0.5, EntryRules.TotpProgress(t, 15), 3);
        var hotp = Totp.Parse("otpauth://hotp/X?secret=JBSWY3DPEHPK3PXP&counter=3")!;
        Assert.Equal(1, EntryRules.TotpProgress(hotp, 0));
    }

    [Fact]
    public void Historial_al_cambiar_la_contraseña()
    {
        var when = DateTimeOffset.UtcNow.AddDays(-2);
        var original = new Credential { Password = "vieja", ModifiedAt = when };
        var entry = original.Clone();
        EntryRules.RecordHistory(original, entry, isNew: false, "nueva");
        Assert.Equal(new PasswordHistoryItem("vieja", when), Assert.Single(entry.History));

        var same = original.Clone();
        EntryRules.RecordHistory(original, same, isNew: false, "vieja");   // la misma: nada
        Assert.Empty(same.History);
        var fresh = original.Clone();
        EntryRules.RecordHistory(original, fresh, isNew: true, "otra");    // nueva: no hay anterior
        Assert.Empty(fresh.History);
        var empty = new Credential();
        EntryRules.RecordHistory(empty, empty.Clone(), isNew: false, "x"); // sin contraseña antes: nada
        Assert.Empty(empty.History);
    }

    [Fact]
    public void Guardar_en_la_boveda()
    {
        var data = new VaultData { Folders = ["Trabajo"] };
        var a = new Credential { Title = "A", Folder = "trabajo" };
        EntryRules.Commit(data, a);
        Assert.Same(a, Assert.Single(data.Entries));
        Assert.Equal(["Trabajo"], data.Folders);   // ya estaba (sin mirar mayusculas)

        var edited = a.Clone();
        edited.Title = "A2";
        edited.Folder = "Casa";
        EntryRules.Commit(data, edited);
        Assert.Equal("A2", Assert.Single(data.Entries).Title);
        Assert.Equal(["Trabajo", "Casa"], data.Folders);

        EntryRules.Commit(data, new Credential { Title = "B" });
        Assert.Equal(2, data.Entries.Count);
        Assert.Equal(2, data.Folders.Count);
    }
}

public class HelpersTests
{
    [Fact]
    public void RunOnUi_al_momento_o_encolado()
    {
        var d = new TestDispatcher();
        var ran = 0;
        d.RunOnUi(() => ran++);
        Assert.Equal(1, ran);
        d.Required = true;
        d.RunOnUi(() => ran++);
        Assert.Equal(1, ran);
        Assert.Single(d.Queued);
        d.Queued[0]();
        Assert.Equal(2, ran);
    }

    [Fact]
    public void ServiceHelper_sin_iniciar_avisa()
    {
        using (TestHost.Start())
        {
        }
        ServiceHelper.Initialize(null!);
        var ex = Assert.Throws<InvalidOperationException>(() => ServiceHelper.GetRequiredService<VaultStore>());
        Assert.Contains("before MauiProgram", ex.Message);
    }

    [Fact]
    public async Task Sin_plataforma_ni_biometria_ni_navegador()
    {
        var b = new NoBiometric();
        Assert.False(await b.IsAvailableAsync());
        Assert.False(await b.AuthenticateAsync("t", "r"));
        var browser = new NoOAuthBrowser();
        Assert.Equal("http://127.0.0.1/auth/", browser.RedirectUri("Google", "x"));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => browser.AuthenticateAsync(new Uri("https://a"), new Uri("https://b"), default));
    }

    [Fact]
    public void Error_sin_datos()
    {
        Exception original;
        try
        {
            try { throw new FormatException("La contraseña 'secreta-123' no vale"); }
            catch (Exception inner) { throw new InvalidOperationException("otra 'secreta-123'", inner); }
        }
        catch (Exception ex) { original = ex; }
        var aggregate = new AggregateException(original, new ArgumentException("x"));
        var redacted = new CrashReporting.RedactedException(aggregate);
        var text = redacted.ToString();
        Assert.DoesNotContain("secreta-123", text);
        Assert.Contains("System.AggregateException", text);
        Assert.Contains("---> System.InvalidOperationException", text);
        Assert.Contains("---> System.FormatException", text);
        Assert.Contains("---> System.ArgumentException", text);
        Assert.Contains("HResult 0x", text);
        Assert.Equal(aggregate.StackTrace, redacted.StackTrace);
        Assert.DoesNotContain("secreta", redacted.Message);

        // Una cadena sin fin (mas de 8 niveles) se corta.
        Exception deep = new Exception("0");
        for (var i = 1; i < 15; i++)
            deep = new Exception(i.ToString(), deep);
        var lines = new CrashReporting.RedactedException(deep).ToString().Split('\n').Count(l => l.Contains("--->"));
        Assert.Equal(8, lines);
    }

    [Fact]
    public void Registro_en_la_carpeta_de_pruebas_y_con_tope()
    {
        using var app = TestHost.Start();
        AppLog.Write("linea\nuno");
        AppLog.Error("nube", new CloudException("Drive", System.Net.HttpStatusCode.Forbidden, "{\"error\":\"x\"}"));
        AppLog.Error("otro", new IOException("disco"));
        var path = Path.Combine(app.Files.AppDataDirectory, "logs", "app.log");
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("linea uno", lines[0]);
        Assert.Contains("[nube] CloudException:", lines[1]);
        Assert.EndsWith("[otro] IOException: disco", lines[2]);

        // Pasado 1 MB se queda la mitad mas reciente.
        File.WriteAllText(path, new string('x', 1_000_001) + "FIN");
        AppLog.Write("nueva");
        var size = new FileInfo(path).Length;
        Assert.InRange(size, 400_000, 600_000);
        Assert.EndsWith("nueva", File.ReadAllLines(path)[^1]);

        // Si no se puede escribir, no rompe nada.
        File.Delete(path);
        Directory.CreateDirectory(path);
        AppLog.Write("no cabe");
    }
}
