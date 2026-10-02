using Credentials.Services;

namespace Credentials.Pages;

/// <summary>Lo que se comprueba de la contraseña maestra al crearla o cambiarla.</summary>
public static class MasterPasswordRules
{
    public const int MinLength = 8;

    /// <summary>
    /// La clave del texto del problema («MasterPasswordEmpty», «MasterPasswordShort»,
    /// «MasterPasswordMismatch»), o null si vale. Sin <paramref name="repeat"/> no se compara.
    /// </summary>
    public static string? Problem(string password, string? repeat)
    {
        if (password.Length == 0)
            return "MasterPasswordEmpty";
        if (password.Length < MinLength)
            return "MasterPasswordShort";
        if (repeat is not null && repeat != password)
            return "MasterPasswordMismatch";
        return null;
    }
}

/// <summary>Las opciones y los textos de Ajustes que no dependen de la pantalla.</summary>
public static class SettingsRules
{
    /// <summary>Opciones del bloqueo por inactividad, en minutos (0 = nunca).</summary>
    public static readonly int[] LockMinutes = [0, 1, 2, 5, 10, 15, 30, 60];

    /// <summary>Opciones del vaciado del portapapeles, en segundos (0 = nunca).</summary>
    public static readonly int[] ClipSeconds = [0, 15, 30, 60, 120];

    public static List<string> LockLabels(ILocalizationService l) =>
        LockMinutes.Select(m => m == 0 ? l["AutoLockNever"] : string.Format(l.CurrentCulture, l["AutoLockMinutes"], m)).ToList();

    public static List<string> ClipLabels(ILocalizationService l) =>
        ClipSeconds.Select(s => s == 0 ? l["ClipboardNever"] : string.Format(l.CurrentCulture, l["ClipboardSeconds"], s)).ToList();

    /// <summary>La posicion de <paramref name="value"/> en las opciones; la primera si no esta.</summary>
    public static int IndexOf(int[] options, int value) => Math.Max(0, Array.IndexOf(options, value));

    /// <summary>
    /// La linea de estado de la nube que llega de la boveda: «cloud:ok:…» no se enseña, «cloud:error:…»
    /// sale como fallo de sincronizacion, y lo demas tal cual.
    /// </summary>
    public static string StatusText(string status, ILocalizationService l) =>
        status.StartsWith("cloud:ok:") ? string.Empty
        : status.StartsWith("cloud:error:") ? string.Format(l.CurrentCulture, l["SyncFailed"], status[12..])
        : status;

    /// <summary>El resultado de sincronizar: cuantas entradas cambiaron, o que no habia nada.</summary>
    public static string SyncText(int changed, ILocalizationService l) =>
        changed > 0 ? string.Format(l.CurrentCulture, l["SyncDone"], changed) : l["SyncNothing"];

    /// <summary>La palabra que confirma borrar la boveda: «BORRAR» o «DELETE», sin mirar mayusculas ni espacios.</summary>
    public static bool IsDeleteWord(string? word) =>
        word is not null && (word.Trim().Equals("BORRAR", StringComparison.OrdinalIgnoreCase) || word.Trim().Equals("DELETE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Nombre del fichero exportado: «sOCCredentials-AAAAMMDD-HHMM.ext».</summary>
    public static string ExportName(DateTime now, string extension) => $"sOCCredentials-{now:yyyyMMdd-HHmm}.{extension}";

    /// <summary>El proveedor por su nombre de marca, para el aviso de «no configurado».</summary>
    public static string BrandOf(StorageMode mode) => mode == StorageMode.GoogleDrive ? "Google" : "Microsoft";
}
