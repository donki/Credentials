using Credentials.Models;
using Credentials.Services;

namespace Credentials.Pages;

/// <summary>
/// Lo que decide que se ve en la lista de la boveda, sin nada de pantalla: el filtro de las fichas
/// (todas, favoritas, clase, carpeta o etiqueta), la busqueda, el orden, las fichas de filtro y los
/// textos de la fila y del contador.
/// </summary>
public static class VaultQuery
{
    /// <summary>Filtro sin nada: todas las entradas.</summary>
    public const string All = "all";

    /// <summary>Las entradas vivas que pasan el filtro y la busqueda, en el orden elegido.</summary>
    public static List<Credential> Apply(IEnumerable<Credential> source, string filter, string search, string sortMode)
    {
        var entries = source.Where(e => !e.Deleted);
        entries = filter switch
        {
            "fav" => entries.Where(e => e.Favorite),
            var f when f.StartsWith("kind:") => entries.Where(e => e.Kind.ToString() == f[5..]),
            var f when f.StartsWith("folder:") => entries.Where(e => string.Equals(e.Folder, f[7..], StringComparison.OrdinalIgnoreCase)),
            var f when f.StartsWith("tag:") => entries.Where(e => e.Tags.Contains(f[4..], StringComparer.OrdinalIgnoreCase)),
            _ => entries,
        };
        if (search.Length > 0)
        {
            var q = search.Trim();
            entries = entries.Where(e => e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Username.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Url.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Notes.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || e.Tags.Any(t => t.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
        }
        entries = sortMode switch
        {
            "modified" => entries.OrderByDescending(e => e.ModifiedAt),
            "created" => entries.OrderByDescending(e => e.CreatedAt),
            _ => entries.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
        };
        return entries.ToList();
    }

    /// <summary>Las fichas de filtro (clave y texto) que tienen sentido con lo que hay en la boveda.</summary>
    public static List<(string Key, string Text)> Chips(VaultData data, ILocalizationService l)
    {
        var all = data.Entries.Where(e => !e.Deleted).ToList();
        var chips = new List<(string, string)> { (All, l["AllEntries"]) };
        if (all.Any(e => e.Favorite))
            chips.Add(("fav", "★ " + l["Favorites"]));
        foreach (var kind in all.Select(e => e.Kind).Distinct().OrderBy(k => k))
            chips.Add(("kind:" + kind, l["Kind" + kind]));
        foreach (var folder in all.Select(e => e.Folder).Concat(data.Folders).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f))
            chips.Add(("folder:" + folder, "/" + folder));   // como ruta: las etiquetas van con «#»
        foreach (var tag in all.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t))
            chips.Add(("tag:" + tag, "#" + tag));
        return chips;
    }

    /// <summary>Pulsar una ficha: la activa, o la quita si ya lo estaba.</summary>
    public static string Toggle(string current, string chip) => current == chip ? All : chip;

    /// <summary>La carpeta del filtro activo (para dar de alta dentro), o vacio.</summary>
    public static string FolderOf(string filter) => filter.StartsWith("folder:") ? filter[7..] : string.Empty;

    /// <summary>«1 entrada» / «N entradas».</summary>
    public static string CountText(int count, ILocalizationService l) =>
        count == 1 ? l["OneEntry"] : string.Format(l.CurrentCulture, l["EntriesCount"], count);

    /// <summary>El codigo partido por la mitad, para leerlo mejor: «123 456».</summary>
    public static string SplitCode(string code) => $"{code[..(code.Length / 2)]} {code[(code.Length / 2)..]}";

    /// <summary>El codigo vivo de la fila con los segundos que le quedan; vacio si el secreto no vale.</summary>
    public static string RowCode(string totpUri)
    {
        if (Totp.Parse(totpUri) is not { } t)
            return string.Empty;
        var (code, left) = t.Now();
        return $"{SplitCode(code)} · {left}";
    }
}
