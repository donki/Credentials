using Credentials.Platforms.Windows;
using Browser = Credentials.Platforms.Windows.Browser;

namespace Credentials.Services;

/// <summary>
/// Lo que la aplicacion hace solo en el escritorio de Windows (bandeja, «Arrancar con Windows»,
/// extensiones de navegador, autocompletar en las aplicaciones, la ventanita de la contraseña). Las
/// paginas y la App lo piden aqui en lugar de llevar <c>#if WINDOWS</c>: en Windows es
/// <c>DesktopIntegration</c> sobre las piezas de Win32 (<see cref="IDesktopHost"/>), en Android y en
/// el destino sin plataforma, <see cref="NoDesktopIntegration"/>; las pruebas montan la de Windows
/// sobre dobles.
/// </summary>
public interface IDesktopIntegration
{
    /// <summary>Hay escritorio de Windows (si no, nada de lo de abajo se enseña ni hace nada).</summary>
    bool IsAvailable { get; }

    /// <summary>«Arrancar con Windows» (la clave Run de HKCU).</summary>
    bool StartWithWindows { get; set; }

    /// <summary>Si al minimizar se va a la bandeja (cambia al momento el icono de bandeja, si lo hay).</summary>
    void SetMinimizeToTray(bool on);

    /// <summary>La version de la Microsoft Store (MSIX): sin puente con los navegadores.</summary>
    bool ExtensionsPackaged { get; }

    /// <summary>La aplicacion trae la extension y el host de mensajeria nativa.</summary>
    bool ExtensionsAvailable { get; }

    /// <summary>Los navegadores con soporte que hay en el PC.</summary>
    IReadOnlyList<Browser> DetectedBrowsers();

    /// <summary>Los navegadores que propone la guia (ninguno si no se puede instalar).</summary>
    IReadOnlyList<Browser> GuideBrowsers();

    /// <summary>Instala la extension en un navegador (registro y ficha de su tienda), con dialogos sobre la pagina.</summary>
    Task InstallExtensionAsync(Page page, Browser browser);

    /// <summary>La ventana principal recien creada: tamaño, bandeja, sesion de Windows, autocompletar y extensiones.</summary>
    void AttachWindow(Window window);

    /// <summary>Mientras se pide la contraseña, la ventana en pequeño abajo a la derecha; devuelve lo que la deja como estaba.</summary>
    Action? CompactForGate(Window? window);

    /// <summary>Vuelve a dejar en pequeño la ventana que pide la contraseña (al volver de la bandeja).</summary>
    void RecompactGate();

    /// <summary>
    /// Ajusta el alto de la ventanita de la contraseña a lo que pide su contenido
    /// (<paramref name="contentHeight"/>, en unidades de MAUI), anclada abajo. Con
    /// <paramref name="force"/> se rehace aunque el contenido no haya cambiado.
    /// </summary>
    void FitGateHeight(Page gate, double contentHeight, bool force = false);
}

/// <summary>Sin escritorio de Windows (Android y el destino sin plataforma): no hay nada que hacer.</summary>
public sealed class NoDesktopIntegration : IDesktopIntegration
{
    public bool IsAvailable => false;
    public bool StartWithWindows { get => false; set { } }
    public void SetMinimizeToTray(bool on) { }
    public bool ExtensionsPackaged => false;
    public bool ExtensionsAvailable => false;
    public IReadOnlyList<Browser> DetectedBrowsers() => [];
    public IReadOnlyList<Browser> GuideBrowsers() => [];
    public Task InstallExtensionAsync(Page page, Browser browser) => Task.CompletedTask;
    public void AttachWindow(Window window) { }
    public Action? CompactForGate(Window? window) => null;
    public void RecompactGate() { }
    public void FitGateHeight(Page gate, double contentHeight, bool force = false) { }
}

/// <summary>Un rectangulo de pantalla en pixeles fisicos.</summary>
public readonly record struct FrameRect(int X, int Y, int Width, int Height);

/// <summary>La ventana nativa de una ventana de MAUI en Windows (AppWindow de WinUI), lo justo para colocarla.</summary>
public interface IWindowFrame
{
    /// <summary>Posicion y tamaño exteriores.</summary>
    FrameRect Bounds { get; }

    /// <summary>Alto del area cliente (sin los bordes de Windows).</summary>
    int ClientHeight { get; }

    /// <summary>Pixeles fisicos por unidad de MAUI.</summary>
    double Scale { get; }

    /// <summary>La zona de trabajo (sin la barra de tareas) de la pantalla donde esta.</summary>
    FrameRect WorkArea { get; }

    bool IsMaximized { get; }

    void Restore();

    void Maximize();

    void MoveAndResize(FrameRect bounds);

    /// <summary>Minimiza la ventana dejandole <paramref name="restoreBounds"/> (y maximizada, si lo estaba) para cuando se restaure.</summary>
    void MinimizeWithRestoreBounds(FrameRect restoreBounds, bool restoreMaximized);
}

/// <summary>
/// Las piezas de Windows que usa <c>DesktopIntegration</c>: lo que toca Win32, WinUI o el registro.
/// En la aplicacion es <c>Platforms.Windows.DesktopHost</c>; en las pruebas, un doble que apunta.
/// </summary>
public interface IDesktopHost
{
    /// <summary>Los argumentos con que se arranco el proceso (--tray, --background).</summary>
    string[] CommandLine { get; }

    /// <summary>La aplicacion es la de la Microsoft Store (MSIX).</summary>
    bool IsPackaged { get; }

    /// <summary>La instalacion de la extension (registro de HKCU, carpeta de datos y navegador de verdad).</summary>
    ExtensionInstallerCore Extensions { get; }

    /// <summary>«Arrancar con Windows».</summary>
    StartupRegistration Startup { get; }

    /// <summary>Cuanto lleva el PC sin teclado ni raton (para el bloqueo por inactividad).</summary>
    TimeSpan? SystemIdle();

    /// <summary>El icono de bandeja sobre la ventana nativa de <paramref name="window"/>; null si aun no tiene ventana nativa.</summary>
    TrayController? CreateTray(Window window, Func<string, string> text);

    /// <summary>Ejecuta <paramref name="action"/> en el hilo de la ventana nativa.</summary>
    void Post(Window window, Action action);

    /// <summary>Arranca el autocompletar en las aplicaciones del escritorio (puede lanzar).</summary>
    void StartDesktopAutofill(VaultStore store, ISettingsService settings, ILocalizationService l);

    /// <summary>Identidad para la barra de tareas y anclaje al lanzador (solo sin paquete).</summary>
    void ApplyTaskbarIdentity(Window window);

    /// <summary>La tuberia que atiende al host de mensajeria nativa de los navegadores.</summary>
    void StartExtensionServer(VaultStore store, ISettingsService settings, Action requestUnlock, Action<string> browserConnected);

    /// <summary>Trae al frente la ventana nativa (sin icono de bandeja).</summary>
    void BringToFront(Window? window);

    /// <summary>La ventana nativa de <paramref name="window"/> para colocarla; null si no la hay.</summary>
    IWindowFrame? FrameOf(Window? window);
}
