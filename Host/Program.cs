using System.Diagnostics;
using Microsoft.Win32;

namespace Credentials.Host;

/// <summary>
/// Puente entre la extension del navegador y la aplicacion. Protocolo del navegador (mensajeria
/// nativa): cada mensaje es un entero de 32 bits little-endian con la longitud y despues el JSON.
/// Protocolo con la aplicacion: una linea JSON por mensaje, por la tuberia sOCCredentials. Aqui no
/// se interpreta nada; solo se pasa de un lado a otro con el mismo «id». La logica esta en
/// NativeMessaging.cs (HostRelay), que se prueba sin tuberias; aqui solo lo que toca el sistema.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Un registro minimo: cuando algo falla, el navegador solo dice «desconectado» y no hay forma
        // de saber si el host no arranco, si no encontro la aplicacion o si se corto la tuberia.
        var platform = new SystemPlatform();
        platform.Log("arranca; argumentos: " + string.Join(" ", args));
        return new HostRelay(platform).Run(Console.OpenStandardInput(), Console.OpenStandardOutput());
    }

    private sealed class SystemPlatform : IHostPlatform
    {
        private const string PipeName = "sOCCredentials";
        private const string AppKey = @"HKEY_CURRENT_USER\Software\sOCratic\Credentials";

        public IHostConnection Connect(int timeoutMs) => PipeConnector.Connect(PipeName, timeoutMs);

        public string? AppPath() => Registry.GetValue(AppKey, "AppPath", null) as string;

        public bool FileExists(string path) => File.Exists(path);

        // UseShellExecute: que la aplicacion no herede las tuberias del navegador.
        public void Start(string path, string arguments, string? workingDirectory) =>
            Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true, WorkingDirectory = workingDirectory ?? string.Empty });

        public DateTime UtcNow => DateTime.UtcNow;

        public void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

        /// <summary>Una linea en %LOCALAPPDATA%\sOCCredentials\logs\host.log.</summary>
        public void Log(string line) =>
            HostLog.Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials", "logs"), line, DateTime.Now, Environment.ProcessId);
    }
}
