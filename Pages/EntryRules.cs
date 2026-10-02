using Credentials.Models;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// Lo que hace la ficha de una entrada sin nada de pantalla: leer lo escrito (etiquetas, carpeta,
/// enlace, codigos de respaldo), la fuerza de la contraseña, como se enseña el segundo factor, y
/// guardar la entrada en la boveda con el historial de contraseñas.
/// </summary>
public static class EntryRules
{
    /// <summary>Colores de la barra de fuerza, de muy debil (0) a muy fuerte (4).</summary>
    private static readonly string[] StrengthColors = ["#BA1A1A", "#D97706", "#CA8A04", "#0E9F6E", "#059669"];

    public static Color StrengthColor(int strength) => Color.FromArgb(StrengthColors[strength is >= 0 and <= 3 ? strength : 4]);

    /// <summary>Lo lleno de la barra: nada sin contraseña; si no, un quinto por cada nivel.</summary>
    public static double StrengthProgress(string password, int strength) => password.Length == 0 ? 0 : (strength + 1) / 5.0;

    /// <summary>El enlace para abrir en el navegador: con «https://» si no lleva esquema; null si no hay.</summary>
    public static string? OpenableUrl(string? text)
    {
        var url = (text ?? string.Empty).Trim();
        if (url.Length == 0)
            return null;
        return url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
    }

    /// <summary>Etiquetas separadas por comas, sin vacias ni repetidas (sin mirar mayusculas).</summary>
    public static List<string> ParseTags(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>La carpeta sin espacios ni barras al principio o al final.</summary>
    public static string NormalizeFolder(string? text) => (text ?? string.Empty).Trim().Trim('/');

    /// <summary>Codigos de respaldo pegados: uno por linea, o separados por comas, puntos y coma o tabuladores.</summary>
    public static List<string> ParseRecovery(string? text) =>
        (text ?? string.Empty).Split(['\n', '\r', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => c.Length > 0)
            .ToList();

    /// <summary>Añade los codigos que no esten ya (sin mirar mayusculas). Devuelve cuantos entraron.</summary>
    public static int AddRecovery(List<RecoveryCode> list, IEnumerable<string> codes)
    {
        var added = 0;
        foreach (var code in codes)
        {
            if (list.Any(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(new RecoveryCode { Code = code });
            added++;
        }
        return added;
    }

    /// <summary>El resumen de los codigos de respaldo: ninguno, o cuantos hay y cuantos quedan sin usar.</summary>
    public static string RecoverySummary(IReadOnlyCollection<RecoveryCode> codes, ILocalizationService l) =>
        codes.Count == 0
            ? l["RecoveryNone"]
            : string.Format(l.CurrentCulture, l["RecoveryCount"], codes.Count, codes.Count(r => !r.Used));

    /// <summary>Emisor y cuenta del segundo factor con sus parametros: «GitHub · ana  (SHA1, 6, 30s)».</summary>
    public static string TotpDescription(Totp t) =>
        string.Join(" · ", new[] { t.Issuer, t.Account }.Where(s => s.Length > 0)) + $"  ({t.Algorithm}, {t.Digits}, {t.Period}s)";

    /// <summary>La clave secreta en grupos de cuatro, como la dan los sitios.</summary>
    public static string SeedGroups(string secret) => string.Join(" ", secret.TrimEnd('=').Chunk(4).Select(c => new string(c)));

    /// <summary>Lo que queda de la ventana del codigo, de 1 a 0 (los de contador no caducan).</summary>
    public static double TotpProgress(Totp t, int secondsLeft) => t.IsCounter ? 1 : secondsLeft / (double)t.Period;

    /// <summary>
    /// Al guardar una entrada que ya existia con otra contraseña, la anterior pasa al historial
    /// (la mas reciente primero) con la fecha en que se puso.
    /// </summary>
    public static void RecordHistory(Credential original, Credential entry, bool isNew, string newPassword)
    {
        if (!isNew && original.Password.Length > 0 && newPassword != original.Password)
            entry.History.Insert(0, new PasswordHistoryItem(original.Password, original.ModifiedAt));
    }

    /// <summary>Escribe la entrada en la boveda (sustituye la que tenga su Id o la añade) y da de alta su carpeta.</summary>
    public static void Commit(VaultData data, Credential entry)
    {
        var index = data.Entries.FindIndex(x => x.Id == entry.Id);
        if (index >= 0)
            data.Entries[index] = entry;
        else
            data.Entries.Add(entry);
        if (entry.Folder.Length > 0 && !data.Folders.Contains(entry.Folder, StringComparer.OrdinalIgnoreCase))
            data.Folders.Add(entry.Folder);
    }
}
