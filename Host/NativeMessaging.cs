using System.IO.Pipes;
using System.Text;

namespace Credentials.Host;

/// <summary>
/// Los marcos de la mensajeria nativa del navegador: un entero de 32 bits little-endian con la
/// longitud y despues el JSON en UTF-8. Sin consola ni tuberias: se prueba con flujos en memoria.
/// </summary>
public static class NativeMessaging
{
    /// <summary>Mas que esto no es un mensaje de la extension (el navegador limita a 64 MB de la app al host).</summary>
    public const int MaxLength = 64 * 1024 * 1024;

    /// <summary>El siguiente mensaje, o null si el navegador cerro el puerto (o mando algo sin sentido).</summary>
    public static string? ReadFrame(Stream input)
    {
        var header = new byte[4];
        if (!ReadExactly(input, header))
            return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > MaxLength)
            return null;
        var body = new byte[length];
        if (!ReadExactly(input, body))
            return null;
        return Encoding.UTF8.GetString(body);
    }

    /// <summary>Llena el bufer entero; false si el flujo se acaba antes.</summary>
    public static bool ReadExactly(Stream s, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
                return false;
            read += n;
        }
        return true;
    }

    public static void WriteFrame(Stream output, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        output.Write(BitConverter.GetBytes(body.Length), 0, 4);
        output.Write(body, 0, body.Length);
        output.Flush();
    }

    /// <summary>Respuesta de error con el mismo id que la peticion (sacado a mano: sin JSON de verdad aqui).</summary>
    public static string Error(string request, string code, string message)
    {
        var id = "null";
        var i = request.IndexOf("\"id\"", StringComparison.Ordinal);
        if (i >= 0)
        {
            var colon = request.IndexOf(':', i);
            if (colon >= 0)
            {
                var j = colon + 1;
                while (j < request.Length && (char.IsDigit(request[j]) || request[j] == ' ')) j++;
                var digits = request[(colon + 1)..j].Trim();
                if (digits.Length > 0) id = digits;
            }
        }
        return "{\"id\":" + id + ",\"error\":\"" + code + "\",\"detail\":\"" + message.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"}";
    }
}

/// <summary>Una conexion abierta con la aplicacion: una linea JSON por mensaje en cada sentido.</summary>
public interface IHostConnection : IDisposable
{
    bool IsConnected { get; }

    void WriteLine(string line);

    string? ReadLine();
}

/// <summary>Lo que el host necesita del sistema (tuberia, registro, procesos, reloj y registro de sucesos).</summary>
public interface IHostPlatform
{
    /// <summary>Conecta con la tuberia de la aplicacion; TimeoutException si no contesta a tiempo.</summary>
    IHostConnection Connect(int timeoutMs);

    /// <summary>Donde arrancar la aplicacion (lo deja ella en HKCU\Software\sOCratic\Credentials\AppPath).</summary>
    string? AppPath();

    bool FileExists(string path);

    void Start(string path, string arguments, string? workingDirectory);

    DateTime UtcNow { get; }

    void Sleep(int milliseconds);

    void Log(string line);
}

/// <summary>Una conexion sobre un flujo (la tuberia con nombre en el host de verdad).</summary>
public sealed class StreamConnection(Stream stream, Func<bool> isConnected) : IHostConnection
{
    private readonly StreamReader _reader = new(stream, new UTF8Encoding(false));
    private readonly StreamWriter _writer = new(stream, new UTF8Encoding(false)) { AutoFlush = true };

    public bool IsConnected => isConnected();

    public void WriteLine(string line) => _writer.WriteLine(line);

    public string? ReadLine() => _reader.ReadLine();

    public void Dispose() => stream.Dispose();
}

/// <summary>La conexion con la tuberia con nombre de la aplicacion (en esta misma maquina).</summary>
public static class PipeConnector
{
    /// <summary>Conecta con la tuberia; TimeoutException si nadie la atiende a tiempo.</summary>
    public static IHostConnection Connect(string pipeName, int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
        try { pipe.Connect(timeoutMs); }
        catch (Exception) { pipe.Dispose(); throw; }
        return new StreamConnection(pipe, () => pipe.IsConnected);
    }
}

/// <summary>
/// El puente entre la extension y la aplicacion: lee marcos del navegador, pasa cada uno por la
/// tuberia (arrancando la aplicacion si hace falta) y devuelve la respuesta con el mismo «id».
/// </summary>
public sealed class HostRelay(IHostPlatform platform)
{
    private IHostConnection? _connection;

    /// <summary>Cuanto se espera la primera vez a que la aplicacion conteste en la tuberia.</summary>
    public int FirstConnectMs { get; init; } = 1500;

    /// <summary>Cuanto se espera, como mucho, a que la aplicacion recien arrancada levante la tuberia.</summary>
    public TimeSpan StartupWait { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>El bucle del host: hasta que el navegador cierre el puerto. Devuelve el codigo de salida.</summary>
    public int Run(Stream stdin, Stream stdout)
    {
        while (true)
        {
            var request = NativeMessaging.ReadFrame(stdin);
            if (request is null)
            {
                platform.Log("el navegador cerro el puerto");
                Close();
                return 0;
            }
            string response;
            try
            {
                response = Relay(request);
            }
            catch (Exception ex)
            {
                Close();
                platform.Log("error: " + ex.Message);
                response = NativeMessaging.Error(request, ex is TimeoutException ? "noapp" : "app", ex.Message);
            }
            NativeMessaging.WriteFrame(stdout, response);
        }
    }

    /// <summary>Manda la peticion a la aplicacion (arrancandola si hace falta) y devuelve su respuesta.</summary>
    public string Relay(string request)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var connection = EnsureConnection();
                connection.WriteLine(request);
                var line = connection.ReadLine();
                if (line is not null)
                    return line;
                Close();   // la aplicacion cerro la tuberia: se reintenta una vez con una nueva
            }
            catch (IOException) when (attempt == 0)
            {
                Close();
            }
        }
        throw new IOException("La aplicacion ha cerrado la conexion.");
    }

    private IHostConnection EnsureConnection()
    {
        if (_connection is { IsConnected: true })
            return _connection;
        Close();
        IHostConnection connection;
        try
        {
            connection = platform.Connect(FirstConnectMs);
        }
        catch (TimeoutException)
        {
            // No esta abierta: se arranca escondida en la bandeja y se espera a que levante la tuberia.
            platform.Log("la aplicacion no responde en la tuberia: se arranca");
            StartApp();
            var deadline = platform.UtcNow + StartupWait;
            while (true)
            {
                try { connection = platform.Connect(1000); break; }
                catch (TimeoutException) when (platform.UtcNow < deadline) { platform.Sleep(300); }
            }
        }
        _connection = connection;
        return connection;
    }

    private void StartApp()
    {
        var path = platform.AppPath();
        if (string.IsNullOrEmpty(path) || !platform.FileExists(path))
            throw new TimeoutException("sOC Credentials no esta instalada (falta AppPath).");
        // «--background»: escondida en la bandeja y sin pedir la contraseña hasta que el usuario la
        // necesite (el navegador arranca la aplicacion por cosas pasivas, como la insignia de cada pestaña).
        platform.Start(path, "--background", Path.GetDirectoryName(path));
    }

    private void Close()
    {
        try { _connection?.Dispose(); } catch (Exception) { }
        _connection = null;
    }
}

/// <summary>El registro minimo del host: una linea con la hora; se vacia al pasar de medio mega.</summary>
public static class HostLog
{
    public const long MaxBytes = 500_000;

    public static void Append(string directory, string line, DateTime now, int processId)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "host.log");
            if (new FileInfo(file) is { Exists: true, Length: > MaxBytes })
                File.WriteAllText(file, string.Empty);
            File.AppendAllText(file, $"{now:yyyy-MM-dd HH:mm:ss} [{processId}] {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }
}
