using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Minimizar una ventana y dejarle a la vez el tamaño y la posición que tendrá al restaurarla, sin
/// que se vea el cambio (SetWindowPlacement con la ventana ya minimizada). Las cuentas, en PlacementMath.
/// </summary>
internal static class WindowPlacement
{
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_MINIMIZE = 6;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
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

    internal delegate bool PlacementCall(IntPtr hWnd, ref WINDOWPLACEMENT placement);

    // Costuras para las pruebas: minimizar y recolocar una ventana se ve (y mueve el primer plano).
    internal static Func<IntPtr, int, bool> ShowWindowCall { get; set; } = ShowWindow;
    internal static PlacementCall GetPlacement { get; set; } = GetWindowPlacement;
    internal static PlacementCall SetPlacement { get; set; } = SetWindowPlacement;

    /// <summary>
    /// Minimiza <paramref name="hwnd"/> y deja como tamaño «normal» <paramref name="screen"/> (en
    /// coordenadas de pantalla; <paramref name="workOffsetX"/>/<paramref name="workOffsetY"/> es lo
    /// que la zona de trabajo se separa de la pantalla, porque la colocación va en coordenadas de la
    /// zona de trabajo).
    /// </summary>
    public static void MinimizeWithRestoreBounds(IntPtr hwnd, global::Windows.Graphics.RectInt32 screen,
        int workOffsetX, int workOffsetY, bool restoreMaximized)
    {
        ShowWindowCall(hwnd, SW_MINIMIZE);
        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetPlacement(hwnd, ref wp))
            return;
        var (left, top, right, bottom) = PlacementMath.NormalPosition(new ScreenRect(screen.X, screen.Y, screen.Width, screen.Height), workOffsetX, workOffsetY);
        wp.showCmd = SW_SHOWMINIMIZED;
        wp.rcNormalPosition = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        wp.flags = PlacementMath.Flags(wp.flags, restoreMaximized);
        SetPlacement(hwnd, ref wp);
    }
}
