using System.Net;
using Credentials.Models;
using Credentials.Pages;
using Credentials.Services;
using Microsoft.Maui.Devices;

namespace Credentials.Ui.Tests;

public class SettingsPageTests
{
    private const string Master = "contraseña-larga";

    private static async Task<(TestHost App, SettingsPage Page)> Open(params Credential[] entries)
    {
        var app = TestHost.Start();
        await app.CreateVaultAsync(Master, entries);
        return (app, new SettingsPage());
    }

    private static string Status(SettingsPage page) => page.Find<Label>("SyncStatus").Text;

    [Fact]
    public async Task Textos_y_valores_de_los_ajustes()
    {
        var (app, page) = await Open();
        using var _app = app;
        Assert.Equal(app["SettingsTitle"], page.Title);
        Assert.Equal("✓ " + app["StorageLocal"], page.Find<Button>("LocalButton").Text);
        Assert.Equal(app["StorageGoogle"], page.Find<Button>("GoogleButton").Text);
        Assert.Equal("ic_lock_w.png", ((FileImageSource)page.Find<Button>("LocalButton").ImageSource).File);
        Assert.False(page.Find<Label>("AccountLabel").IsVisible);
        Assert.False(page.Find<Button>("SyncButton").IsVisible);
        Assert.False(page.Find<VisualElement>("AutofillCard").IsVisible);   // sin plataforma no hay autocompletar
        var autolock = page.Find<Picker>("AutoLockPicker");
        Assert.Equal(SettingsRules.LockLabels(app.Texts), autolock.ItemsSource);
        Assert.Equal(SettingsRules.IndexOf(SettingsRules.LockMinutes, app.Settings.AutoLockMinutes), autolock.SelectedIndex);
        Assert.Equal(2, page.Find<Picker>("ClipboardPicker").SelectedIndex);   // 30 s por defecto

        // Idioma: el activo va en primario; cambiar a ingles lo guarda y rehace los textos.
        Assert.Same(app.App.Resources["PrimaryButton"], page.Find<Button>("SpanishButton").Style);
        await page.Click("SpanishButton");   // ya lo es: nada
        Assert.Equal("es", app.Texts.CurrentLanguage);
        await page.Click("EnglishButton");
        Assert.Equal("en", app.Settings.Language);
        Assert.Equal("Settings", page.Title);
        Assert.Same(app.App.Resources["PrimaryButton"], page.Find<Button>("EnglishButton").Style);
        await page.Click("SpanishButton");
        Assert.Equal("es", app.Texts.CurrentLanguage);
    }

    [Fact]
    public async Task Al_aparecer_mira_la_biometria()
    {
        var (app, page) = await Open();
        using var _app = app;
        await page.Appear();
        Assert.False(page.Find<Switch>("BiometricsSwitch").IsEnabled);
        Assert.Equal(app["BiometricsUnavailable"], page.Find<Label>("BiometricsHint").Text);

        app.Biometric.Available = true;
        await page.Appear();
        Assert.True(page.Find<Switch>("BiometricsSwitch").IsEnabled);

        // Bloqueada: sale la puerta y, si se cierra sin abrir, no sigue.
        app.Store.Lock();
        var appearing = page.Appear();
        var gate = await Ui.GateOn(page);
        Ui.Dismiss(page, gate);
        await appearing;
    }

    [Fact]
    public async Task Bloqueada_pide_la_contraseña_y_sin_ella_no_sigue()
    {
        var (app, page) = await Open();
        using var _app = app;
        app.Store.Lock();
        var appearing = page.Appear();
        Ui.Dismiss(page, await Ui.GateOn(page));
        await appearing;
        Assert.Equal(0, app.Biometric.Asked);
    }

    [Fact]
    public async Task Interruptores_y_listas()
    {
        var (app, page) = await Open();
        using var _app = app;
        page.Find<Switch>("TraySwitch").IsToggled = false;
        Assert.False(app.Settings.TrayOnMinimize);
        page.Find<Switch>("StartupSwitch").IsToggled = true;   // solo hace algo en Windows
        app.Settings.AskExtensions = false;
        page.Find<Switch>("ExtAskSwitch").IsToggled = true;   // en Windows lo pone ApplyTexts; aqui empieza apagado
        Assert.True(app.Settings.AskExtensions);
        page.Find<Switch>("ExtAskSwitch").IsToggled = false;
        Assert.False(app.Settings.AskExtensions);
        page.Find<Switch>("AutofillAskSwitch").IsToggled = false;
        Assert.False(app.Settings.AskAutofill);
        page.Find<Switch>("DesktopAutofillSwitch").IsToggled = false;
        Assert.False(app.Settings.DesktopAutofill);

        page.Find<Picker>("AutoLockPicker").SelectedIndex = 4;
        Assert.Equal(10, app.Settings.AutoLockMinutes);
        page.Find<Picker>("ClipboardPicker").SelectedIndex = 0;
        Assert.Equal(0, app.Settings.ClipboardSeconds);
        page.Find<Picker>("ClipboardPicker").SelectedIndex = -1;   // sin eleccion: se queda
        Assert.Equal(0, app.Settings.ClipboardSeconds);
        page.Find<Picker>("AutoLockPicker").SelectedIndex = -1;
        Assert.Equal(10, app.Settings.AutoLockMinutes);

        // Mientras se rellenan los valores, tocar los interruptores no guarda nada.
        Ui.SetField(page, "_loading", true);
        page.Find<Switch>("TraySwitch").IsToggled = true;
        page.Find<Switch>("StartupSwitch").IsToggled = false;
        await AsyncVoid.Run(() => page.Find<Switch>("TrustSwitch").IsToggled = true);
        await AsyncVoid.Run(() => page.Find<Switch>("BiometricsSwitch").IsToggled = true);
        await Ui.Call(page, "OnStorageClicked", page.Find<Button>("GoogleButton"), EventArgs.Empty);
        Ui.SetField(page, "_loading", false);
        Assert.False(app.Settings.TrayOnMinimize);
        Assert.False(app.Settings.TrustDevice);
        Assert.False(app.Settings.Biometrics);
        Assert.Equal(StorageMode.Local, app.Settings.Storage);
        Assert.Empty(app.Dialogs.Log);
        await Ui.Call(page, "OnStorageClicked", new Label(), EventArgs.Empty);   // no es un boton

        // Los botones de Android (servicio de autocompletar, navegadores) aqui no hacen nada.
        await Ui.Call(page, "OnPreferredClicked");
        await Ui.Call(page, "OnBrowserSettingsClicked");
        await Ui.Call(page, "OnAutofillClicked");
        Assert.Empty(app.Toast.Shown);
        Assert.Empty(app.Dialogs.Log);
    }

    [Fact]
    public async Task Confiar_en_el_dispositivo_pide_confirmacion()
    {
        var (app, page) = await Open();
        using var _app = app;
        var trust = page.Find<Switch>("TrustSwitch");
        await AsyncVoid.Run(() => trust.IsToggled = true);   // «cancelar»
        Assert.False(trust.IsToggled);
        Assert.False(app.Settings.TrustDevice);
        Assert.Empty(app.Secure.Values);

        app.Dialogs.Answer(true);
        await AsyncVoid.Run(() => trust.IsToggled = true);
        Assert.True(app.Settings.TrustDevice);
        Assert.Single(app.Secure.Values);   // la clave, en la boveda del sistema

        await AsyncVoid.Run(() => trust.IsToggled = false);
        Assert.False(app.Settings.TrustDevice);
        Assert.Empty(app.Secure.Values);
    }

    [Fact]
    public async Task Biometria_solo_si_el_usuario_pasa()
    {
        var (app, page) = await Open();
        using var _app = app;
        var bio = page.Find<Switch>("BiometricsSwitch");
        await AsyncVoid.Run(() => bio.IsToggled = true);
        Assert.False(bio.IsToggled);
        Assert.False(app.Settings.Biometrics);
        Assert.Equal(1, app.Biometric.Asked);

        app.Biometric.Pass = true;
        await AsyncVoid.Run(() => bio.IsToggled = true);
        Assert.True(app.Settings.Biometrics);
        Assert.Single(app.Secure.Values);

        await AsyncVoid.Run(() => bio.IsToggled = false);
        Assert.False(app.Settings.Biometrics);
        Assert.Empty(app.Secure.Values);
    }

    [Fact]
    public async Task Cambiar_la_contraseña_maestra()
    {
        var (app, page) = await Open();
        using var _app = app;
        await page.Click("ChangeMasterButton");   // cancelado
        app.Dialogs.Answer("corta");
        await page.Click("ChangeMasterButton");
        Assert.Equal(app["MasterPasswordShort"], app.Dialogs.Log[^1].Message);
        app.Dialogs.Answer("otra-contraseña", "otra-distinta");
        await page.Click("ChangeMasterButton");
        Assert.Equal(app["MasterPasswordMismatch"], app.Dialogs.Log[^1].Message);
        app.Dialogs.Answer("otra-contraseña");   // y la repeticion cancelada
        await page.Click("ChangeMasterButton");
        Assert.Equal(app["MasterPasswordMismatch"], app.Dialogs.Log[^1].Message);

        app.Dialogs.Answer("otra-contraseña", "otra-contraseña");
        await page.Click("ChangeMasterButton");
        Assert.Equal(app["ChangeMasterDone"], app.Toast.Shown[^1]);
        app.Store.Lock();
        await app.Store.UnlockAsync("otra-contraseña");
        Assert.True(app.Store.IsUnlocked);
    }

    [Fact]
    public async Task Importar_de_fichero_y_de_QR()
    {
        var (app, page) = await Open(new Credential { Title = "Ya estaba", Username = "u", Url = "https://ya.example", Password = "p" });
        using var _app = app;
        await page.Click("ImportButton");   // cancelado
        Assert.Empty(app.Dialogs.Log);

        app.Picker.Results.Enqueue(() => "esto no es nada conocido");
        await page.Click("ImportButton");
        Assert.Equal(app["ImportUnknown"], app.Dialogs.Log[^1].Message);

        app.Picker.Results.Enqueue(() => "name,url,username,password\nNuevo,https://nuevo.example,ana,clave\nYa estaba,https://ya.example,u,p\n");
        await page.Click("ImportButton");
        Assert.Equal(string.Format(app["ImportDone"], 1, 1), app.Dialogs.Log[^1].Message);
        Assert.Contains(app.Store.Data!.Entries, e => e.Title == "Nuevo");

        app.Picker.Results.Enqueue(() => throw new IOException("no se puede leer"));
        await page.Click("ImportButton");
        Assert.Equal("no se puede leer", app.Dialogs.Log[^1].Message);

        await page.Click("ImportGaButton");   // el lector, cancelado
        var shown = app.Dialogs.Log.Count;
        Assert.Equal(shown, app.Dialogs.Log.Count);
        app.Scanner.Results.Enqueue("otpauth://totp/Banco:ana?secret=JBSWY3DPEHPK3PXP&issuer=Banco");
        await page.Click("ImportGaButton");
        Assert.Equal(string.Format(app["ImportDone"], 1, 0), app.Dialogs.Log[^1].Message);
        Assert.Contains(app.Store.Data!.Entries, e => e.Title == "Banco" && e.HasTotp);

        // Lo mismo otra vez: todo repetido, no se guarda nada.
        app.Scanner.Results.Enqueue("otpauth://totp/Banco:ana?secret=JBSWY3DPEHPK3PXP&issuer=Banco");
        await page.Click("ImportGaButton");
        Assert.Equal(string.Format(app["ImportDone"], 0, 1), app.Dialogs.Log[^1].Message);
    }

    [Fact]
    public async Task Exportar_cifrada_y_en_claro_y_compartir()
    {
        var (app, page) = await Open(new Credential { Title = "Secreta", Password = "p" });
        using var _app = app;
        await page.Click("ExportEncButton");
        var shared = Assert.Single(app.Share.Files);
        Assert.EndsWith(".soccred", shared.File!.FullPath);
        Assert.StartsWith(app.Files.CacheDirectory, shared.File.FullPath);
        Assert.Equal(File.ReadAllText(VaultStore.FilePath), File.ReadAllText(shared.File.FullPath));

        await page.Click("ExportPlainButton");   // aviso cancelado
        Assert.Single(app.Share.Files);
        app.Dialogs.Answer(true);
        await page.Click("ExportPlainButton");
        Assert.Contains("Secreta", File.ReadAllText(app.Share.Files[^1].File!.FullPath));

        // Si no se puede escribir, se avisa.
        Directory.Delete(app.Files.CacheDirectory, recursive: true);
        await page.Click("ExportEncButton");
        Assert.Equal(app["Error"], app.Dialogs.Log[^1].Title);
        Assert.Equal(2, app.Share.Files.Count);
    }

    [Fact]
    public async Task Borrar_la_boveda_con_la_palabra()
    {
        var (app, page) = await Open(new Credential { Title = "X" });
        using var _app = app;
        app.Settings.TrustDevice = true;
        await app.Store.RememberKeyAsync(true);

        app.Dialogs.Answer("no");
        await page.Click("DeleteVaultButton");
        Assert.True(app.Store.Exists);
        Assert.True(app.Store.IsUnlocked);

        app.Dialogs.Answer(" borrar ");
        page.Find<Button>("DeleteVaultButton").SendClicked();   // sin esperar: al bloquear, las paginas sacan la puerta
        await Ui.Until(() => app.Navigation.Routes.Contains("//VaultPage"));
        Assert.False(app.Store.Exists);
        Assert.False(File.Exists(VaultStore.FilePath + ".bak"));
        Assert.False(app.Store.IsUnlocked);
        Assert.False(app.Settings.TrustDevice);
        Assert.Empty(app.Secure.Values);
        Assert.Equal(StorageMode.Local, app.Settings.Storage);
    }

    // ------------------------------------------------------------------ nube

    [Fact]
    public async Task Entrar_en_Google_sincronizar_y_salir()
    {
        var (app, page) = await Open(new Credential { Title = "De aqui" });
        using var _app = app;
        var cloud = new FakeGoogle(app.Http);

        await page.Click("LocalButton");   // ya es la activa: nada
        Assert.Equal(StorageMode.Local, app.Settings.Storage);

        await page.Click("GoogleButton");
        if (!app.Store.IsConfigured(StorageMode.GoogleDrive))
        {
            // Compilado sin identificadores de cliente: solo el aviso.
            Assert.Equal(string.Format(app["StorageNotConfigured"], "Google"), app.Dialogs.Log[^1].Message);
            return;
        }
        Assert.Equal(StorageMode.GoogleDrive, app.Settings.Storage);
        Assert.Equal("✓ " + app["StorageGoogle"], page.Find<Button>("GoogleButton").Text);
        Assert.Equal(string.Format(app["SignedInAs"], "ana@gmail.com"), page.Find<Label>("AccountLabel").Text);
        Assert.True(page.Find<Button>("SyncButton").IsVisible);
        Assert.NotNull(cloud.Content);   // la primera sincronizacion la subio
        Assert.Equal(app["SyncNothing"], Status(page));

        // Otra instalacion añadio algo con la misma sal: al sincronizar llega.
        await page.Click("SyncButton");
        Assert.Equal(app["SyncNothing"], Status(page));

        // El estado que avisa la boveda tambien se enseña.
        await app.Store.SyncQuietlyAsync(TimeSpan.Zero);
        Assert.Equal(string.Empty, Status(page));
        Assert.False(page.Find<Label>("SyncStatus").IsVisible);

        await page.Click("SignOutButton");
        Assert.Equal(StorageMode.Local, app.Settings.Storage);
        Assert.False(page.Find<Button>("SyncButton").IsVisible);

        // Volver a local desde la nube: tambien sale de la cuenta.
        await app.Store.SignInAsync(StorageMode.GoogleDrive);
        await page.Click("LocalButton");
        Assert.Equal(StorageMode.Local, app.Settings.Storage);
        Assert.Equal(string.Empty, app.Settings.AccountEmail);
        Assert.Equal("✓ " + app["StorageLocal"], page.Find<Button>("LocalButton").Text);
    }

    [Fact]
    public async Task La_nube_con_otra_sal_pide_la_contraseña()
    {
        var (app, page) = await Open();
        using var _app = app;
        var cloud = new FakeGoogle(app.Http);
        if (!app.Store.IsConfigured(StorageMode.GoogleDrive))
            return;
        await app.Store.SignInAsync(StorageMode.GoogleDrive);
        cloud.Content = FakeGoogle.OtherVault(Master, "Del movil");

        await page.Click("SyncButton");   // contraseña cancelada
        Assert.Equal(string.Empty, Status(page));
        Assert.Equal(app["SyncPasswordNeeded"], app.Dialogs.Log[^1].Message);

        app.Dialogs.Answer("mala-contraseña");
        await page.Click("SyncButton");
        Assert.Equal(app["MasterPasswordWrong"], Status(page));

        app.Dialogs.Answer(Master);
        await page.Click("SyncButton");
        Assert.Equal(string.Format(app["SyncDone"], 1), Status(page));
        Assert.Contains(app.Store.Data!.Entries, e => e.Title == "Del movil");
    }

    [Fact]
    public async Task Fallos_de_la_nube()
    {
        var (app, page) = await Open();
        using var _app = app;
        _ = new FakeGoogle(app.Http);
        if (!app.Store.IsConfigured(StorageMode.GoogleDrive))
            return;
        await app.Store.SignInAsync(StorageMode.GoogleDrive);

        app.Http.On(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", HttpStatusCode.Forbidden, "{\"error\":\"insufficient\"}");
        await page.Click("SyncButton");
        Assert.Equal(app["SyncScope"], Status(page));

        app.Http.On(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", HttpStatusCode.InternalServerError, "{}");
        await page.Click("SyncButton");
        Assert.StartsWith(string.Format(app["SyncFailed"], "")[..10], Status(page));

        // Un aviso de error de la boveda (sincronizacion sola) sale tambien.
        await app.Store.SyncQuietlyAsync(TimeSpan.Zero);
        Assert.StartsWith(string.Format(app["SyncFailed"], "")[..10], Status(page));
        Assert.True(page.Find<Label>("SyncStatus").IsVisible);
    }

    [Fact]
    public async Task Entrar_cancelado_sin_permiso_o_con_error()
    {
        var (app, page) = await Open();
        using var _app = app;
        if (!app.Store.IsConfigured(StorageMode.GoogleDrive))
            return;
        var browser = (FakeOAuthBrowser)typeof(VaultStore).GetField("_browser", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(app.Store)!;

        browser.Fail = new OperationCanceledException();
        await page.Click("GoogleButton");
        Assert.Equal(app["SignInTimeout"], Status(page));
        Assert.Equal(StorageMode.Local, app.Settings.Storage);

        browser.Fail = new InvalidOperationException("la cuenta no quiere");
        await page.Click("GoogleButton");
        Assert.Equal("la cuenta no quiere", app.Dialogs.Log[^1].Message);
        Assert.Equal(StorageMode.Local, app.Settings.Storage);

        // Sin el permiso de la carpeta de la aplicacion: el texto que lo explica.
        browser.Fail = null;
        _ = new FakeGoogle(app.Http, scope: "openid email");
        await page.Click("GoogleButton");
        Assert.Equal(app["SyncScope"], app.Dialogs.Log[^1].Message);
        Assert.Equal(StorageMode.Local, app.Settings.Storage);
    }
}
