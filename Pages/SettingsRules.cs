using Credentials.Services;

namespace Credentials.Pages;

/// <summary>Lo que se comprueba de la contraseña maestra al crearla o cambiarla.</summary>
public static class MasterPasswordRules
{
    public const int MinLength = 8;

    /// <summary>
    /// La clave del texto del problema («MasterPasswordEmpty», «MasterPasswordSpaces»,
    /// «MasterPasswordShort», «MasterPasswordMismatch»), o null si vale. Sin <paramref name="repeat"/>
    /// no se compara.
    /// </summary>
    /// <remarks>
    /// Al crearla o cambiarla no se admiten espacios (ni otros blancos) al principio o al final: no se
    /// ven al escribir con puntos, un teclado que los quita o los añade solo la deja inservible, y el
    /// usuario no sabria por que no abre. No se recortan en silencio, para que lo que se guarda sea
    /// exactamente lo que se escribio. Al DESBLOQUEAR esta regla no se aplica (solo se mira que no
    /// este vacia): las bovedas creadas antes pueden tener esos espacios y tienen que seguir abriendose.
    /// </remarks>
    public static string? Problem(string password, string? repeat)
    {
        if (password.Length == 0)
            return "MasterPasswordEmpty";
        if (HasEdgeSpaces(password))
            return "MasterPasswordSpaces";
        if (password.Length < MinLength)
            return "MasterPasswordShort";
        if (repeat is not null && repeat != password)
            return "MasterPasswordMismatch";
        return null;
    }

    /// <summary>Empieza o termina con un espacio, un tabulador u otro blanco.</summary>
    public static bool HasEdgeSpaces(string password) =>
        password.Length > 0 && (char.IsWhiteSpace(password[0]) || char.IsWhiteSpace(password[^1]));
}

/// <summary>
/// Lo que dice la pantalla de desbloqueo bajo el boton: si se puede abrir sin escribir la contraseña
/// (huella o cara en Android; «Confiar en este usuario y dispositivo» en Windows) y como activarlo.
/// </summary>
public static class UnlockHelp
{
    /// <summary>
    /// La clave del texto que toca, o null si no hace falta ninguno (la huella ya esta lista y su boton
    /// a la vista).
    /// </summary>
    /// <param name="creating">Se esta creando la boveda.</param>
    /// <param name="android">Android (huella o cara); si no, Windows (confiar en el dispositivo).</param>
    /// <param name="biometricAvailable">El dispositivo tiene huella o cara configuradas.</param>
    /// <param name="biometricOn">El ajuste de la huella esta encendido.</param>
    /// <param name="hasStoredKey">La clave esta guardada en la boveda del sistema.</param>
    /// <param name="trustOn">«Confiar en este usuario y dispositivo» esta encendido.</param>
    public static string? HintKey(bool creating, bool android, bool biometricAvailable, bool biometricOn, bool hasStoredKey, bool trustOn)
    {
        if (!android)
        {
            if (creating)
                return "UnlockHintTrustLater";
            return trustOn ? "UnlockHintTrustRestore" : "UnlockHintTrust";
        }
        if (!biometricAvailable)
            return creating ? null : "UnlockHintBioNone";
        if (creating)
            return "UnlockHintBioLater";
        if (!biometricOn)
            return "UnlockHintBioOff";
        return hasStoredKey ? null : "UnlockHintBioRestore";
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
