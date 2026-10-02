using System.Reflection;
using Credentials.Helpers;
using Credentials.Models;
using Credentials.Pages;
using Credentials.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Credentials.Ui.Tests;

public class AppTests
{
    private static Window CreateWindow(App app) =>
        (Window)typeof(App).GetMethod("CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, [null])!;

    private static void Raise(object target, string evt)
    {
        var field = target.GetType().GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? typeof(Window).GetField(evt, BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((EventHandler?)field.GetValue(target))?.Invoke(target, EventArgs.Empty);
    }

    [Fact]
    public async Task La_ventana_con_el_Shell_la_guia_y_la_sincronizacion()
    {
        using var app = TestHost.Start();
        App.UnlockPause = TimeSpan.Zero;
        var window = CreateWindow(app.App);
        Assert.Equal("sOC Credentials", window.Title);
        Assert.IsType<AppShell>(window.Page);

        // Creada la ventana, el temporizador de sincronizar cada cinco minutos.
        Raise(window, "Created");
        var timer = Assert.Single(app.Dispatcher.Timers);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Interval);
        Assert.True(timer.IsRunning);
        timer.Fire();
        Raise(window, "Resumed");
        Raise(window, "Activated");
        Assert.Empty(app.Http.Calls);   // sin nube no se sincroniza nada

        // El primer desbloqueo abre la guia; los siguientes, no.
        await AsyncVoid.Run(() => app.Store.CreateAsync("contraseña-larga").GetAwaiter().GetResult());
        await Ui.Until(() => app.Navigation.Routes.Contains("//TutorialPage"));
        Assert.True(app.Settings.TutorialDone);
        Assert.True((bool)typeof(App).GetProperty("TutorialOpening", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        app.Store.Lock();
        await AsyncVoid.Run(() => app.Store.UnlockAsync("contraseña-larga").GetAwaiter().GetResult());
        Assert.Single(app.Navigation.Routes);

        // Si navegar falla, no pasa nada.
        app.Settings.TutorialDone = false;
        app.Navigation.Fail = new InvalidOperationException("sin Shell");
        app.Store.Lock();
        await AsyncVoid.Run(() => app.Store.UnlockAsync("contraseña-larga").GetAwaiter().GetResult());
        Assert.True(app.Settings.TutorialDone);
    }

    [Fact]
    public async Task Con_confianza_la_boveda_se_abre_sola_al_arrancar()
    {
        using var app = TestHost.Start();
        await app.CreateVaultAsync("contraseña-larga");
        app.Settings.TrustDevice = true;
        app.Settings.TutorialDone = true;
        await app.Store.RememberKeyAsync(true);
        app.Store.Lock();
        _ = CreateWindow(app.App);
        await Ui.Until(() => app.Store.IsUnlocked);
    }

    [Fact]
    public async Task El_Shell_textos_menu_y_atras()
    {
        using var app = TestHost.Start();
        var shell = new AppShell();
        Assert.Equal(app["MenuVault"], shell.Find<Label>("HomeLabel").Text);
        Assert.Equal(app["MenuSettings"], shell.Find<Label>("SettingsLabel").Text);
        Assert.Equal("v2026.10.01.00", shell.Find<Label>("VersionLabel").Text);
        Assert.Equal([app["MenuVault"], app["MenuSettings"], app["MenuTutorial"], app["About"]], shell.Items.Select(i => i.Title));

        // En ingles tambien los titulos de las secciones (antes se quedaban en castellano).
        app.Texts.SetLanguage("en");
        Assert.Equal("Settings", shell.Find<Label>("SettingsLabel").Text);
        Assert.Equal([app["MenuVault"], app["MenuSettings"], app["MenuTutorial"], app["About"]], shell.Items.Select(i => i.Title));
        Assert.Equal("Settings", shell.Items[1].Title);

        // En la boveda y sin menu, atras es el de siempre (el del Shell de MAUI, que sin ventana no sabe).
        Assert.Throws<NullReferenceException>(() => shell.Back());

        // Atras con el menu abierto: lo cierra.
        shell.FlyoutIsPresented = true;
        Assert.True(shell.Back());
        Assert.False(shell.FlyoutIsPresented);

        // En otra seccion: vuelve a la boveda.
        shell.CurrentItem = shell.Items[1];
        Assert.True(shell.Back());
        Assert.Same(shell.Items[0], shell.CurrentItem);

        // Los toques del menu navegan por el Shell y cierran el menu.
        foreach (var (handler, route) in new[] { ("OnSettingsTapped", "SettingsPage"), ("OnTutorialTapped", "TutorialPage"), ("OnAboutTapped", "AboutPage"), ("OnHomeTapped", "VaultPage") })
        {
            shell.FlyoutIsPresented = true;
            try { await Ui.Call(shell, handler, shell, new TappedEventArgs(null)); }
            catch (InvalidOperationException) { /* sin ventana el Shell puede no terminar de navegar */ }
            Assert.False(shell.FlyoutIsPresented);
        }
    }

    [Fact]
    public async Task Navegar_por_el_Shell_de_la_ventana()
    {
        using var app = TestHost.Start();
        var window = (Window)((IApplication)app.App).CreateWindow(null);
        var shell = Assert.IsType<AppShell>(window.Page);
        Assert.Same(shell, Shell.Current);
        var navigation = new ShellNavigationService();
        await navigation.GoToAsync("//SettingsPage");
        Assert.Equal("SettingsPage", shell.CurrentItem?.CurrentItem?.CurrentItem?.Route);
        var page = new ContentPage();
        await navigation.PushAsync(page);
        Assert.Same(page, shell.Navigation.NavigationStack[^1]);
    }

    [Fact]
    public async Task Leer_el_fichero_elegido()
    {
        using var app = TestHost.Start();
        var path = Path.Combine(app.Root, "importar.csv");
        File.WriteAllText(path, "name,url\nX,https://x\n", new System.Text.UTF8Encoding(true));
        var picker = new PickerDouble();
        Essentials.Use(typeof(FilePicker), "SetDefault", picker);
        TextFilePicker.Open = file => Task.FromResult<Stream>(File.OpenRead(file.FullPath));
        try
        {
            var reader = new TextFilePicker();
            Assert.Null(await reader.PickTextAsync("Elige"));   // cancelado
            Assert.Equal("Elige", picker.LastTitle);
            picker.Result = new FileResult(path);
            Assert.Equal("name,url\nX,https://x\n", await reader.PickTextAsync("Elige"));   // sin la marca BOM
        }
        finally
        {
            Essentials.Use(typeof(FilePicker), "SetDefault", null!);
        }
    }

    private sealed class PickerDouble : IFilePicker
    {
        public FileResult? Result { get; set; }
        public string? LastTitle { get; private set; }
        public Task<FileResult?> PickAsync(PickOptions? options = null)
        {
            LastTitle = options?.PickerTitle;
            return Task.FromResult(Result);
        }
        public Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null) => Task.FromResult<IEnumerable<FileResult?>>([Result]);
    }

    [Fact]
    public void El_programa_registra_todos_los_servicios()
    {
        using var app = TestHost.Start();
        var maui = MauiProgram.CreateMauiApp();
        var services = maui.Services;
        Assert.IsType<SettingsService>(services.GetRequiredService<ISettingsService>());
        Assert.IsType<LocalizationService>(services.GetRequiredService<ILocalizationService>());
        Assert.NotNull(services.GetRequiredService<VaultStore>());
        Assert.IsType<ModernDialogService>(services.GetRequiredService<IDialogService>());
        Assert.IsType<ShellNavigationService>(services.GetRequiredService<INavigationService>());
        Assert.IsType<CameraQrScanner>(services.GetRequiredService<IQrScanner>());
        Assert.IsType<TextFilePicker>(services.GetRequiredService<ITextFilePicker>());
        Assert.IsType<NoBiometric>(services.GetRequiredService<IBiometric>());
        Assert.IsType<NoOAuthBrowser>(services.GetRequiredService<IOAuthBrowser>());
        Assert.NotNull(services.GetRequiredService<IClipboard>());
        Assert.NotNull(services.GetRequiredService<IShare>());
        Assert.NotNull(services.GetRequiredService<IBrowser>());
        Assert.NotNull(services.GetRequiredService<Microsoft.Maui.ApplicationModel.Communication.IEmail>());
        Assert.NotNull(services.GetRequiredService<IAppInfo>());
        Assert.NotNull(services.GetRequiredService<Microsoft.Maui.Devices.IDeviceInfo>());
        Assert.NotNull(services.GetRequiredService<IFileSystem>());
        // ServiceHelper queda apuntando al contenedor de la aplicacion.
        Assert.Same(services.GetRequiredService<VaultStore>(), ServiceHelper.GetRequiredService<VaultStore>());
        ServiceHelper.Initialize(app.Services);
    }

    [Fact]
    public async Task El_gestor_de_errores_avisa_en_el_idioma_de_la_app()
    {
        using var app = TestHost.Start();
        CrashReporting.Install();
        CrashReporting.Install();   // una sola vez
        Assert.NotNull(SocShared.CrashGuard.Alert);

        // El aviso con los textos de la app (aqui sin pantalla: ModernDialog no llega a pintarse).
        var page = new ContentPage { Content = new Label() };
        try { await SocShared.CrashGuard.Alert!(page, "t", "m", "ok"); }
        catch (Exception) { /* sin animaciones fuera del dispositivo */ }

        // Sin servicios todavia, el texto por defecto.
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        try { await SocShared.CrashGuard.Alert!(page, "t", "m", "ok"); }
        catch (Exception) { }
        ServiceHelper.Initialize(app.Services);

        // Una tarea que falla sin que nadie la mire: se registra sin el mensaje (que podria llevar datos).
        Fail("dato-secreto-123");
        bool Logged() => File.Exists(SocShared.CrashGuard.LogPath) && File.ReadAllText(SocShared.CrashGuard.LogPath).Contains("UnobservedTaskException");
        for (var i = 0; i < 40 && !Logged(); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50);
        }
        Assert.True(Logged());
        Assert.DoesNotContain("dato-secreto-123", File.ReadAllText(SocShared.CrashGuard.LogPath));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Fail(string secret) => _ = Task.FromException(new FormatException(secret));
    }
}

public class DemoDataTests
{
    [Fact]
    public async Task Abre_o_crea_siembra_y_navega()
    {
        using var app = TestHost.Start();
        DemoData.PageDelay = TimeSpan.Zero;

        await DemoData.ApplyAsync(null, demo: false, page: null, lang: "en");
        Assert.Equal("en", app.Settings.Language);
        Assert.Equal("en", app.Texts.CurrentLanguage);
        Assert.False(app.Store.Exists);

        await DemoData.ApplyAsync("contraseña-demo", demo: true, page: "settings", lang: null);
        Assert.True(app.Store.IsUnlocked);
        Assert.Equal(6, app.Store.Data!.Entries.Count);
        Assert.Equal("//SettingsPage", app.Navigation.Routes[^1]);

        await DemoData.ApplyAsync("contraseña-demo", demo: true, page: "tutorial", lang: "");   // ya sembrada: no repite
        Assert.Equal(6, app.Store.Data!.Entries.Count);
        await DemoData.ApplyAsync("contraseña-demo", false, "about", null);
        Assert.Equal(["//SettingsPage", "//TutorialPage", "//AboutPage"], app.Navigation.Routes);

        await DemoData.ApplyAsync("contraseña-demo", false, "entry:GitHub", null);
        var entryPage = Assert.IsType<EntryPage>(Assert.Single(app.Navigation.Pushed));
        Assert.Equal("GitHub", entryPage.Title);
        await DemoData.ApplyAsync("contraseña-demo", false, "entry:No existe", null);
        await DemoData.ApplyAsync("contraseña-demo", false, "otra", null);
        Assert.Single(app.Navigation.Pushed);

        await DemoData.ApplyAsync("contraseña-demo", false, "lock", null);
        Assert.False(app.Store.IsUnlocked);

        // Con la boveda ya creada, abre con la contraseña; si no es, no pasa nada.
        await DemoData.ApplyAsync("mala", false, null, null);
        Assert.False(app.Store.IsUnlocked);
        await DemoData.ApplyAsync("contraseña-demo", false, null, null);
        Assert.True(app.Store.IsUnlocked);
    }
}
