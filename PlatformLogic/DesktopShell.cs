using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Credentials.Platforms.Windows;

// La logica de las piezas pequeñas del escritorio de Windows (una sola instancia, entrada con el
// navegador por 127.0.0.1, identidad en la barra de tareas y colocacion de la ventana). Lo que toca
// Windows esta en Platforms/Windows detras de estas interfaces; aqui todo se prueba con dobles.

/// <summary>El aviso «ya me he enseñado» entre instancias (un evento con nombre en Windows).</summary>
public interface IShownSignal : IDisposable
{
    void Reset();

    bool Wait(TimeSpan timeout);
}

/// <summary>Otra instancia de este programa.</summary>
public interface IOtherInstance : IDisposable
{
    /// <summary>Su version (lanza si es de otro usuario o no hay permiso).</summary>
    Version? Version { get; }

    void Kill();

    void WaitForExit(int milliseconds);
}

/// <summary>Lo que necesita SingleInstanceCore de Windows (mutex, eventos y procesos con nombre).</summary>
public interface ISingleInstanceNative
{
    /// <summary>Crea el mutex de la instancia y se queda con el; false si ya lo tenia otra (lanza si no se puede).</summary>
    bool TryClaimMutex();

    /// <summary>Se queda con el mutex sin esperar a nadie (cuando la otra no contesta).</summary>
    void TakeOverMutex();

    IShownSignal OpenShownSignal();

    /// <summary>Cede a la otra instancia el derecho a ponerse delante (AllowSetForegroundWindow).</summary>
    void AllowForeground();

    /// <summary>Manda a todas las ventanas el mensaje «enseñate».</summary>
    void BroadcastShow();

    bool OtherInstanceAlive();

    /// <summary>La version de este exe, o null si no se sabe.</summary>
    Version? MyVersion();

    /// <summary>Las demas instancias de este programa (no la propia).</summary>
    IEnumerable<IOtherInstance> OtherInstances();
}

/// <summary>
/// Una sola instancia por sesion: si la aplicacion ya esta abierta, volver a ejecutarla trae la
/// ventana existente al frente y este proceso se va. Si la otra no contesta (colgada, o un proceso
/// sin ventana que aun tiene el mutex), esta arranca igual. Manda la version nueva (constitucion
/// General 8.3): las instancias de una version anterior se cierran antes.
/// </summary>
public sealed class SingleInstanceCore(ISingleInstanceNative native)
{
    /// <summary>Cuantas veces se repite el aviso (una por segundo) mientras la otra siga viva.</summary>
    public int Attempts { get; init; } = 10;

    public TimeSpan WaitPerAttempt { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>True si esta instancia tiene que seguir arrancando; false si ya hay otra que se ha puesto delante.</summary>
    public bool Claim()
    {
        CloseOlderVersions();
        try
        {
            if (native.TryClaimMutex())
                return true;
        }
        catch (Exception)
        {
            return true;   // sin mutex (raro) se arranca igual: mejor dos ventanas que ninguna
        }

        // La otra instancia: se le manda el aviso a su ventana (aunque este oculta sigue existiendo).
        // Este proceso es el que acaba de arrancar el usuario: cede el derecho a ponerse delante.
        try
        {
            using var shown = native.OpenShownSignal();
            shown.Reset();
            native.AllowForeground();
            // Si la otra acaba de arrancar (la abren a la vez el usuario y el navegador), aun no tiene
            // ventana que conteste: se repite el aviso mientras siga viva.
            for (var i = 0; i < Attempts; i++)
            {
                native.BroadcastShow();
                if (shown.Wait(WaitPerAttempt))
                    return false;
                if (!native.OtherInstanceAlive())
                    break;
            }
        }
        catch (Exception) { }

        // Nadie ha contestado: el mutex es de un proceso que ya no atiende. Se arranca esta.
        try { native.TakeOverMutex(); } catch (Exception) { }
        return true;
    }

    /// <summary>Cierra las instancias de este mismo programa que sean de una version anterior a esta.</summary>
    public void CloseOlderVersions()
    {
        try
        {
            var mine = native.MyVersion();
            if (mine is null)
                return;
            foreach (var other in native.OtherInstances())
            {
                using (other)
                {
                    try
                    {
                        // De otro usuario o sin permiso: leer la version lanza y se deja en paz.
                        var theirs = other.Version;
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

    /// <summary>La version de un FileVersion («2026.10.1.0»), o null.</summary>
    public static Version? ParseVersion(string? fileVersion) => Version.TryParse(fileVersion, out var v) ? v : null;
}

/// <summary>Una peticion que llega al servidor local de la entrada con el navegador.</summary>
public interface ILoopbackRequest
{
    Uri? Url { get; }

    /// <summary>El navegador volvio con «error» en la consulta (el usuario cancelo o el proveedor se nego).</summary>
    bool HasError { get; }

    /// <summary>Contesta solo con un codigo (404 a lo que no es la vuelta).</summary>
    void Respond(int statusCode);

    /// <summary>Contesta con una pagina HTML.</summary>
    Task RespondHtmlAsync(byte[] body);
}

/// <summary>El servidor local de un solo uso (HttpListener en Windows).</summary>
public interface ILoopbackListener : IDisposable
{
    void Start();

    /// <summary>La siguiente peticion; lanza HttpListenerException u ObjectDisposedException si se aborta.</summary>
    Task<ILoopbackRequest> NextAsync();

    void Abort();

    void Stop();
}

/// <summary>
/// Entrar con Google o Microsoft en el escritorio: navegador del sistema y un servidor local de un
/// solo uso en 127.0.0.1 que espera la vuelta al camino registrado.
/// </summary>
public static class LoopbackOAuth
{
    public static async Task<Uri> AuthenticateAsync(ILoopbackListener listener, Uri callback, Action openBrowser, CancellationToken cancellationToken)
    {
        listener.Start();
        openBrowser();
        using var registration = cancellationToken.Register(listener.Abort);
        while (true)
        {
            ILoopbackRequest request;
            // Si el tiempo de espera vence, Abort tumba el listener y la espera sale con
            // HttpListenerException u ObjectDisposedException segun el momento: las dos son «cancelado».
            try { request = await listener.NextAsync().ConfigureAwait(false); }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // El navegador pide tambien /favicon.ico y cosas asi: solo vale la vuelta al camino registrado.
            var url = request.Url ?? callback;
            if (!url.AbsolutePath.Equals(callback.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                request.Respond(404);
                continue;
            }
            try { await request.RespondHtmlAsync(Encoding.UTF8.GetBytes(Page(!request.HasError))).ConfigureAwait(false); }
            catch (Exception) { /* el navegador ya tiene lo que queria; la pagina de cortesia es lo de menos */ }
            listener.Stop();
            return url;
        }
    }

    /// <summary>La pagina de cortesia que ve el usuario en el navegador al volver.</summary>
    public static string Page(bool ok)
    {
        var text = ok ? "Ya puedes volver a la aplicaci&oacute;n." : "La entrada no se ha completado.";
        return "<html><body style=\"font-family:Segoe UI;background:#14161F;color:#eee;text-align:center;padding-top:80px\"><h2>sOC Credentials</h2><p>" + text + "</p></body></html>";
    }
}

/// <summary>El servidor local de verdad: HttpListener en 127.0.0.1 con el camino de vuelta registrado.</summary>
public sealed class HttpLoopbackListener(Uri callback) : ILoopbackListener
{
    private readonly HttpListener _listener = new() { Prefixes = { callback.ToString() } };

    /// <summary>Un puerto libre de 127.0.0.1 (el sistema lo elige).</summary>
    public static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Start() => _listener.Start();
    public async Task<ILoopbackRequest> NextAsync() => new Request(await _listener.GetContextAsync().ConfigureAwait(false));
    public void Abort() => _listener.Abort();
    public void Stop() => _listener.Stop();
    public void Dispose() => ((IDisposable)_listener).Dispose();

    private sealed class Request(HttpListenerContext context) : ILoopbackRequest
    {
        public Uri? Url => context.Request.Url;
        public bool HasError => context.Request.QueryString["error"] is not null;

        public void Respond(int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.Close();
        }

        public async Task RespondHtmlAsync(byte[] body)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, CancellationToken.None).ConfigureAwait(false);
            context.Response.Close();
        }
    }
}

/// <summary>
/// Identidad de la ventana para la barra de tareas cuando la aplicacion arranca por un lanzador:
/// AppUserModelID y, si hay lanzador, las propiedades de relanzamiento (comando, nombre e icono)
/// para que «anclar» apunte al lanzador y no al exe de la carpeta versionada.
/// </summary>
public static class TaskbarProperties
{
    public const uint Id = 5, RelaunchCommand = 2, RelaunchIconResource = 3, RelaunchDisplayNameResource = 4;

    /// <summary>Las propiedades (PKEY_AppUserModel_* por su numero) y sus valores, en orden.</summary>
    public static List<(uint Pid, string Value)> For(string appUserModelId, string displayName, string? launcherPath, Func<string, bool> exists)
    {
        var list = new List<(uint, string)> { (Id, appUserModelId) };
        if (!string.IsNullOrEmpty(launcherPath) && exists(launcherPath))
        {
            list.Add((RelaunchCommand, "\"" + launcherPath + "\""));
            list.Add((RelaunchDisplayNameResource, displayName));
            list.Add((RelaunchIconResource, launcherPath + ",0"));
        }
        return list;
    }
}

/// <summary>La colocacion «normal» (al restaurar) de una ventana minimizada, en coordenadas de la zona de trabajo.</summary>
public static class PlacementMath
{
    public const uint RestoreToMaximized = 0x2;   // WPF_RESTORETOMAXIMIZED

    /// <summary>(izquierda, arriba, derecha, abajo) de <paramref name="screen"/> menos lo que la zona de trabajo se separa de la pantalla.</summary>
    public static (int Left, int Top, int Right, int Bottom) NormalPosition(ScreenRect screen, int workOffsetX, int workOffsetY) =>
        (screen.X - workOffsetX, screen.Y - workOffsetY, screen.X - workOffsetX + screen.Width, screen.Y - workOffsetY + screen.Height);

    public static uint Flags(uint flags, bool restoreMaximized) => restoreMaximized ? flags | RestoreToMaximized : flags & ~RestoreToMaximized;
}

/// <summary>
/// Los mutex, eventos y procesos con nombre de verdad (.NET, sin ventanas). Lo que va por mensajes de
/// ventana (ceder el primer plano, el «enseñate» a todas) se le pasa: lo pone SingleInstance. Los
/// nombres son parametros para que las pruebas usen los suyos y no toquen los de la aplicacion.
/// </summary>
public sealed class SystemInstances(string mutexName, string shownEventName, string processName, string? processPath, Action allowForeground, Action broadcastShow) : ISingleInstanceNative
{
    private Mutex? _mutex;

    /// <summary>El mutex de la instancia (se guarda para que dure lo que dure el proceso).</summary>
    public Mutex? Mutex => _mutex;

    public bool TryClaimMutex()
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
            return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public void TakeOverMutex() => _mutex = new Mutex(initiallyOwned: false, mutexName);

    public IShownSignal OpenShownSignal() => new Signal(new EventWaitHandle(false, EventResetMode.AutoReset, shownEventName));

    /// <summary>La instancia abierta avisa de que ha atendido el «enseñate» (y por tanto la otra se puede ir).</summary>
    public static void NotifyShown(string shownEventName)
    {
        try
        {
            using var shown = new EventWaitHandle(false, EventResetMode.AutoReset, shownEventName);
            shown.Set();
        }
        catch (Exception) { }
    }

    public void AllowForeground() => allowForeground();

    public void BroadcastShow() => broadcastShow();

    public Version? MyVersion() => VersionOf(processPath);

    public bool OtherInstanceAlive()
    {
        try
        {
            var others = OtherInstances().Cast<Other>().ToList();
            var alive = others.Any(p => !p.Process.HasExited);
            others.ForEach(p => p.Dispose());
            return alive;
        }
        catch (Exception) { return false; }
    }

    public IEnumerable<IOtherInstance> OtherInstances()
    {
        var me = Environment.ProcessId;
        var list = new List<IOtherInstance>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(processName))
        {
            if (p.Id == me)
                p.Dispose();
            else
                list.Add(new Other(p));
        }
        return list;
    }

    /// <summary>La version de un exe (FileVersion), o null si no esta o no la tiene.</summary>
    public static Version? VersionOf(string? exe) =>
        string.IsNullOrEmpty(exe) || !File.Exists(exe) ? null : SingleInstanceCore.ParseVersion(System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion);

    private sealed class Signal(EventWaitHandle handle) : IShownSignal
    {
        public void Reset() => handle.Reset();
        public bool Wait(TimeSpan timeout) => handle.WaitOne(timeout);
        public void Dispose() => handle.Dispose();
    }

    private sealed class Other(System.Diagnostics.Process process) : IOtherInstance
    {
        public System.Diagnostics.Process Process => process;
        public Version? Version => VersionOf(process.MainModule?.FileName);
        public void Kill() => process.Kill();
        public void WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public void Dispose() => process.Dispose();
    }
}
