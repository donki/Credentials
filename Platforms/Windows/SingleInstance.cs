using System.Diagnostics;
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
/// <para>El aviso se espera confirmado: si la otra instancia no contesta (se quedo colgada, o es un
/// proceso sin ventana que aun tiene el mutex), esta arranca igual. Antes se iba en silencio y el
/// usuario no veia nada al abrir la aplicacion.</para>
/// <para><b>Manda la version nueva</b> (constitucion General 8.3): si la que esta abierta es de una
/// version anterior, se cierra y sigue esta. Pasaba en cada entrega: el navegador relanzaba la vieja
/// en segundo plano mientras se sustituia, y al abrir la nueva esta le pasaba el turno a la vieja.</para>
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
        CloseOlderVersions();
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
            // Si la otra acaba de arrancar (la abren a la vez el usuario y el navegador), aun no tiene
            // ventana que conteste: se repite el aviso cada segundo mientras siga viva, hasta 10 s.
            for (var i = 0; i < 10; i++)
            {
                PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                if (shown.WaitOne(TimeSpan.FromSeconds(1)))
                    return false;
                if (!OtherInstanceAlive())
                    break;
            }
        }
        catch (Exception) { }

        // Nadie ha contestado: el mutex es de un proceso que ya no atiende. Se arranca esta.
        try { _mutex = new Mutex(initiallyOwned: false, MutexName); } catch (Exception) { }
        return true;
    }

    /// <summary>Cierra las instancias de este mismo programa que sean de una version anterior a esta.</summary>
    private static void CloseOlderVersions()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            var mine = VersionOf(Environment.ProcessPath);
            if (mine is null)
                return;
            foreach (var other in Process.GetProcessesByName(me.ProcessName))
            {
                using (other)
                {
                    if (other.Id == me.Id)
                        continue;
                    try
                    {
                        // De otro usuario o sin permiso: MainModule lanza y se deja en paz.
                        var theirs = VersionOf(other.MainModule?.FileName);
                        if (theirs is null || theirs >= mine)
                            continue;
                        other.Kill();
                        other.WaitForExit(5000);
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception) { }
    }

    private static Version? VersionOf(string? exe)
    {
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return null;
        return Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var v) ? v : null;
    }

    private static bool OtherInstanceAlive()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            return Process.GetProcessesByName(me.ProcessName).Any(p => { using (p) return p.Id != me.Id && !p.HasExited; });
        }
        catch (Exception) { return false; }
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
