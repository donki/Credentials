using System.Diagnostics;
using System.Reflection;

namespace Credentials.Launcher;

/// <summary>
/// Un solo exe para el usuario: lleva la aplicacion WinUI dentro (app.zip), la deja en
/// %LOCALAPPDATA%\sOCCredentials\app\&lt;version&gt; si no esta ya esa version, y la arranca desde
/// ahi. La logica esta en LauncherCore.cs (se prueba con carpetas temporales); aqui solo el sistema.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => LauncherCore.Run(new SystemHost(), args);

    private sealed class SystemHost : ILauncherHost
    {
        public Stream? Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

        public string AppRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials", "app");

        public string? ProcessPath => Environment.ProcessPath;

        public void Start(ProcessStartInfo info) => Process.Start(info);

        public void ShowError(string message) => MessageBox(IntPtr.Zero, message, "sOC Credentials", 0x10);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
