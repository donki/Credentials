using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Minimizar una ventana y dejarle a la vez el tamaño y la posición que tendrá al restaurarla, sin
/// que se vea el cambio (SetWindowPlacement con la ventana ya minimizada).
/// </summary>
internal static class WindowPlacement
{
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_MINIMIZE = 6;
    private const uint WPF_RESTORETOMAXIMIZED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public uint flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Minimiza <paramref name="hwnd"/> y deja como tamaño «normal» <paramref name="screen"/> (en
    /// coordenadas de pantalla; <paramref name="workOffsetX"/>/<paramref name="workOffsetY"/> es lo
    /// que la zona de trabajo se separa de la pantalla, porque la colocación va en coordenadas de la
    /// zona de trabajo).
    /// </summary>
    public static void MinimizeWithRestoreBounds(IntPtr hwnd, global::Windows.Graphics.RectInt32 screen,
        int workOffsetX, int workOffsetY, bool restoreMaximized)
    {
        ShowWindow(hwnd, SW_MINIMIZE);
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp))
            return;
        wp.showCmd = SW_SHOWMINIMIZED;
        wp.rcNormalPosition = new RECT
        {
            Left = screen.X - workOffsetX,
            Top = screen.Y - workOffsetY,
            Right = screen.X - workOffsetX + screen.Width,
            Bottom = screen.Y - workOffsetY + screen.Height,
        };
        wp.flags = restoreMaximized ? wp.flags | WPF_RESTORETOMAXIMIZED : wp.flags & ~WPF_RESTORETOMAXIMIZED;
        SetWindowPlacement(hwnd, ref wp);
    }
}
