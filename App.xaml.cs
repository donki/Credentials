using Credentials.Helpers;

namespace Credentials;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>La guia de configuracion se esta abriendo sola en esta sesion: las preguntas de despues de desbloquear no salen.</summary>
    internal static bool TutorialOpening { get; private set; }

    /// <summary>El respiro tras desbloquear antes de abrir la guia (que la puerta se haya cerrado). Las pruebas lo acortan.</summary>
    internal static TimeSpan UnlockPause { get; set; } = TimeSpan.FromMilliseconds(600);

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "sOC Credentials" };
        // Windows: bandeja, sesion de escritorio, autocompletar y extensiones (nada en las demas).
        Helpers.ServiceHelper.GetRequiredService<Services.IDesktopIntegration>().AttachWindow(window);
        // Sincronizacion automatica con la nube: al abrir la boveda, al volver a la aplicacion y cada
        // cinco minutos mientras esta abierta (antes solo con el boton de Ajustes).
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            store.Unlocked += () => _ = store.SyncQuietlyAsync(TimeSpan.Zero);
            window.Resumed += (_, _) => _ = store.SyncQuietlyAsync(TimeSpan.FromSeconds(30));
            window.Activated += (_, _) => _ = store.SyncQuietlyAsync(TimeSpan.FromSeconds(30));
            window.Created += (_, _) =>
            {
                var timer = Dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromMinutes(5);
                timer.Tick += (_, _) => _ = store.SyncQuietlyAsync(TimeSpan.FromMinutes(4));
                timer.Start();
            };
        }
        // «Confiar en este usuario y dispositivo»: la boveda se abre sola al arrancar (la extension y el
        // autocompletar ya la encuentran abierta).
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            _ = store.TryTrustedUnlockAsync();
        }
        // La primera vez que se abre la boveda sale sola la guia de configuracion (despues, desde el
        // menu). Va antes que las preguntas de cada plataforma, que la ven y se callan.
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            var settings = Helpers.ServiceHelper.GetRequiredService<Services.ISettingsService>();
            var navigation = Helpers.ServiceHelper.GetRequiredService<Services.INavigationService>();
            store.Unlocked += () => Dispatcher.RunOnUi(async () =>
            {
                if (settings.TutorialDone)
                    return;
                settings.TutorialDone = true;
                TutorialOpening = true;
                await Task.Delay(UnlockPause);
                try { await navigation.GoToAsync("//TutorialPage"); }
                catch (Exception) { }
            });
        }
        // Android: al desbloquear, si otro gestor (o ninguno) rellena las contraseñas, se propone que lo haga esta.
        {
            var store = Helpers.ServiceHelper.GetRequiredService<Services.VaultStore>();
            var android = Helpers.ServiceHelper.GetRequiredService<Services.IAndroidIntegration>();
            if (android.IsAvailable)
            {
                store.Unlocked += () => Dispatcher.RunOnUi(async () =>
                {
                    await Task.Delay(UnlockPause);
                    try
                    {
                        // Si sale la guia de configuracion, nada de preguntas sueltas.
                        if (TutorialOpening)
                            return;
                        if (window.Page is { } page)
                            await android.OfferAutofillAfterUnlockAsync(page);
                    }
                    catch (Exception) { }
                });
            }
        }
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
