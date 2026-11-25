using System.Runtime.InteropServices;
using Credentials.Services;
using Microsoft.UI.Windowing;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Las piezas de Windows de <see cref="DesktopIntegration"/> (<see cref="IDesktopHost"/>): la ventana
/// nativa de WinUI de la ventana de MAUI, el icono de bandeja, el autocompletar de escritorio, la
/// tuberia de las extensiones, la barra de tareas y el registro de verdad.
/// </summary>
public sealed class DesktopHost : IDesktopHost
{
    private ExtensionServer? _extensions;
    private DesktopAutofill? _desktopAutofill;

    /// <summary>La tuberia de los navegadores (las pruebas usan otra, para no cruzarse con la aplicacion abierta).</summary>
    internal string PipeName { get; init; } = ExtensionServer.PipeName;

    public string[] CommandLine => Environment.GetCommandLineArgs();

    public bool IsPackaged => PackageIdentity.IsPackaged();

    public ExtensionInstallerCore Extensions => ExtensionInstaller.Core;

    public StartupRegistration Startup => WindowsStartup.Registration;

    public TimeSpan? SystemIdle() => TrayIcon.SystemIdle();

    /// <summary>La ventana de WinUI de una ventana de MAUI (cuando ya tiene).</summary>
    internal static Microsoft.UI.Xaml.Window? Native(Window? window) => window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;

    public TrayController? CreateTray(Window window, Func<string, string> text)
    {
        if (Native(window) is not { } native)
            return null;
        return new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(native), text, () => native.Close());
    }

    public void Post(Window window, Action action) => Native(window)?.DispatcherQueue.TryEnqueue(() => action());

    public void StartDesktopAutofill(VaultStore store, ISettingsService settings, ILocalizationService l)
    {
        _desktopAutofill = new DesktopAutofill(store, settings, l);
        _desktopAutofill.Start();
    }

    public void ApplyTaskbarIdentity(Window window)
    {
        if (Native(window) is { } native)
            TaskbarIdentity.Apply(WinRT.Interop.WindowNative.GetWindowHandle(native), "sOCratic.sOCCredentials", "sOC Credentials", Environment.GetEnvironmentVariable("SOC_LAUNCHER"));
    }

    public void StartExtensionServer(VaultStore store, ISettingsService settings, Action requestUnlock, Action<string> browserConnected)
    {
        _extensions = new ExtensionServer(store, settings, requestUnlock, PipeName);
        _extensions.BrowserConnected += browserConnected;
        _extensions.Start();
    }

    public void BringToFront(Window? window)
    {
        if (Native(window) is not { } native)
            return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        ShowWindow(hwnd, 9 /* SW_RESTORE */);
        SetForegroundWindow(hwnd);
    }

    public IWindowFrame? FrameOf(Window? window) => Native(window) is { } native ? new WinUIFrame(native) : null;

    /// <summary>La AppWindow de WinUI vista como <see cref="IWindowFrame"/>.</summary>
    internal sealed class WinUIFrame(Microsoft.UI.Xaml.Window native) : IWindowFrame
    {
        private AppWindow App => native.AppWindow;

        private OverlappedPresenter? Presenter => App.Presenter as OverlappedPresenter;

        public FrameRect Bounds => new(App.Position.X, App.Position.Y, App.Size.Width, App.Size.Height);

        public int ClientHeight => App.ClientSize.Height;

        public double Scale => native.Content?.XamlRoot?.RasterizationScale ?? 1.0;

        public FrameRect WorkArea => Rect(DisplayArea.GetFromWindowId(App.Id, DisplayAreaFallback.Primary).WorkArea);

        public bool IsMaximized => Presenter?.State == OverlappedPresenterState.Maximized;

        public void Restore() => Presenter?.Restore();

        public void Maximize() => Presenter?.Maximize();

        public void MoveAndResize(FrameRect r) => App.MoveAndResize(new global::Windows.Graphics.RectInt32(r.X, r.Y, r.Width, r.Height));

        public void MinimizeWithRestoreBounds(FrameRect restoreBounds, bool restoreMaximized)
        {
            var saved = new global::Windows.Graphics.RectInt32(restoreBounds.X, restoreBounds.Y, restoreBounds.Width, restoreBounds.Height);
            var display = DisplayArea.GetFromRect(saved, DisplayAreaFallback.Primary);
            WindowPlacement.MinimizeWithRestoreBounds(WinRT.Interop.WindowNative.GetWindowHandle(native), saved,
                display.WorkArea.X - display.OuterBounds.X, display.WorkArea.Y - display.OuterBounds.Y, restoreMaximized);
        }

        private static FrameRect Rect(global::Windows.Graphics.RectInt32 r) => new(r.X, r.Y, r.Width, r.Height);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
