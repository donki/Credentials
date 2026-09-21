using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Una sola instancia por sesion: si la aplicacion ya esta abierta (aunque este escondida en el area
/// de notificacion), volver a ejecutarla trae la ventana existente al frente y este proceso se va.
/// Se sabe por un mutex con nombre; el aviso a la otra instancia es un mensaje de ventana propio
/// (registrado con RegisterWindowMessage), que atiende el procedimiento de ventana de TrayIcon.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = "Local\\sOCCredentials.SingleInstance";
    private static Mutex? _mutex;

    /// <summary>El mensaje «enseñate», compartido por todas las instancias del mismo usuario.</summary>
    public static uint ShowMessage { get; } = RegisterWindowMessage("sOCCredentials.Show");

    /// <summary>True si esta es la primera instancia (y se queda con el mutex); false si ya hay otra, a la que se le ha pedido que se enseñe.</summary>
    public static bool Claim()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew)
                return true;
            _mutex.Dispose();
            _mutex = null;
        }
        catch (Exception)
        {
            return true;   // sin mutex (raro) se arranca igual: mejor dos ventanas que ninguna
        }
        // La otra instancia: se le manda el aviso a su ventana (aunque este oculta sigue existiendo).
        // Este proceso es el que acaba de arrancar el usuario: cede el derecho a ponerse delante.
        AllowSetForegroundWindow(-1 /* ASFW_ANY */);
        PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero);
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
