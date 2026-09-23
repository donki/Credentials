using System.Runtime.InteropServices;
using System.Threading;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Una sola instancia por sesion: si la aplicacion ya esta abierta (aunque este escondida en el area
/// de notificacion), volver a ejecutarla trae la ventana existente al frente y este proceso se va.
/// Se sabe por un mutex con nombre; el aviso a la otra instancia es un mensaje de ventana propio
/// (registrado con RegisterWindowMessage), que atiende el procedimiento de ventana de TrayIcon.
/// </summary>
/// <remarks>
/// El aviso se espera confirmado: si la otra instancia no contesta (se quedo colgada, o es un
/// proceso sin ventana que aun tiene el mutex), esta arranca igual. Antes se iba en silencio y el
/// usuario no veia nada al abrir la aplicacion.
/// </remarks>
internal static class SingleInstance
{
    private const string MutexName = "Local\\sOCCredentials.SingleInstance";
    private const string ShownEventName = "Local\\sOCCredentials.Shown";
    private static Mutex? _mutex;

    /// <summary>El mensaje «enseñate», compartido por todas las instancias del mismo usuario.</summary>
    public static uint ShowMessage { get; } = RegisterWindowMessage("sOCCredentials.Show");

    /// <summary>True si esta instancia tiene que seguir arrancando; false si ya hay otra que se ha puesto delante.</summary>
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
        try
        {
            using var shown = new EventWaitHandle(false, EventResetMode.AutoReset, ShownEventName);
            shown.Reset();
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero);
            // Un par de segundos: lo que tarda una ventana escondida en volver, incluso con el disco ocupado.
            if (shown.WaitOne(TimeSpan.FromSeconds(2)))
                return false;
        }
        catch (Exception) { }

        // Nadie ha contestado: el mutex es de un proceso que ya no atiende. Se arranca esta.
        try { _mutex = new Mutex(initiallyOwned: false, MutexName); } catch (Exception) { }
        return true;
    }

    /// <summary>La instancia abierta avisa de que ha atendido el «enseñate» (y por tanto la otra se puede ir).</summary>
    public static void NotifyShown()
    {
        try
        {
            using var shown = new EventWaitHandle(false, EventResetMode.AutoReset, ShownEventName);
            shown.Set();
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
