namespace Credentials.Services;

/// <summary>
/// Registro de la aplicacion para lo que no se ve en pantalla: los fallos de la nube con la
/// peticion que fallo y lo que contesto el servidor. Nunca lleva contraseñas, tokens ni datos de la
/// boveda. En Windows va a <c>%LOCALAPPDATA%\sOCCredentials\logs\app.log</c>; en Android, a la
/// carpeta de datos de la aplicacion y tambien a logcat (etiqueta «sOCCredentials»).
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    private static string FilePath
    {
        get
        {
            var dir = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sOCCredentials", "logs")
                : Path.Combine(FileSystem.AppDataDirectory, "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "app.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message.ReplaceLineEndings(" ")}";
#if ANDROID
            global::Android.Util.Log.Warn("sOCCredentials", line);
#endif
            lock (Gate)
            {
                var path = FilePath;
                // Tope de 1 MB: se queda la mitad mas reciente.
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                {
                    var keep = File.ReadAllText(path);
                    File.WriteAllText(path, keep[(keep.Length / 2)..]);
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception) { /* el registro nunca rompe nada */ }
    }

    public static void Error(string where, Exception ex) =>
        Write($"[{where}] {ex.GetType().Name}: {(ex is CloudException c ? c.Detail : ex.Message)}");
}
