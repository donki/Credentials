using System.Runtime.CompilerServices;
using Credentials.Helpers;
using Credentials.Pages;
using Credentials.Platforms.Windows;
using Browser = Credentials.Platforms.Windows.Browser;

namespace Credentials.Services;

/// <summary>
/// El escritorio de Windows (<see cref="IDesktopIntegration"/>): la logica de lo que antes iba en
/// bloques <c>#if WINDOWS</c> de App, Ajustes, la guia y la puerta de desbloqueo. Lo que toca Win32,
/// WinUI o el registro lo pone <see cref="IDesktopHost"/>; asi esto se prueba entero con dobles.
/// </summary>
public sealed class DesktopIntegration(VaultStore store, ISettingsService settings, ILocalizationService l, IToastService toast, IDialogService dialogs, IDesktopHost host, Func<bool>? sandbox = null) : IDesktopIntegration
{
    /// <summary>El nombre del valor de «Arrancar con Windows» en la clave Run.</summary>
    public const string StartupValueName = "sOCCredentials";

    private readonly ExtensionSetupFlow _flow = new(host.Extensions);
    private readonly Func<bool> _sandbox = sandbox ?? (() => VaultStore.Sandbox);
    private readonly ConditionalWeakTable<Page, FitState> _fits = [];
    private TrayController? _tray;
    private IWindowFrame? _compactFrame;

    /// <summary>La ventana ha salido solo para pedir la contraseña (arranque con Windows, vuelta a la sesion): al abrir la boveda vuelve a la bandeja.</summary>
    private bool _hideAfterUnlock;

    public bool IsAvailable => true;

    /// <summary>El icono de bandeja de la ventana principal, cuando ya la hay.</summary>
    public TrayController? Tray => _tray;

    public bool StartWithWindows
    {
        get => host.Startup.IsEnabled(StartupValueName);
        set => host.Startup.Set(StartupValueName, value);
    }

    public void SetMinimizeToTray(bool on)
    {
        if (_tray is { } tray)
            tray.MinimizeToTray = on;
    }

    public bool ExtensionsPackaged => host.Extensions.Packaged;

    public bool ExtensionsAvailable => host.Extensions.Available;

    public IReadOnlyList<Browser> DetectedBrowsers() => host.Extensions.Detected();

    public IReadOnlyList<Browser> GuideBrowsers() => host.Extensions.GuideBrowsers();

    public Task InstallExtensionAsync(Page page, Browser browser) => _flow.InstallAsync(new PagePrompts(dialogs, page), browser, l);

    // ------------------------------------------------------------------ la ventana principal

    public void AttachWindow(Window window)
    {
        // Sin Windows Hello desde 2026.09.24.01: si estaba activado, fuera el ajuste y la clave que
        // guardaba para el (la boveda se abre solo con la contraseña maestra).
        if (settings.Biometrics)
        {
            settings.Biometrics = false;
            if (!settings.TrustDevice)
                _ = store.RememberKeyAsync(false);
        }
        // Tamaño de arranque razonable en el escritorio: la lista es alta y estrecha, como en el movil.
        window.Width = 900;
        window.Height = 760;
        // Al minimizar, a la bandeja (icono en el area de notificacion con Abrir y Salir), si el
        // usuario no lo ha quitado en Ajustes.
        window.HandlerChanged += (_, _) => OnNativeWindow(window);
    }

    private void OnNativeWindow(Window window)
    {
        if (_tray is not null || host.CreateTray(window, key => l[key]) is not { } tray)
            return;
        tray.MinimizeToTray = settings.TrayOnMinimize;
        _tray = tray;
        // La boveda queda abierta mientras se usa el PC: la inactividad es la del sistema (teclado y
        // raton), y al bloquear la sesion de Windows (Win+L) se cierra al momento.
        VaultStore.SystemIdle = host.SystemIdle;
        tray.SessionLocked += () => host.Post(window, () => { try { if (!settings.TrustDevice) store.Lock(); } catch (Exception) { } });
        // Autocompletar en las aplicaciones del escritorio (lista pegada al campo de contraseña).
        try
        {
            if (_sandbox())
                throw new InvalidOperationException("sandbox");
            host.StartDesktopAutofill(store, settings, l);
        }
        catch (Exception) { }
        // La contraseña se pide una vez por sesion de escritorio: al arrancar con Windows («--tray»)
        // sale la pagina de desbloqueo y, abierta la boveda, la ventana se va a la bandeja; y al volver
        // a la sesion tras Win+L (que la cerro) se vuelve a pedir. Con «--background» (la arranca el
        // navegador por algo pasivo) se esconde sin pedir nada: se pedira cuando se use la extension.
        var args = host.CommandLine;
        if (args.Contains("--background") || (args.Contains("--tray") && store.IsUnlocked))
            host.Post(window, tray.HideToTray);
        else if (args.Contains("--tray"))
            _hideAfterUnlock = true;
        tray.SessionUnlocked += () => host.Post(window, () =>
        {
            try
            {
                if (store.IsUnlocked || !store.Exists)
                    return;
                _hideAfterUnlock = tray.IsHidden;
                tray.Show();
                if (window.Page is { } page)
                    _ = Gate.EnsureUnlockedAsync(page);
                // Si la puerta ya estaba (se preparo al bloquear, con la ventana escondida), sacarla
                // de la bandeja la ha restaurado a su tamaño normal: otra vez en pequeño.
                RecompactGate();
                Gate.Current?.RetryBiometric();
            }
            catch (Exception) { }
        });
        // Sin paquete (exe suelto o lanzador): identidad para la barra de tareas y anclaje al lanzador.
        if (!host.IsPackaged)
            host.ApplyTaskbarIdentity(window);
        if (!_sandbox())
            StartExtensions(window);
    }

    /// <summary>
    /// Extensiones de navegador: el refresco de lo registrado (la carpeta cambia con la version), la
    /// tuberia que atiende al host de mensajeria nativa y, al desbloquear, la oferta de instalarla en
    /// los navegadores que no la tengan.
    /// </summary>
    private void StartExtensions(Window window)
    {
        try
        {
            // Empaquetada (Microsoft Store) no registra nada: el registro va virtualizado y el
            // navegador no veria el puente (ExtensionInstallerCore.Packaged).
            host.Extensions.RegisterForInstalledBrowsers();
            host.StartExtensionServer(store, settings, RequestUnlock, browser =>
            {
                var name = ExtensionInstallerCore.Known.FirstOrDefault(b => b.Key == browser)?.Name ?? browser;
                toast.Show(string.Format(l.CurrentCulture, l["ExtConnected"], name));
            });
            store.Unlocked += () => window.Dispatcher.RunOnUi(async () =>
            {
                // Un respiro para que la pagina de desbloqueo se haya cerrado antes de preguntar.
                await Task.Delay(App.UnlockPause);
                // Si sale la guia de configuracion, ni preguntas sueltas ni volver a la bandeja.
                if (App.TutorialOpening)
                {
                    _hideAfterUnlock = false;
                    return;
                }
                if (window.Page is { } page)
                    await _flow.OfferAfterUnlockAsync(new PagePrompts(dialogs, page), settings, l);
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

    /// <summary>La extension necesita la boveda: se trae la ventana y, si esta cerrada, la pagina de desbloqueo.</summary>
    public void RequestUnlock()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        try
        {
            if (_tray is { } tray)
                tray.Show();
            else
                host.BringToFront(window);
        }
        catch (Exception) { }
        if (!store.IsUnlocked && window?.Page is { } page)
            _ = Gate.EnsureUnlockedAsync(page);
    }

    /// <summary>Los dialogos de la instalacion guiada, sobre la pagina.</summary>
    private sealed class PagePrompts(IDialogService dialogs, Page page) : IExtensionPrompts
    {
        public Task<string?> ActionSheetAsync(string title, string cancel, params string[] options) => dialogs.ActionSheetAsync(page, title, cancel, options);

        public Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null) => dialogs.AlertAsync(page, title, message, accept, cancel);
    }

    // ------------------------------------------------------------------ la ventanita de la contraseña

    /// <summary>
    /// En Windows, mientras se pide la contraseña, la ventana se queda pequeña y abajo a la derecha
    /// del escritorio (como un aviso del sistema). Devuelve lo que la deja como estaba.
    /// </summary>
    public Action? CompactForGate(Window? window)
    {
        try
        {
            if (host.FrameOf(window) is not { } frame)
                return null;
            var wasMaximized = frame.IsMaximized;
            if (wasMaximized)
                frame.Restore();
            var saved = frame.Bounds;
            PlaceSmall(frame);
            _compactFrame = frame;
            return () =>
            {
                _compactFrame = null;
                try
                {
                    if (store.IsUnlocked)
                    {
                        // Desbloqueada: la ventana se va directamente a la barra de tareas y el tamaño
                        // de antes queda para cuando se restaure. Asi no se ve crecer la ventana
                        // pequeña con la pantalla de la contraseña todavia dentro.
                        frame.MinimizeWithRestoreBounds(saved, wasMaximized);
                        return;
                    }
                    frame.MoveAndResize(saved);
                    if (wasMaximized)
                        frame.Maximize();
                }
                catch (Exception) { }
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Pequeña y abajo a la derecha de la zona de trabajo; el alto lo ajusta luego la puerta.</summary>
    private static void PlaceSmall(IWindowFrame frame)
    {
        var area = frame.WorkArea;
        var scale = frame.Scale;
        int w = (int)(420 * scale), h = (int)(340 * scale), margin = (int)(12 * scale);
        frame.MoveAndResize(new FrameRect(area.X + area.Width - w - margin, area.Y + area.Height - h - margin, w, h));
    }

    /// <summary>
    /// Vuelve a dejar en pequeño la ventana que pide la contraseña. Hace falta al volver de la
    /// bandeja: al bloquear Windows (Win+L) la boveda se cierra y la puerta se prepara con la ventana
    /// escondida; al volver al escritorio, sacarla de la bandeja la restauraba a su tamaño de antes
    /// (incluso maximizada) con la contraseña dentro.
    /// </summary>
    public void RecompactGate()
    {
        try
        {
            if (Gate.Current is not { } gate || _compactFrame is not { } frame)
                return;
            if (frame.IsMaximized)
                frame.Restore();
            PlaceSmall(frame);
            gate.RefitHeight();
        }
        catch (Exception) { }
    }

    private sealed class FitState
    {
        public double? TitleBar;
        public double Fitted = -1;
    }

    /// <summary>
    /// La puerta va en una ventana pequeña: su alto se ajusta a lo que hay dentro, sin huecos arriba
    /// ni abajo, y crece si aparece un error. Anclada abajo: crece o encoge hacia arriba.
    /// </summary>
    public void FitGateHeight(Page gate, double contentHeight, bool force = false)
    {
        try
        {
            if (gate.Width <= 0 || gate.Height <= 0 || host.FrameOf(gate.Window) is not { } frame)
                return;
            var state = _fits.GetOrCreateValue(gate);
            if (force)
                state.Fitted = -1;
            if (Math.Abs(contentHeight - state.Fitted) < 1)
                return;   // ya ajustada a este contenido (el cambio de tamaño vuelve a llamar aqui)
            var scale = frame.Scale;
            var bounds = frame.Bounds;
            var chrome = bounds.Height - frame.ClientHeight;   // bordes de Windows
            // MAUI pinta su barra de titulo dentro del area cliente: lo que sobra entre el area
            // cliente y la pagina es esa barra. Se mide una vez, con la ventana aun quieta: despues
            // la pagina va un paso por detras de cada cambio de tamaño.
            state.TitleBar ??= Math.Max(0, frame.ClientHeight / scale - gate.Height);
            var wanted = (int)Math.Ceiling((contentHeight + state.TitleBar.Value) * scale) + chrome;
            state.Fitted = contentHeight;
            if (Math.Abs(bounds.Height - wanted) < 2)
                return;
            var bottom = bounds.Y + bounds.Height;
            frame.MoveAndResize(new FrameRect(bounds.X, bottom - wanted, bounds.Width, wanted));
        }
        catch (Exception)
        {
            // Solo es la forma de la ventana: si falla, se queda como estaba.
        }
    }
}
