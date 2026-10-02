using System.Diagnostics;
using System.IO.Compression;

namespace Credentials.Launcher;

/// <summary>Lo que el lanzador necesita del sistema: sus recursos, donde desempaquetar y arrancar procesos.</summary>
public interface ILauncherHost
{
    /// <summary>Un recurso incrustado en el exe (app.zip, version.txt), o null si no esta.</summary>
    Stream? Resource(string name);

    /// <summary>Donde vive la aplicacion desempaquetada (%LOCALAPPDATA%\sOCCredentials\app).</summary>
    string AppRoot { get; }

    /// <summary>El exe del propio lanzador (para que la ventana se ancle a el).</summary>
    string? ProcessPath { get; }

    void Start(ProcessStartInfo info);

    void ShowError(string message);
}

/// <summary>
/// Un solo exe para el usuario: lleva la aplicacion WinUI dentro (app.zip), la deja en
/// &lt;AppRoot&gt;\&lt;version&gt; si no esta ya esa version, y la arranca desde ahi. Las versiones
/// anteriores se borran para no acumular carpetas.
/// </summary>
public static class LauncherCore
{
    public const string AppExe = "Credentials.exe";
    public const string CompleteMark = ".completa";

    public static int Run(ILauncherHost host, string[] args)
    {
        try
        {
            var version = Read(host.Resource("version.txt"), "version.txt").Trim();
            var target = Path.Combine(host.AppRoot, version);
            if (NeedsUnpack(target))
                Unpack(() => host.Resource("app.zip"), host.AppRoot, target, DateTime.Now);
            host.Start(StartInfo(target, host.ProcessPath, args));
            return 0;
        }
        catch (Exception ex)
        {
            host.ShowError("No se ha podido arrancar sOC Credentials:\n" + ex.Message);
            return 1;
        }
    }

    /// <summary>Hay que desempaquetar si falta el exe o la marca de que se termino de desempaquetar.</summary>
    public static bool NeedsUnpack(string target) =>
        !File.Exists(Path.Combine(target, AppExe)) || !File.Exists(Path.Combine(target, CompleteMark));

    /// <summary>La aplicacion, con los mismos argumentos y el lanzador en SOC_LAUNCHER (para anclarla a la barra de tareas).</summary>
    public static ProcessStartInfo StartInfo(string target, string? launcherPath, string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(target, AppExe)) { UseShellExecute = false, WorkingDirectory = target };
        info.Environment["SOC_LAUNCHER"] = launcherPath ?? string.Empty;
        foreach (var a in args)
            info.ArgumentList.Add(a);
        return info;
    }

    /// <summary>Desempaqueta a una carpeta temporal y la renombra al final: nunca queda a medias.</summary>
    public static void Unpack(Func<Stream?> zip, string root, string target, DateTime now)
    {
        Directory.CreateDirectory(root);
        var temp = target + ".nuevo";
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        using (var stream = zip() ?? throw new InvalidOperationException("Falta app.zip dentro del exe."))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            archive.ExtractToDirectory(temp);
        File.WriteAllText(Path.Combine(temp, CompleteMark), now.ToString("s"));
        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.Move(temp, target);

        // Las versiones anteriores sobran (si alguna esta en uso, se queda hasta la proxima).
        foreach (var old in Directory.GetDirectories(root))
        {
            if (string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(old, recursive: true); } catch (Exception) { }
        }
    }

    public static string Read(Stream? resource, string name)
    {
        using var stream = resource ?? throw new InvalidOperationException("Falta " + name + " dentro del exe.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
