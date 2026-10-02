using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Al minimizar, la ventana se esconde y queda un icono en el area de notificacion; clic para
/// volver, boton derecho para «Abrir» o «Salir». WinUI no trae icono de bandeja, asi que va con
/// Shell_NotifyIcon y una subclase del procedimiento de la ventana (para cazar SC_MINIMIZE). Que
/// hacer con cada mensaje lo decide TrayController (PlatformLogic, con pruebas); aqui, Win32.
/// </summary>
public sealed class TrayIcon : TrayController
{
    private readonly IntPtr _hwnd;
    private readonly WndProc _proc;
    private readonly IntPtr _oldProc;
    private readonly Func<string, string> _text;

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

    protected override void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IdOpen, _text("TrayOpen"));
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0, IdExit, _text("TrayExit"));
        GetCursorPos(out var p);
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
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);
}
