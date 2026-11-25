using System.Reflection;
using Credentials.Pages;
using Credentials.Platforms.Windows;
using Credentials.Services;

namespace Credentials.Ui.Tests;

/// <summary>
/// Lo que antes iba en bloques <c>#if WINDOWS</c> / <c>#if ANDROID</c> de App y de las paginas, ahora
/// detras de IDesktopIntegration e IAndroidIntegration: cada rama, con la aplicacion montada como si
/// fuera la de Windows (DesktopIntegration de verdad sobre un escritorio de mentira) o la de Android.
/// </summary>
public class PlatformTests
{
    private const string Master = "contraseña-larga";

    private static Window NewWindow(TestHost app) => (Window)((IApplication)app.App).CreateWindow(null);

    /// <summary>Como cuando MAUI pone la ventana nativa (HandlerChanged).</summary>
    private static void NativeReady(Window window) =>
        ((EventHandler?)typeof(Element).GetField("HandlerChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window))?.Invoke(window, EventArgs.Empty);

    private static void TutorialOpening(bool value) =>
        typeof(App).GetProperty("TutorialOpening", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static T F<T>(object page, string name) => Ui.Field<T>(page, name);

    private static void Go(TutorialPage page, int index) =>
        typeof(TutorialPage).GetMethod("Go", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [index]);

    private static string RunValue(TestHost app) =>
        app.Registry.Values.GetValueOrDefault((RegistryRoot.CurrentUser, StartupRegistration.RunKey, DesktopIntegration.StartupValueName)) ?? string.Empty;

    // ------------------------------------------------------------------ Ajustes

    [Fact]
    public async Task Ajustes_en_Windows_bandeja_arranque_y_extensiones()
    {
        using var app = TestHost.Windows();
        await app.CreateVaultAsync(Master);
        app.DesktopHost.AddBrowser("msedge.exe");
        app.DesktopHost.AddBrowser("firefox.exe");
        app.DesktopHost.AddBrowser("chrome.exe");   // no se admite: no sale
        app.Settings.SetExtensionSeen("edge");
        app.Desktop.StartWithWindows = true;
        Assert.Equal("\"C:\\sOC\\sOCCredentials.exe\" --tray", RunValue(app));

        var page = new SettingsPage();
        Assert.True(page.Find<VisualElement>("WindowsCard").IsVisible);
        Assert.True(page.Find<Switch>("StartupSwitch").IsToggled);
        Assert.Equal(app["ExtSection"], page.Find<Label>("ExtTitle").Text);
        Assert.Equal(app["ExtSectionHint"], page.Find<Label>("ExtHint").Text);
        Assert.True(page.Find<VisualElement>("ExtensionsCard").IsVisible);
        Assert.False(page.Find<Button>("ExtGetExeButton").IsVisible);
        Assert.True(page.Find<VisualElement>("ExtAskRow").IsVisible);
        Assert.True(page.Find<Switch>("ExtAskSwitch").IsToggled);
        // En Windows el autocompletar es el de las aplicaciones del escritorio: sin estado ni boton de Android.
        Assert.True(page.Find<VisualElement>("AutofillCard").IsVisible);
        Assert.False(page.Find<Label>("AutofillStatus").IsVisible);
        Assert.False(page.Find<Button>("AutofillButton").IsVisible);
        Assert.False(page.Find<VisualElement>("AutofillAskRow").IsVisible);

        // Una fila por navegador: el que ya conecto, marcado; el boton de instalar de cada uno.
        var rows = page.Find<VerticalStackLayout>("BrowsersBox").Cast<Grid>().ToList();
        Assert.Equal(2, rows.Count);
        Label Text(Grid row, int i) => (Label)((VerticalStackLayout)row.Children[0])[i];
        Assert.Equal("Microsoft Edge", Text(rows[0], 0).Text);
        Assert.Equal("✓ " + app["ExtInstalled"], Text(rows[0], 1).Text);
        Assert.Equal("Firefox", Text(rows[1], 0).Text);
        Assert.Equal(app["ExtNotInstalled"], Text(rows[1], 1).Text);
        var install = (Button)rows[1].Children[1];
        Assert.Same(app.App.Resources["PrimaryButton"], install.Style);

        // «Instalar…» en Firefox: registra el host y abre su ficha de la tienda.
        app.Dialogs.Answer(true);
        await AsyncVoid.Run(install.SendClicked);
        Assert.Equal(string.Format(app["ExtInstallIn"], "Firefox"), app.Dialogs.Log[^1].Title);
        var firefox = app.DesktopHost.Opened.Single();
        Assert.Equal(ExtensionInstallerCore.FirefoxStoreUrl, firefox.Url);
        Assert.True(app.DesktopHost.Extensions.IsRegistered(app.DesktopHost.Extensions.Detected()[1]));
        Assert.Equal(2, page.Find<VerticalStackLayout>("BrowsersBox").Count);   // rehecha

        // Al aparecer, sin huella ni capturas (no hay en Windows).
        await page.Appear();
        Assert.False(page.Find<VisualElement>("BiometricsRow").IsVisible);
        Assert.False(page.Find<VisualElement>("ScreenshotsRow").IsVisible);

        // Quitar «Arrancar con Windows» borra el valor de la clave Run; ponerlo, lo vuelve a escribir.
        page.Find<Switch>("StartupSwitch").IsToggled = false;
        Assert.Equal(string.Empty, RunValue(app));
        page.Find<Switch>("StartupSwitch").IsToggled = true;
        Assert.Contains("--tray", RunValue(app));

        // La bandeja: el ajuste y, si ya hay icono, al momento.
        page.Find<Switch>("TraySwitch").IsToggled = false;
        Assert.False(app.Settings.TrayOnMinimize);
        var window = NewWindow(app);
        NativeReady(window);
        Assert.True(app.Desktop.Tray!.MinimizeToTray == app.Settings.TrayOnMinimize);
        page.Find<Switch>("TraySwitch").IsToggled = true;
        Assert.True(app.Desktop.Tray.MinimizeToTray);
        page.Find<Switch>("TraySwitch").IsToggled = false;
        Assert.False(app.Desktop.Tray.MinimizeToTray);
    }

    [Fact]
    public async Task Ajustes_en_Windows_version_de_la_Store_y_sin_extension()
    {
        using var app = TestHost.Windows();
        await app.CreateVaultAsync(Master);
        app.DesktopHost.AddBrowser("msedge.exe");
        app.DesktopHost.IsPackaged = true;
        var page = new SettingsPage();
        Assert.Equal(app["ExtPackagedHint"], page.Find<Label>("ExtHint").Text);
        Assert.True(page.Find<VisualElement>("ExtensionsCard").IsVisible);
        Assert.True(page.Find<Button>("ExtGetExeButton").IsVisible);
        Assert.False(page.Find<VisualElement>("ExtAskRow").IsVisible);
        Assert.Empty(page.Find<VerticalStackLayout>("BrowsersBox"));
        await page.Click("ExtGetExeButton");
        Assert.Equal(PackageIdentity.ReleasesUrl, app.Browser.Opened.Single().ToString().TrimEnd('/'));

        // Version exe sin la extension dentro (un Debug sin el host): ni tarjeta.
        app.DesktopHost.IsPackaged = false;
        File.Delete(Path.Combine(app.DesktopHost.AppDir, "CredentialsHost.exe"));
        var bare = new SettingsPage();
        Assert.False(bare.Find<VisualElement>("ExtensionsCard").IsVisible);
        Assert.Equal(app["ExtSectionHint"], bare.Find<Label>("ExtHint").Text);
    }

    [Fact]
    public async Task Ajustes_en_Android_autocompletar_servicio_preferido_y_navegadores()
    {
        using var app = TestHost.OnAndroid();
        await app.CreateVaultAsync(Master);
        app.Android.HasPreferredService = true;
        app.Android.Browsers.Add(("org.example.uno", "Navegador Uno"));
        app.Android.Browsers.Add(("org.example.dos", "Navegador Dos"));

        var page = new SettingsPage();
        Assert.False(page.Find<VisualElement>("WindowsCard").IsVisible);
        Assert.True(page.Find<VisualElement>("AutofillCard").IsVisible);
        Assert.False(page.Find<VisualElement>("DesktopAutofillRow").IsVisible);
        Assert.Equal(app["AutofillIsOther"], page.Find<Label>("AutofillStatus").Text);
        Assert.Equal(app["AutofillUseThis"], page.Find<Button>("AutofillButton").Text);
        Assert.Same(app.App.Resources["PrimaryButton"], page.Find<Button>("AutofillButton").Style);
        Assert.Equal("ic_key_w.png", ((FileImageSource)page.Find<Button>("AutofillButton").ImageSource).File);
        Assert.True(page.Find<VisualElement>("AutofillAskRow").IsVisible);
        Assert.True(page.Find<Button>("PreferredButton").IsVisible);
        Assert.Equal(app["AutofillPreferredHint"], page.Find<Label>("PreferredHint").Text);
        Assert.True(page.Find<Button>("BrowsersButton").IsVisible);
        Assert.Equal(app["AutofillBrowsersHint"], page.Find<Label>("BrowsersHint").Text);

        // Ya es la nuestra: marcada, boton de cambiar y sin la pregunta al desbloquear.
        app.Android.AutofillIsOurs = true;
        await page.Appear();
        Assert.True(page.Find<VisualElement>("BiometricsRow").IsVisible);
        Assert.Equal("✓ " + app["AutofillIsOurs"], page.Find<Label>("AutofillStatus").Text);
        Assert.Equal(app["AutofillChange"], page.Find<Button>("AutofillButton").Text);
        Assert.Same(app.App.Resources["OutlineButton"], page.Find<Button>("AutofillButton").Style);
        Assert.False(page.Find<VisualElement>("AutofillAskRow").IsVisible);

        // Botones: elegir este servicio (y si el sistema no deja, el error en un aviso).
        await page.Click("AutofillButton");
        app.Android.RequestFails = new InvalidOperationException("sin pantalla de ajustes");
        await page.Click("AutofillButton");
        Assert.Equal("sin pantalla de ajustes", app.Toast.Shown[^1]);
        Assert.Equal(["request", "request"], app.Android.Calls);

        // Servicio preferido: se abre; si no hay pantalla, se avisa.
        await page.Click("PreferredButton");
        Assert.DoesNotContain(app["AutofillPreferredNoScreen"], app.Toast.Shown);
        app.Android.CanOpen = false;
        await page.Click("PreferredButton");
        Assert.Equal(app["AutofillPreferredNoScreen"], app.Toast.Shown[^1]);

        // Navegadores: con dos, se elige; los pasos y sus ajustes.
        app.Android.Calls.Clear();
        app.Android.CanOpen = true;
        app.Dialogs.Answer("Navegador Dos");
        await page.Click("BrowsersButton");
        Assert.Equal(["Navegador Uno", "Navegador Dos"], app.Dialogs.Log[^2].Options);
        Assert.Equal(string.Format(app["AutofillBrowserSteps"], "Navegador Dos"), app.Dialogs.Log[^1].Message);
        Assert.Equal(["browser:org.example.dos"], app.Android.Calls);
        // Cancelar, o un nombre que no esta: nada.
        app.Dialogs.Answer(app["Cancel"]);
        await page.Click("BrowsersButton");
        app.Dialogs.Answer("Otro");
        await page.Click("BrowsersButton");
        Assert.Single(app.Android.Calls);
        // Con uno solo, directo; si no se abre, aviso.
        app.Android.Browsers.RemoveAt(1);
        app.Android.CanOpen = false;
        await page.Appear();
        await page.Click("BrowsersButton");
        Assert.Equal("browser:org.example.uno", app.Android.Calls[^1]);
        Assert.Equal(app["AutofillPreferredNoScreen"], app.Toast.Shown[^1]);
        // Sin navegadores, el boton no hace nada.
        app.Android.Browsers.Clear();
        await page.Appear();
        Assert.False(page.Find<Button>("BrowsersButton").IsVisible);
        await page.Click("BrowsersButton");

        // Capturas: el ajuste y la ventana al momento.
        page.Find<Switch>("ScreenshotsSwitch").IsToggled = !page.Find<Switch>("ScreenshotsSwitch").IsToggled;
        Assert.Equal("screenshots", app.Android.Calls[^1]);

        // Sin servicio de autocompletar en el dispositivo, ni tarjeta.
        app.Android.AutofillSupported = false;
        await page.Appear();
        Assert.False(page.Find<VisualElement>("AutofillCard").IsVisible);
    }

    // ------------------------------------------------------------------ la guia

    [Fact]
    public async Task Guia_en_Windows()
    {
        using var app = TestHost.Windows();
        app.DesktopHost.AddBrowser("msedge.exe");
        app.DesktopHost.AddBrowser("firefox.exe");
        app.Settings.TrayOnMinimize = false;
        var page = new TutorialPage();
        await page.Appear();
        // Bienvenida, bandeja, escritorio, Edge, Firefox, gestor del navegador, nube y fin.
        Assert.Equal(string.Format(app["TutStepOf"], 1, 8), F<Label>(page, "_progress").Text);
        Assert.Equal(app["TutWelcomeWindows"], F<Label>(page, "_body").Text);

        Go(page, 1);
        Assert.Equal(app["TutTrayTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["TutPending"], F<Label>(page, "_state").Text);
        var window = NewWindow(app);
        NativeReady(window);
        app.Desktop.Tray!.MinimizeToTray = false;
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.True(app.Settings.TrayOnMinimize);
        Assert.True(app.Desktop.Tray.MinimizeToTray);
        Assert.Contains("--tray", RunValue(app));
        Assert.Equal("✓ " + app["TutDone"], F<Label>(page, "_state").Text);

        Go(page, 2);
        Assert.Equal(app["TutDesktopTitle"], F<Label>(page, "_title").Text);
        app.Settings.DesktopAutofill = false;
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.True(app.Settings.DesktopAutofill);

        Go(page, 3);
        Assert.Equal(string.Format(app["TutExtTitle"], "Microsoft Edge"), F<Label>(page, "_title").Text);
        Assert.Equal(string.Format(app["TutExtBodyStore"], "Microsoft Edge"), F<Label>(page, "_body").Text);
        Assert.Equal(app["TutPending"], F<Label>(page, "_state").Text);
        app.Dialogs.Answer(true);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal(ExtensionInstallerCore.EdgeStoreUrl, app.DesktopHost.Opened.Single().Url);
        app.Settings.SetExtensionSeen("edge");
        app.Dispatcher.Timers[0].Fire();
        Assert.Equal("✓ " + app["TutDone"], F<Label>(page, "_state").Text);

        Go(page, 5);
        Assert.Equal(app["TutBrowserPmTitle"], F<Label>(page, "_title").Text);
        Assert.DoesNotContain("Chrome", string.Concat(Enumerable.Range(0, 8).Select(i => { Go(page, i); return F<Label>(page, "_title").Text + F<Label>(page, "_body").Text; })));
    }

    [Fact]
    public async Task Guia_en_Windows_version_de_la_Store_y_sin_navegadores()
    {
        using var app = TestHost.Windows();
        app.DesktopHost.AddBrowser("msedge.exe");
        app.DesktopHost.IsPackaged = true;
        var page = new TutorialPage();
        await page.Appear();
        // Bienvenida, bandeja, escritorio, «hace falta la version EXE», nube y fin.
        Assert.Equal(string.Format(app["TutStepOf"], 1, 6), F<Label>(page, "_progress").Text);
        Go(page, 3);
        Assert.Equal(app["TutExtPackagedTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["ExtGetExe"], F<Button>(page, "_action").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal(PackageIdentity.ReleasesUrl, app.Browser.Opened.Single().ToString().TrimEnd('/'));

        // Version exe sin ningun navegador con soporte: lo dice.
        using var empty = TestHost.Windows();
        var none = new TutorialPage();
        await none.Appear();
        Assert.Equal(string.Format(empty["TutStepOf"], 1, 6), F<Label>(none, "_progress").Text);
        Go(none, 3);
        Assert.Equal(empty["TutNoBrowsersTitle"], F<Label>(none, "_title").Text);
        Assert.False(F<Button>(none, "_action").IsVisible);
    }

    [Fact]
    public async Task Guia_en_Android()
    {
        using var app = TestHost.OnAndroid();
        app.Android.HasPreferredService = true;
        app.Android.Browsers.Add(("org.mozilla.firefox", "Firefox"));
        app.Android.Browsers.Add(("org.example.navegador", "Navegador"));
        var page = new TutorialPage();
        await page.Appear();
        // Bienvenida, autocompletar, servicio preferido, dos navegadores, huella, capturas, nube y fin.
        Assert.Equal(string.Format(app["TutStepOf"], 1, 9), F<Label>(page, "_progress").Text);
        Assert.Equal(app["TutWelcomeAndroid"], F<Label>(page, "_body").Text);

        Go(page, 1);
        Assert.Equal(app["TutAutofillTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["TutPending"], F<Label>(page, "_state").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("request", app.Android.Calls[^1]);
        app.Android.AutofillIsOurs = true;
        app.Dispatcher.Timers[0].Fire();
        Assert.Equal("✓ " + app["TutDone"], F<Label>(page, "_state").Text);

        Go(page, 2);
        Assert.Equal(app["TutPreferredTitle"], F<Label>(page, "_title").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("preferred", app.Android.Calls[^1]);
        Assert.Empty(app.Toast.Shown);
        app.Android.CanOpen = false;
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal(app["AutofillPreferredNoScreen"], app.Toast.Shown[^1]);

        Go(page, 3);
        Assert.Equal(string.Format(app["TutBrowserTitle"], "Firefox"), F<Label>(page, "_title").Text);
        Assert.Equal(string.Format(app["TutBrowserFirefox"], "Firefox"), F<Label>(page, "_body").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("browser:org.mozilla.firefox", app.Android.Calls[^1]);
        Assert.Equal(2, app.Toast.Shown.Count);
        app.Android.CanOpen = true;
        Go(page, 4);
        Assert.Equal(string.Format(app["TutBrowserChromium"], "Navegador"), F<Label>(page, "_body").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal("browser:org.example.navegador", app.Android.Calls[^1]);
        Assert.Equal(2, app.Toast.Shown.Count);

        Go(page, 5);
        Assert.Equal(app["TutBioTitle"], F<Label>(page, "_title").Text);
        Assert.Equal(app["TutOptional"], F<Label>(page, "_state").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Go(page, 6);
        Assert.Equal(app["TutShotsTitle"], F<Label>(page, "_title").Text);
        await AsyncVoid.Run(F<Button>(page, "_action").SendClicked);
        Assert.Equal(["//SettingsPage", "//SettingsPage"], app.Navigation.Routes);

        // Sin servicio de autocompletar ni preferido ni navegadores: bienvenida, huella, capturas, nube y fin.
        app.Android.AutofillSupported = false;
        app.Android.HasPreferredService = false;
        app.Android.Browsers.Clear();
        await page.Appear();
        Assert.Equal(string.Format(app["TutStepOf"], 1, 5), F<Label>(page, "_progress").Text);
    }

    // ------------------------------------------------------------------ la App en Windows

    [Fact]
    public async Task App_en_Windows_bandeja_sesion_y_extensiones()
    {
        using var app = TestHost.Windows();
        App.UnlockPause = TimeSpan.Zero;
        TutorialOpening(false);
        app.DesktopSandbox = false;
        app.Settings.TutorialDone = true;
        app.Settings.Biometrics = true;    // de cuando habia Windows Hello: fuera
        app.Settings.TrayOnMinimize = false;
        app.DesktopHost.AddBrowser("msedge.exe");
        await app.CreateVaultAsync(Master);
        app.Store.Lock();

        // Sin icono todavia: «enseñate» trae la ventana como se pueda.
        app.Desktop.RequestUnlock();
        Assert.Equal(1, app.DesktopHost.BroughtToFront);

        app.DesktopHost.CommandLine = ["Credentials.exe", "--tray"];
        var window = NewWindow(app);
        Assert.False(app.Settings.Biometrics);
        Assert.Equal(900, window.Width);
        Assert.Equal(760, window.Height);

        // La ventana nativa llega en el segundo HandlerChanged.
        app.DesktopHost.NoNativeWindow = true;
        NativeReady(window);
        Assert.Null(app.Desktop.Tray);
        app.DesktopHost.NoNativeWindow = false;
        NativeReady(window);
        NativeReady(window);
        Assert.Equal(1, app.DesktopHost.TraysCreated);
        var tray = app.DesktopHost.Tray!;
        Assert.False(tray.MinimizeToTray);
        Assert.Equal(app["TrayOpen"], tray.Text("TrayOpen"));
        Assert.Equal(TimeSpan.FromSeconds(3), VaultStore.SystemIdle!());
        Assert.Equal(1, app.DesktopHost.AutofillStarted);
        Assert.Equal(1, app.DesktopHost.TaskbarApplied);
        Assert.Empty(tray.Calls);   // con --tray y la boveda cerrada, la ventana sale para la contraseña

        // La extension conecta: aviso con el nombre del navegador.
        app.DesktopHost.BrowserConnected!("edge");
        app.DesktopHost.BrowserConnected!("otro");
        Assert.Equal([string.Format(app["ExtConnected"], "Microsoft Edge"), string.Format(app["ExtConnected"], "otro")], app.Toast.Shown);

        // Abierta la boveda: se ofrece la extension y la ventana vuelve a la bandeja.
        app.Dialogs.Answer(app["NotNow"]);
        await AsyncVoid.Run(() => app.Store.UnlockAsync(Master).GetAwaiter().GetResult());
        await Ui.Until(() => tray.Calls.Contains("show:0"));
        Assert.Equal(string.Format(app["ExtOfferTitle"], "Microsoft Edge"), app.Dialogs.Log.Single().Title);
        Assert.True(tray.IsHidden);

        // Win+L: se cierra (sin confianza); al volver, sale de la bandeja con la puerta.
        tray.LockSession();
        Assert.False(app.Store.IsUnlocked);
        tray.UnlockSession();
        Assert.Contains("show:9", tray.Calls);
        var gate = await Ui.GateOn(window.Page!);
        await Ui.UnlockThrough(window.Page!, Master);
        Assert.True(app.Store.IsUnlocked);
        await Ui.Until(() => tray.IsHidden);   // salio de la bandeja solo para la contraseña: vuelve

        // Con la boveda abierta, volver a la sesion no hace nada; con confianza, Win+L no la cierra.
        var calls = tray.Calls.Count;
        tray.UnlockSession();
        Assert.Equal(calls, tray.Calls.Count);
        app.Settings.TrustDevice = true;
        tray.LockSession();
        Assert.True(app.Store.IsUnlocked);

        // La extension pide la boveda: con icono, la ventana sale de la bandeja.
        app.Desktop.RequestUnlock();
        Assert.Equal(["show:9", "front", "remove"], tray.Calls.TakeLast(3));
        Assert.Equal(1, app.DesktopHost.BroughtToFront);
        app.Settings.TrustDevice = false;
        app.Store.Lock();
        app.Desktop.RequestUnlock();
        Ui.Dismiss(window.Page!, await Ui.GateOn(window.Page!));
        await Ui.Until(() => !Ui.GateShowing);

        // «Salir» del menu de la bandeja.
        tray.Handle(TrayController.WmCommand, TrayController.IdExit, 0);
        Assert.True(app.DesktopHost.ExitCalled);
    }

    [Fact]
    public async Task App_en_Windows_segun_como_arranca()
    {
        // --background (lo abre el navegador): a la bandeja sin pedir nada.
        using (var app = TestHost.Windows())
        {
            app.DesktopSandbox = false;
            app.DesktopHost.CommandLine = ["Credentials.exe", "--background"];
            NativeReady(NewWindow(app));
            Assert.Equal(["add", "show:0"], app.DesktopHost.Tray!.Calls);
        }
        // --tray con la boveda ya abierta (confianza): tambien a la bandeja.
        using (var app = TestHost.Windows())
        {
            await app.CreateVaultAsync(Master);
            app.DesktopSandbox = false;
            app.DesktopHost.CommandLine = ["Credentials.exe", "--tray"];
            NativeReady(NewWindow(app));
            Assert.True(app.DesktopHost.Tray!.IsHidden);
        }
        // Arranque normal, version de la Store, sin autocompletar posible y en modo de pruebas
        // (SOC_SANDBOX): ni identidad de barra, ni autocompletar, ni extensiones; no falla.
        using (var app = TestHost.Windows())
        {
            app.DesktopHost.IsPackaged = true;
            app.DesktopHost.AutofillFails = true;
            NativeReady(NewWindow(app));
            Assert.Empty(app.DesktopHost.Tray!.Calls);
            Assert.Equal(0, app.DesktopHost.TaskbarApplied);
            Assert.Equal(0, app.DesktopHost.AutofillStarted);
            Assert.Null(app.DesktopHost.RequestUnlock);
            app.DesktopSandbox = false;
            app.Desktop.AttachWindow(NewWindow(app));
        }
        using (var app = TestHost.Windows())
        {
            app.DesktopSandbox = false;
            app.DesktopHost.AutofillFails = true;
            app.Registry.Broken = true;     // ni el registro: las extensiones no arrancan y no pasa nada
            NativeReady(NewWindow(app));
            Assert.Equal(0, app.DesktopHost.AutofillStarted);
            Assert.NotNull(app.DesktopHost.RequestUnlock);
        }
        using (var app = TestHost.Windows())
        {
            // La tuberia no se puede abrir (otra aplicacion la tiene): la ventana sigue sin extensiones.
            app.DesktopSandbox = false;
            app.DesktopHost.ExtensionServerFails = true;
            NativeReady(NewWindow(app));
            Assert.Null(app.DesktopHost.RequestUnlock);
            Assert.Equal(1, app.DesktopHost.TraysCreated);
        }
        using (var app = TestHost.Windows())
        {
            // Sin icono y sin poder traer la ventana: pedir la boveda no lanza.
            app.DesktopHost.BringToFrontFails = true;
            app.Desktop.RequestUnlock();
            Assert.Equal(0, app.DesktopHost.BroughtToFront);
        }
        // Con Windows Hello y confianza, la clave se queda (la usa la confianza).
        using (var app = TestHost.Windows())
        {
            await app.CreateVaultAsync(Master);
            app.Settings.Biometrics = true;
            app.Settings.TrustDevice = true;
            await app.Store.RememberKeyAsync(true);
            NewWindow(app);
            Assert.False(app.Settings.Biometrics);
            Assert.True(app.Store.HasStoredKey);
        }
    }

    [Fact]
    public async Task App_en_Windows_con_la_guia_no_pregunta_ni_vuelve_a_la_bandeja()
    {
        using var app = TestHost.Windows();
        App.UnlockPause = TimeSpan.Zero;
        app.DesktopSandbox = false;
        app.DesktopHost.AddBrowser("msedge.exe");
        app.DesktopHost.CommandLine = ["Credentials.exe", "--tray"];
        var window = NewWindow(app);
        NativeReady(window);
        // Primera vez: sale la guia (TutorialDone falso), que calla la oferta.
        await AsyncVoid.Run(() => app.Store.CreateAsync(Master).GetAwaiter().GetResult());
        await Ui.Until(() => app.Navigation.Routes.Contains("//TutorialPage"));
        await Task.Delay(50);
        Assert.Empty(app.Dialogs.Log);
        Assert.False(app.DesktopHost.Tray!.IsHidden);
    }

    // ------------------------------------------------------------------ la App en Android

    [Fact]
    public async Task App_en_Android_ofrece_el_autocompletar_tras_desbloquear()
    {
        using var app = TestHost.OnAndroid();
        App.UnlockPause = TimeSpan.Zero;
        app.Settings.TutorialDone = true;
        TutorialOpening(false);
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var window = NewWindow(app);
        Assert.NotEqual(900, window.Width);   // el tamaño es cosa del escritorio
        await AsyncVoid.Run(() => app.Store.UnlockAsync(Master).GetAwaiter().GetResult());
        await Ui.Until(() => app.Android.Calls.Contains("offer"));

        // Con la guia saliendo, no.
        TutorialOpening(true);
        app.Android.Calls.Clear();
        app.Store.Lock();
        await AsyncVoid.Run(() => app.Store.UnlockAsync(Master).GetAwaiter().GetResult());
        await Task.Delay(50);
        Assert.Empty(app.Android.Calls);
        TutorialOpening(false);
    }

    [Fact]
    public async Task Atras_en_Android_boveda_y_puerta()
    {
        using var app = TestHost.OnAndroid();
        await app.CreateVaultAsync(Master);
        var vault = new VaultPage();
        Assert.True(vault.Back());
        Assert.Equal("back", app.Android.Calls[^1]);

        app.Store.Lock();
        var appearing = vault.Appear();
        var gate = await Ui.GateOn(vault);
        // En la aplicacion: al fondo, la puerta sigue.
        Assert.True(gate.Back());
        Assert.Equal("gateback", app.Android.Calls[^1]);
        Assert.Single(vault.Navigation.ModalStack);
        // Rellenando otra aplicacion: la puerta se cierra sin abrir.
        app.Android.Back = GateBackAction.CloseGate;
        Assert.True(gate.Back());
        Assert.True(Ui.Field<bool>(gate, "_closed"));
        Ui.Dismiss(vault, gate);
        await appearing;
        Assert.False(app.Store.IsUnlocked);
    }

    // ------------------------------------------------------------------ la ventanita de la contraseña (Windows)

    [Fact]
    public async Task Puerta_en_Windows_ventana_pequeña_y_vuelta()
    {
        using var app = TestHost.Windows();
        var frame = new FakeFrame();
        app.DesktopHost.Frame = frame;
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new ContentPage();
        _ = new Window(page);

        // Pide la contraseña en pequeño abajo a la derecha (420×340 a escala 1,5, margen 12).
        var unlocking = Gate.EnsureUnlockedAsync(page);
        var gate = await Ui.GateOn(page);
        Assert.Equal("move:1272,512,630,510", frame.Calls[0]);
        // Al desbloquearla, sin la puerta, la ventana se minimiza con su tamaño de antes para cuando vuelva.
        Assert.False(gate.Find<Image>("Logo").IsVisible);
        Assert.False(gate.Find<Label>("TitleLabel").IsVisible);
        await Ui.UnlockThrough(page, Master);
        Assert.True(await unlocking);
        Assert.Equal("minimize:100,80,900,760:False", frame.Calls[^1]);

        // Maximizada y sin abrir: vuelve a su sitio y maximizada.
        app.Store.Lock();
        frame.Calls.Clear();
        frame.Bounds = new FrameRect(0, 0, 1920, 1040);
        frame.IsMaximized = true;
        unlocking = Gate.EnsureUnlockedAsync(page);
        gate = await Ui.GateOn(page);
        Assert.Equal(["restore", "move:1272,512,630,510"], frame.Calls);

        // Al volver de la bandeja (Win+L) la ventana se habia restaurado grande: otra vez pequeña,
        // y el alto ajustado a lo que hay dentro, anclado abajo.
        gate.Layout(new Rect(0, 0, 420, 340));      // al tomar tamaño se ajusta el alto, anclada abajo
        Assert.Equal(1022, frame.Bounds.Y + frame.Bounds.Height);
        frame.Bounds = new FrameRect(0, 0, 1920, 1040);
        frame.IsMaximized = true;
        frame.Calls.Clear();
        app.Desktop.RecompactGate();
        Assert.Equal("restore", frame.Calls[0]);
        Assert.Equal("move:1272,512,630,510", frame.Calls[1]);
        Assert.Equal(3, frame.Calls.Count);
        Assert.Equal(1022, frame.Bounds.Y + frame.Bounds.Height);   // misma base: 512 + 510
        Assert.Equal(1272, frame.Bounds.X);
        var fitted = frame.Bounds;
        // Ya a su alto: volver a ajustar no la mueve; con el mismo contenido ni se mira.
        gate.RefitHeight();
        app.Desktop.FitGateHeight(gate, 120);
        var count = frame.Calls.Count;
        app.Desktop.FitGateHeight(gate, 120.4);
        Assert.Equal(count, frame.Calls.Count);
        gate.RefitHeight();
        Assert.Equal(fitted, frame.Bounds);
        // Si la ventana falla al recolocarla, se queda como esta.
        frame.Broken = true;
        app.Desktop.RecompactGate();
        frame.Broken = false;

        Ui.Dismiss(page, gate);
        Assert.False(await unlocking);
        Assert.Equal("move:0,0,1920,1040", frame.Calls[^2]);
        Assert.Equal("maximize", frame.Calls[^1]);

        // Sin puerta a la vista, recolocar no hace nada.
        frame.Calls.Clear();
        app.Desktop.RecompactGate();
        Assert.Empty(frame.Calls);
    }

    [Fact]
    public async Task Puerta_en_Windows_si_la_ventana_falla_se_queda_como_esta()
    {
        using var app = TestHost.Windows();
        await app.CreateVaultAsync(Master);
        app.Store.Lock();
        var page = new ContentPage();
        _ = new Window(page);

        // Sin ventana nativa: ni se encoge ni se ajusta (y sin tamaño todavia, ni se mide).
        var unlocking = Gate.EnsureUnlockedAsync(page);
        var gate = await Ui.GateOn(page);
        gate.RefitHeight();
        gate.Layout(new Rect(0, 0, 420, 340));
        gate.RefitHeight();
        Ui.Dismiss(page, gate);
        Assert.False(await unlocking);

        // La ventana se ha cerrado por medio: ni encoger, ni volver, ni ajustar lanzan.
        var frame = new FakeFrame { Broken = true };
        app.DesktopHost.Frame = frame;
        unlocking = Gate.EnsureUnlockedAsync(page);
        gate = await Ui.GateOn(page);
        gate.Layout(new Rect(0, 0, 420, 340));
        gate.RefitHeight();
        app.Desktop.RecompactGate();
        Ui.Dismiss(page, gate);
        Assert.False(await unlocking);
        Assert.Empty(frame.Calls);
        Assert.Null(app.Desktop.CompactForGate(null));

        // Abierta y con la ventana rota al volver: tampoco.
        frame.Broken = false;
        unlocking = Gate.EnsureUnlockedAsync(page);
        gate = await Ui.GateOn(page);
        frame.Broken = true;
        await Ui.UnlockThrough(page, Master);
        Assert.True(await unlocking);

        // Creando la boveda (no abriendola) se ven el candado y el titulo.
        await app.Store.DeleteLocalAsync();
        var create = new UnlockPage();
        Assert.True(create.Find<Image>("Logo").IsVisible);
        Assert.True(create.Find<Label>("TitleLabel").IsVisible);
    }

    // ------------------------------------------------------------------ sin plataforma

    [Fact]
    public async Task Sin_plataforma_no_hay_nada_que_hacer()
    {
        IDesktopIntegration desktop = new NoDesktopIntegration();
        Assert.False(desktop.IsAvailable);
        desktop.StartWithWindows = true;
        Assert.False(desktop.StartWithWindows);
        desktop.SetMinimizeToTray(true);
        Assert.False(desktop.ExtensionsPackaged);
        Assert.False(desktop.ExtensionsAvailable);
        Assert.Empty(desktop.DetectedBrowsers());
        Assert.Empty(desktop.GuideBrowsers());
        var page = new ContentPage();
        await desktop.InstallExtensionAsync(page, ExtensionInstallerCore.Known[0]);
        Assert.Null(desktop.CompactForGate(null));
        desktop.RecompactGate();
        desktop.FitGateHeight(page, 100);
        using (var app = TestHost.Start())
            desktop.AttachWindow(new Window(new ContentPage()));

        IAndroidIntegration android = new NoAndroidIntegration();
        Assert.False(android.IsAvailable);
        Assert.False(android.AutofillSupported);
        Assert.False(android.AutofillIsOurs);
        Assert.False(android.HasPreferredService);
        Assert.Empty(android.InstalledBrowsers());
        Assert.False(android.OpenPreferredService());
        Assert.False(android.OpenBrowserSettings("x"));
        android.RequestAutofill();
        android.ApplyScreenshotPolicy();
        await android.OfferAutofillAfterUnlockAsync(page);
        android.MoveToBack();
        Assert.Equal(GateBackAction.None, android.GateBack());
    }
}
