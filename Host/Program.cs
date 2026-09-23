using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;

namespace Credentials.Host;

/// <summary>
/// Puente entre la extension del navegador y la aplicacion. Protocolo del navegador (mensajeria
/// nativa): cada mensaje es un entero de 32 bits little-endian con la longitud y despues el JSON.
/// Protocolo con la aplicacion: una linea JSON por mensaje, por la tuberia sOCCredentials. Aqui no
/// se interpreta nada; solo se pasa de un lado a otro con el mismo «id».
/// </summary>
internal static class Program
{
    private const string PipeName = "sOCCredentials";
    private const string AppKey = @"Software\sOCratic\Credentials";

    private static NamedPipeClientStream? _pipe;
    private static StreamReader? _reader;
    private static StreamWriter? _writer;

    private static int Main(string[] args)
    {
        // Un registro minimo: cuando algo falla, el navegador solo dice «desconectado» y no hay forma
        // de saber si el host no arranco, si no encontro la aplicacion o si se corto la tuberia.
        Log("arranca; argumentos: " + string.Join(" ", args));
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        while (true)
        {
            var request = ReadFrame(stdin);
            if (request is null)
            {
                Log("el navegador cerro el puerto");
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
                Log("error: " + ex.Message);
                response = Error(request, ex is TimeoutException ? "noapp" : "app", ex.Message);
            }
            WriteFrame(stdout, response);
        }
    }

    /// <summary>Manda la peticion a la aplicacion (arrancandola si hace falta) y devuelve su respuesta.</summary>
    private static string Relay(string request)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                EnsurePipe();
                _writer!.WriteLine(request);
                var line = _reader!.ReadLine();
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

    private static void EnsurePipe()
    {
        if (_pipe is { IsConnected: true })
            return;
        Close();
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
        try
        {
            pipe.Connect(1500);
        }
        catch (TimeoutException)
        {
            // No esta abierta: se arranca escondida en la bandeja y se espera a que levante la tuberia.
            Log("la aplicacion no responde en la tuberia: se arranca");
            StartApp();
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (true)
            {
                try { pipe.Connect(1000); break; }
                catch (TimeoutException) when (DateTime.UtcNow < deadline) { Thread.Sleep(300); }
            }
        }
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false));
        _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private static void StartApp()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\" + AppKey, "AppPath", null) as string;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new TimeoutException("sOC Credentials no esta instalada (falta AppPath).");
        // UseShellExecute: que la aplicacion no herede las tuberias del navegador. «--background»:
        // escondida en la bandeja y sin pedir la contraseña hasta que el usuario la necesite (el
        // navegador arranca la aplicacion por cosas pasivas, como la insignia de cada pestaña).
        Process.Start(new ProcessStartInfo(path, "--background") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
    }

    /// <summary>Una linea con la hora en %LOCALAPPDATA%\sOCCredentials\logs\host.log (se recorta al crecer).</summary>
    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials", "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "host.log");
            if (new FileInfo(file) is { Exists: true, Length: > 500_000 })
                File.WriteAllText(file, string.Empty);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {line}{Environment.NewLine}");
        }
        catch (Exception) { }
    }

    private static void Close()
    {
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null; _reader = null; _writer = null;
    }

    // ------------------------------------------------------------------ marcos del navegador

    private static string? ReadFrame(Stream stdin)
    {
        var header = new byte[4];
        if (!ReadExactly(stdin, header))
            return null;
        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > 64 * 1024 * 1024)
            return null;
        var body = new byte[length];
        if (!ReadExactly(stdin, body))
            return null;
        return Encoding.UTF8.GetString(body);
    }

    private static bool ReadExactly(Stream s, byte[] buffer)
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

    private static void WriteFrame(Stream stdout, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        stdout.Write(BitConverter.GetBytes(body.Length), 0, 4);
        stdout.Write(body, 0, body.Length);
        stdout.Flush();
    }

    /// <summary>Respuesta de error con el mismo id que la peticion (sacado a mano: sin JSON de verdad aqui).</summary>
    private static string Error(string request, string code, string message)
    {
        var id = "null";
        var i = request.IndexOf("\"id\"", StringComparison.Ordinal);
        if (i >= 0)
        {
            var colon = request.IndexOf(':', i);
            var j = colon + 1;
            while (j < request.Length && (char.IsDigit(request[j]) || request[j] == ' ')) j++;
            var digits = request[(colon + 1)..j].Trim();
            if (digits.Length > 0) id = digits;
        }
        return "{\"id\":" + id + ",\"error\":\"" + code + "\",\"detail\":\"" + message.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"}";
    }
}
