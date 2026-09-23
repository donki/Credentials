namespace Credentials;

public partial class App : Application
{
#if WINDOWS
    private Platforms.Windows.TrayIcon? _tray;
    private Platforms.Windows.ExtensionServer? _extensions;
    private Platforms.Windows.DesktopAutofill? _desktopAutofill;
    /// <summary>La ventana ha salido solo para pedir la contraseña (arranque con Windows, vuelta a la sesion): al abrir la boveda vuelve a la bandeja.</summary>
    private bool _hideAfterUnlock;

    /// <summary>El icono de bandeja de la ventana principal (para que Ajustes cambie su comportamiento).</summary>
    public static Platforms.Windows.TrayIcon? Tray { get; private set; }
#endif

    public App()
    {
        InitializeComponent();
    }

#if WINDOWS
    /// <summary>
    /// Extensiones de navegador: la tuberia que atiende al host de mensajeria nativa, el refresco de
    /// lo ya registrado (la carpeta cambia con la version) y, al desbloquear, la oferta de instalarla
    /// en los navegadores que no la tengan.
    /// </summary>
    private void StartExtensions(Window window)
    {
        try
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            var settings = Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>();
            var loc = Helpers.ServiceHelper.GetRequiredService<Services.ILocalizationService>();
            var toast = Helpers.ServiceHelper.GetRequiredService<Services.IToastService>();
            Platforms.Windows.ExtensionInstaller.RegisterForInstalledBrowsers();
            _extensions = new Platforms.Windows.ExtensionServer(store, settings);
            _extensions.BrowserConnected += browser =>
            {
                var name = Platforms.Windows.ExtensionInstaller.Known.FirstOrDefault(b => b.Key == browser)?.Name ?? browser;
                toast.Show(string.Format(loc.CurrentCulture, loc["ExtConnected"], name));
            };
            _extensions.Start();
            store.Unlocked += () => MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Un respiro para que la pagina de desbloqueo se haya cerrado antes de preguntar.
                await Task.Delay(600);
                if (window.Page is { } page)
                    await Platforms.Windows.ExtensionSetup.OfferAfterUnlockAsync(page, settings, loc);
                // Si la ventana solo salio para pedir la contraseña, vuelve a la bandeja.
                if (_hideAfterUnlock)
                {
                    _hideAfterUnlock = false;
                    _tray?.HideToTray();
                }
            });
        }
        catch (Exception) { /* sin extensiones no pasa nada: la aplicacion sigue */ }
    }

    private static bool IsPackaged()
    {
        try { return global::Windows.ApplicationModel.Package.Current is not null; }
        catch (Exception) { return false; }
    }
#endif

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "sOC Credentials" };
#if WINDOWS
        // Tamaño de arranque razonable en el escritorio: la lista es alta y estrecha, como en el movil.
        window.Width = 900;
        window.Height = 760;
        // Al minimizar, a la bandeja (icono en el area de notificacion con Abrir y Salir), si el
        // usuario no lo ha quitado en Ajustes.
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native && _tray is null)
            {
                var loc = Helpers.ServiceHelper.GetRequiredService<Services.ILocalizationService>();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                var settings = Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>();
                _tray = new Platforms.Windows.TrayIcon(hwnd, key => loc[key], () => native.Close()) { MinimizeToTray = settings.TrayOnMinimize };
                Tray = _tray;
                // La boveda queda abierta mientras se usa el PC: la inactividad es la del sistema
                // (teclado y raton), y al bloquear la sesion de Windows (Win+L) se cierra al momento.
                Services.VaultStore.SystemIdle = Platforms.Windows.TrayIcon.SystemIdle;
                var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
                _tray.SessionLocked += () => native.DispatcherQueue.TryEnqueue(() => { try { store.Lock(); } catch (Exception) { } });
                // Autocompletar en las aplicaciones del escritorio (lista pegada al campo de contraseña).
                try
                {
                    _desktopAutofill = new Platforms.Windows.DesktopAutofill(store, settings, loc);
                    _desktopAutofill.Start();
                }
                catch (Exception) { _desktopAutofill = null; }
                // La contraseña se pide una vez por sesion de escritorio: al arrancar con Windows
                // («--tray») sale la pagina de desbloqueo y, abierta la boveda, la ventana se va a la
                // bandeja; y al volver a la sesion tras Win+L (que la cerro) se vuelve a pedir. Con
                // «--background» (la arranca el navegador por algo pasivo) se esconde sin pedir nada:
                // se pedira cuando el usuario use la extension.
                var args = Environment.GetCommandLineArgs();
                if (args.Contains("--background") || (args.Contains("--tray") && store.IsUnlocked))
                    native.DispatcherQueue.TryEnqueue(() => _tray.HideToTray());
                else if (args.Contains("--tray"))
                    _hideAfterUnlock = true;
                _tray.SessionUnlocked += () => native.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (store.IsUnlocked || !store.Exists)
                            return;
                        _hideAfterUnlock = _tray.IsHidden;
                        _tray.Show();
                        if (window.Page is { } page)
                            _ = Pages.Gate.EnsureUnlockedAsync(page);
                        Pages.Gate.Current?.RetryBiometric();
                    }
                    catch (Exception) { }
                });
                // Sin paquete (exe suelto o lanzador): identidad para la barra de tareas y anclaje al lanzador.
                if (!IsPackaged())
                    Platforms.Windows.TaskbarIdentity.Apply(hwnd, "sOCratic.sOCCredentials", "sOC Credentials", Environment.GetEnvironmentVariable("SOC_LAUNCHER"));
                StartExtensions(window);
            }
        };
#endif
#if ANDROID
        // Al desbloquear: si otro gestor (o ninguno) rellena las contraseñas, se propone que lo haga esta.
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            var settings = Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>();
            var loc = Helpers.ServiceHelper.GetRequiredService<Services.ILocalizationService>();
            store.Unlocked += () => MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(600);
                try
                {
                    if (window.Page is { } page)
                        await Platforms.Android.AutofillSetup.OfferAfterUnlockAsync(page, settings, loc);
                }
                catch (Exception) { }
            });
        }
#endif
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug, desactivado en Release/produccion
        // Solo en Debug, para probar sin teclear: «--master clave» crea o abre la boveda con esa
        // contraseña, «--demo» siembra unas entradas inventadas y «--page …» abre una pagina
        // (Helpers.DemoData). Jamas en Release.
        var args = Environment.GetCommandLineArgs();
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Arg("--master") is { } master)
            window.Created += async (_, _) => await Helpers.DemoData.ApplyAsync(master, args.Contains("--demo"), Arg("--page"), Arg("--lang"));
#endif
        return window;
    }
}
