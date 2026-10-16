using System.Runtime.InteropServices;
using Xaml = Microsoft.UI.Xaml;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Al minimizar, la ventana se esconde y queda un icono en el area de notificacion; clic para
/// volver, boton derecho para «Abrir» o «Salir» (menu WinUI con el aspecto comun, General 6.14).
/// WinUI no trae icono de bandeja, asi que va con Shell_NotifyIcon y una subclase del procedimiento de la ventana (para cazar SC_MINIMIZE). Que
/// hacer con cada mensaje lo decide TrayController (PlatformLogic, con pruebas); aqui, Win32.
/// </summary>
public sealed class TrayIcon : TrayController
{
    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;
    private readonly IntPtr _oldProc;
    private readonly Func<string, string> _text;
    private Xaml.Controls.MenuFlyout? _menu;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Cuanto lleva el PC sin teclado ni raton (GetLastInputInfo), para el bloqueo por inactividad real.</summary>
    public static TimeSpan? SystemIdle()
    {
        var info = new LastInputInfo { cbSize = Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? IdleTime(unchecked((uint)Environment.TickCount), info.dwTime) : null;
    }

    public TrayIcon(IntPtr hwnd, Func<string, string> text, Action exit) : base(SingleInstance.ShowMessage, exit)
    {
        _hwnd = hwnd;
        _text = text;
        _proc = (h, msg, wParam, lParam) => Handle(msg, (long)wParam, (long)lParam) ? IntPtr.Zero : CallWindowProc(_oldProc, h, msg, wParam, lParam);
        _oldProc = SetWindowLongPtr(hwnd, -4 /* GWLP_WNDPROC */, Marshal.GetFunctionPointerForDelegate(_proc));
        // Avisos de sesion (bloqueo, cierre) para esta ventana.
        WTSRegisterSessionNotification(hwnd, 0 /* NOTIFY_FOR_THIS_SESSION */);
    }

    protected override void ShowWindow(int command) => ShowWindow(_hwnd, command);

    protected override void BringToForeground() => SetForegroundWindow(_hwnd);

    protected override void NotifyShown() => SingleInstance.NotifyShown();

    /// <summary>
    /// El menu del boton derecho con el aspecto comun de las apps sOC (General 6.14): un MenuFlyout de
    /// WinUI en lugar del menu gris de Win32. Con la ventana principal escondida en la bandeja no hay
    /// donde colgarlo, asi que va sobre una ventana auxiliar de 1 px, sin bordes ni barra, puesta en el
    /// puntero: se activa (asi el menu pasa a primer plano y se cierra al pinchar fuera), muestra el menu
    /// y se cierra con el. Cada elemento manda su WM_COMMAND a la ventana principal, como el menu Win32,
    /// y TrayController lo atiende igual. Si algo falla, el menu Win32 de siempre.
    /// </summary>
    protected override void ShowMenu()
    {
        // Segundo clic derecho con el menu abierto: se cierra y ya.
        if (_menu is not null)
        {
            _menu.Hide();
            return;
        }
        GetCursorPos(out var p);
        Xaml.Window? host = null;
        try
        {
            var dark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
            var root = new Xaml.Controls.Grid
            {
                Background = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                RequestedTheme = dark ? Xaml.ElementTheme.Dark : Xaml.ElementTheme.Light,
            };
            host = new Xaml.Window { Content = root, Title = "sOC Credentials" };
            var hostHwnd = WinRT.Interop.WindowNative.GetWindowHandle(host);
            // Ni en la barra de tareas ni en Alt+Tab; sin marco, fija y encima de todo.
            host.AppWindow.IsShownInSwitchers = false;
            if (host.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }
            host.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(p.X, p.Y, 1, 1));

            var menu = BuildMenu(dark);
            _menu = menu;
            var window = host;
            menu.Closed += (_, _) =>
            {
                _menu = null;
                // Despues de que el menu acabe de cerrarse, no en medio.
                window.DispatcherQueue.TryEnqueue(() => { try { window.Close(); } catch (Exception) { } });
            };
            // Pinchar en otra aplicacion, el escritorio o la barra de tareas cierra el menu; el menu
            // mismo (su ventana emergente es de este proceso) no.
            host.Activated += (_, e) =>
            {
                if (e.WindowActivationState == Xaml.WindowActivationState.Deactivated && !ForegroundIsOurs())
                    menu.Hide();
            };
            root.Loaded += (_, _) =>
            {
                try
                {
                    SetForegroundWindow(hostHwnd);
                    menu.ShowAt(root, new Xaml.Controls.Primitives.FlyoutShowOptions
                    {
                        Position = new global::Windows.Foundation.Point(0, 0),
                        ShowMode = Xaml.Controls.Primitives.FlyoutShowMode.Standard,
                    });
                }
                catch (Exception)
                {
                    _menu = null;
                    try { window.Close(); } catch (Exception) { }
                    ShowNativeMenu(p);
                }
            };
            host.Activate();
        }
        catch (Exception)
        {
            _menu = null;
            try { host?.Close(); } catch (Exception) { }
            ShowNativeMenu(p);
        }
    }

    /// <summary>
    /// El MenuFlyout con las medidas de General 6.14: tarjeta de esquinas de 10 px, borde de 1 px,
    /// relleno de 4, minimo 240 de ancho y sin sombra; elementos de 32 px de alto con esquinas de 6,
    /// icono de 14 px en el indigo de la app (rojo si es destructivo) y fondo de seleccion al pasar.
    /// Los colores salen de Colors.xaml, en claro u oscuro segun el tema de la app.
    /// </summary>
    private Xaml.Controls.MenuFlyout BuildMenu(bool dark)
    {
        var card = Brush(dark ? "CardBackgroundDark" : "CardBackgroundLight", dark ? "#201F27" : "#FFFFFF");
        var separator = Brush(dark ? "SeparatorDark" : "SeparatorLight", dark ? "#48454F" : "#C7C4D8");
        var text = Brush(dark ? "TextPrimaryDark" : "TextPrimaryLight", dark ? "#E6E1E9" : "#191C1D");
        var selection = Brush(dark ? "SelectionSurfaceDark" : "SelectionSurfaceLight", dark ? "#2E2A55" : "#E4E1FF");
        var accent = Brush(dark ? "PrimaryTextDark" : "PrimaryTextLight", dark ? "#A9A3FF" : "#3525CD");
        var danger = Brush("Danger", "#BA1A1A");
        var transparent = new Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var theme = dark ? Xaml.ElementTheme.Dark : Xaml.ElementTheme.Light;

        var presenterStyle = new Xaml.Style(typeof(Xaml.Controls.MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.BackgroundProperty, card));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.BorderBrushProperty, separator));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.BorderThicknessProperty, new Xaml.Thickness(1)));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.CornerRadiusProperty, new Xaml.CornerRadius(10)));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.PaddingProperty, new Xaml.Thickness(4)));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.ForegroundProperty, text));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.FontSizeProperty, 13d));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.MinWidthProperty, 240d));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.RequestedThemeProperty, theme));
        presenterStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.MenuFlyoutPresenter.IsDefaultShadowEnabledProperty, false));

        var itemStyle = new Xaml.Style(typeof(Xaml.Controls.MenuFlyoutItem));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.BackgroundProperty, transparent));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.ForegroundProperty, text));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.FontSizeProperty, 13d));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.PaddingProperty, new Xaml.Thickness(8, 7, 10, 7)));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.CornerRadiusProperty, new Xaml.CornerRadius(6)));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.MinHeightProperty, 32d));
        itemStyle.Setters.Add(new Xaml.Setter(Xaml.FrameworkElement.MarginProperty, new Xaml.Thickness(0)));

        var separatorStyle = new Xaml.Style(typeof(Xaml.Controls.MenuFlyoutSeparator));
        separatorStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.BackgroundProperty, separator));
        separatorStyle.Setters.Add(new Xaml.Setter(Xaml.Controls.Control.PaddingProperty, new Xaml.Thickness(8, 4, 8, 4)));

        var menu = new Xaml.Controls.MenuFlyout
        {
            MenuFlyoutPresenterStyle = presenterStyle,
            // El menu sale de la ventana de 1 px: ventana emergente propia, no recortada a la anfitriona.
            ShouldConstrainToRootBounds = false,
        };
        var iconFont = new Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        foreach (var entry in MenuEntries)
        {
            if (entry.IsSeparator)
            {
                menu.Items.Add(new Xaml.Controls.MenuFlyoutSeparator { Style = separatorStyle });
                continue;
            }
            var item = new Xaml.Controls.MenuFlyoutItem
            {
                Text = _text(entry.TextKey),
                Style = itemStyle,
                Icon = new Xaml.Controls.FontIcon
                {
                    Glyph = entry.Glyph,
                    FontFamily = iconFont,
                    FontSize = 14,
                    Foreground = entry.Danger ? danger : accent,
                },
            };
            // Los colores de cada estado del elemento (estilo ligero: los busca la plantilla aqui antes que en el tema).
            item.Resources["MenuFlyoutItemBackground"] = transparent;
            item.Resources["MenuFlyoutItemBackgroundPointerOver"] = selection;
            item.Resources["MenuFlyoutItemBackgroundPressed"] = selection;
            item.Resources["MenuFlyoutItemForeground"] = text;
            item.Resources["MenuFlyoutItemForegroundPointerOver"] = text;
            item.Resources["MenuFlyoutItemForegroundPressed"] = text;
            var id = entry.Id;
            item.Click += (_, _) => PostMessage(_hwnd, WmCommand, new IntPtr(id), IntPtr.Zero);
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Un color de Colors.xaml como pincel de WinUI (con el valor de alli por si faltara).</summary>
    private static Xaml.Media.SolidColorBrush Brush(string key, string fallback)
    {
        var color = Microsoft.Maui.Controls.Application.Current?.Resources is { } resources
                    && resources.TryGetValue(key, out var value) && value is Color c
            ? c
            : Color.FromArgb(fallback);
        color.ToRgba(out var r, out var g, out var b, out var a);
        return new Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(a, r, g, b));
    }

    /// <summary>La ventana de delante es de este proceso (el propio menu, o la principal).</summary>
    private static bool ForegroundIsOurs()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>El menu gris de Win32, solo si el de WinUI no ha podido abrirse.</summary>
    private void ShowNativeMenu(Point p)
    {
        var menu = CreatePopupMenu();
        foreach (var entry in MenuEntries)
            AppendMenu(menu, entry.IsSeparator ? 0x800 /* MF_SEPARATOR */ : 0, entry.Id, entry.IsSeparator ? null : _text(entry.TextKey));
        // Sin esto el menu no se cierra al pinchar fuera (documentado en TrackPopupMenu).
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(menu, 0x0080 /* TPM_RIGHTBUTTON */, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    protected override void AddIcon()
    {
        var data = Data();
        data.uFlags = 0x1 | 0x2 | 0x4;   // NIF_MESSAGE | NIF_ICON | NIF_TIP
        data.uCallbackMessage = WmTray;
        data.hIcon = LoadAppIcon();
        data.szTip = "sOC Credentials";
        Shell_NotifyIcon(0 /* NIM_ADD */, ref data);
    }

    protected override void RemoveIcon()
    {
        var data = Data();
        Shell_NotifyIcon(2 /* NIM_DELETE */, ref data);
    }

    private NotifyIconData Data() => new() { cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _hwnd, uID = 1 };

    /// <summary>El icono del propio ejecutable; si no lo tiene, el generico de aplicacion.</summary>
    private static IntPtr LoadAppIcon()
    {
        var small = new IntPtr[1];
        try
        {
            if (Environment.ProcessPath is { } path && ExtractIconEx(path, 0, new IntPtr[1], small, 1) > 0 && small[0] != IntPtr.Zero)
                return small[0];
        }
        catch (Exception) { }
        return LoadIcon(IntPtr.Zero, new IntPtr(32512) /* IDI_APPLICATION */);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public int cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
