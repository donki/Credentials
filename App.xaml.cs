namespace Credentials;

public partial class App : Application
{
#if WINDOWS
    private Platforms.Windows.TrayIcon? _tray;
    private Platforms.Windows.ExtensionServer? _extensions;

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
            Platforms.Windows.ExtensionInstaller.RefreshIfRegistered();
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
                // «--tray» (arranque con Windows): escondida en la bandeja desde el principio.
                if (Environment.GetCommandLineArgs().Contains("--tray"))
                    native.DispatcherQueue.TryEnqueue(() => _tray.HideToTray());
                // Sin paquete (exe suelto o lanzador): identidad para la barra de tareas y anclaje al lanzador.
                if (!IsPackaged())
                    Platforms.Windows.TaskbarIdentity.Apply(hwnd, "sOCratic.sOCCredentials", "sOC Credentials", Environment.GetEnvironmentVariable("SOC_LAUNCHER"));
                StartExtensions(window);
            }
        };
#endif
#if DEBUG
        SocShared.AuthorNotes.Attach(window);   // notas de autor: SOLO Debug, desactivado en Release/produccion
        // Solo en Debug, para probar sin teclear: «--master clave» crea o abre la boveda con esa
        // contraseña y «--demo» siembra unas entradas inventadas. Jamas en Release.
        var args = Environment.GetCommandLineArgs();
        var master = Array.IndexOf(args, "--master");
        if (master >= 0 && master + 1 < args.Length)
        {
            var password = args[master + 1];
            var demo = args.Contains("--demo");
            window.Created += async (_, _) =>
            {
                var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
                try
                {
                    if (store.Exists) await store.UnlockAsync(password); else await store.CreateAsync(password);
                    if (demo && store.Data!.Entries.Count == 0)
                    {
                        store.Data.Entries.AddRange(
                        [
                            new Models.Credential { Kind = Models.EntryKind.Login, Title = "GitHub", Username = "ana@example.com", Password = "correct-horse-battery-staple", Url = "https://github.com", Folder = "Trabajo", Tags = ["dev"], Favorite = true, Totp = "otpauth://totp/GitHub:ana@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub" },
                            new Models.Credential { Kind = Models.EntryKind.Login, Title = "Banco Ejemplo", Username = "12345678A", Password = "Tr0ub4dor&3", Url = "https://banco.example", Folder = "Personal", Tags = ["dinero"] },
                            new Models.Credential { Kind = Models.EntryKind.App, Title = "Wi-Fi de casa", Username = "MiRed", Password = "casa-2026-segura", Folder = "Personal" },
                            new Models.Credential { Kind = Models.EntryKind.Totp, Title = "Microsoft", Username = "ana@example.com", Totp = "otpauth://totp/Microsoft:ana@example.com?secret=GEZDGNBVGY3TQOJQ&issuer=Microsoft" },
                            new Models.Credential { Kind = Models.EntryKind.Note, Title = "Licencia del NAS", Notes = "XXXX-YYYY-ZZZZ-1234 · Comprada el 3 de marzo.", Folder = "Trabajo" },
                        ]);
                        await store.SaveAsync(upload: false);
                    }
                    // «--page settings» o «--page entry:Titulo»: para capturar pantallas.
                    var page = Array.IndexOf(args, "--page");
                    if (page >= 0 && page + 1 < args.Length)
                    {
                        await Task.Delay(500);
                        var which = args[page + 1];
                        if (which == "settings")
                            await Shell.Current.GoToAsync("//SettingsPage");
                        else if (which.StartsWith("entry:") && store.Data!.Entries.FirstOrDefault(x => x.Title == which[6..]) is { } entry)
                            await Shell.Current.Navigation.PushAsync(new Pages.EntryPage(entry, isNew: false));
                    }
                }
                catch (Exception) { }
            };
        }
#endif
        return window;
    }
}
