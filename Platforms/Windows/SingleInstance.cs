using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credentials.Platforms.Windows;

/// <summary>
/// Una sola instancia por sesion: si la aplicacion ya esta abierta (aunque este escondida en el area
/// de notificacion), volver a ejecutarla trae la ventana existente al frente y este proceso se va.
/// Se sabe por un mutex con nombre; el aviso a la otra instancia es un mensaje de ventana propio
/// (registrado con RegisterWindowMessage), que atiende el procedimiento de ventana de TrayIcon.
/// </summary>
/// <remarks>
/// <para>La logica (esperar la confirmacion, cerrar las versiones anteriores) esta en
/// SingleInstanceCore y los mutex, eventos y procesos en SystemInstances (PlatformLogic, con
/// pruebas); aqui solo los mensajes de ventana.</para>
/// <para>El aviso se espera confirmado: si la otra instancia no contesta (se quedo colgada, o es un
/// proceso sin ventana que aun tiene el mutex), esta arranca igual. Antes se iba en silencio y el
/// usuario no veia nada al abrir la aplicacion.</para>
/// <para><b>Manda la version nueva</b> (constitucion General 8.3): si la que esta abierta es de una
/// version anterior, se cierra y sigue esta. Pasaba en cada entrega: el navegador relanzaba la vieja
/// en segundo plano mientras se sustituia, y al abrir la nueva esta le pasaba el turno a la vieja.</para>
/// </remarks>
internal static class SingleInstance
{
    private const string ShownEventName = @"Local\sOCCredentials.Shown";

    /// <summary>Se guarda para que el mutex dure lo que dure el proceso.</summary>
    private static SystemInstances? _instances;

    /// <summary>El mensaje «enseñate», compartido por todas las instancias del mismo usuario.</summary>
    public static uint ShowMessage { get; } = RegisterWindowMessage("sOCCredentials.Show");

    /// <summary>True si esta instancia tiene que seguir arrancando; false si ya hay otra que se ha puesto delante.</summary>
    public static bool Claim()
    {
        using var me = Process.GetCurrentProcess();
        _instances = new SystemInstances(@"Local\sOCCredentials.SingleInstance", ShownEventName, me.ProcessName, Environment.ProcessPath,
            () => AllowSetForegroundWindow(-1 /* ASFW_ANY */), () => PostMessage(new IntPtr(0xFFFF) /* HWND_BROADCAST */, ShowMessage, IntPtr.Zero, IntPtr.Zero));
        return new SingleInstanceCore(_instances).Claim();
    }

    /// <summary>La instancia abierta avisa de que ha atendido el «enseñate» (y por tanto la otra se puede ir).</summary>
    public static void NotifyShown() => SystemInstances.NotifyShown(ShownEventName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
