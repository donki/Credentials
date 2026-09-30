using System.Reflection;
using System.Text.RegularExpressions;
using Credentials.Services;

namespace Credentials.Tests;

internal static class TextTables
{
    private static Dictionary<string, string> Table(string name) =>
        (Dictionary<string, string>)typeof(LocalizationService)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    public static readonly Dictionary<string, string> English = Table("English");
    public static readonly Dictionary<string, string> Spanish = Table("Spanish");
}

public partial class TranslationsTests
{
    [Fact]
    public void SpanishAndEnglish_HaveTheSameKeys()
    {
        Assert.Empty(TextTables.English.Keys.Except(TextTables.Spanish.Keys));
        Assert.Empty(TextTables.Spanish.Keys.Except(TextTables.English.Keys));
    }

    [Fact]
    public void NoTextIsEmpty()
    {
        Assert.DoesNotContain(TextTables.English, kv => string.IsNullOrWhiteSpace(kv.Value));
        Assert.DoesNotContain(TextTables.Spanish, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    /// <summary>
    /// Fallo encontrado por esta prueba (2026-09-30): dos textos en castellano estaban escritos dentro
    /// de la tabla inglesa. Un inicializador con indices no se queja de una clave repetida: la segunda
    /// pisa a la primera, y en ingles salia «La entrada no se completó a tiempo».
    /// </summary>
    [Fact]
    public void NoKeyIsWrittenTwiceInTheSameTable()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "LocalizationService.cs"));
        var spanishStart = source.IndexOf("Dictionary<string, string> Spanish", StringComparison.Ordinal);
        var englishStart = source.IndexOf("Dictionary<string, string> English", StringComparison.Ordinal);
        Assert.True(englishStart > 0 && spanishStart > englishStart);

        foreach (var block in new[] { source[englishStart..spanishStart], source[spanishStart..] })
        {
            var keys = Regex.Matches(block, @"^\s*\[""([^""]+)""\]\s*=", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(keys);
            Assert.Empty(keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key));
        }
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Un «{1}» de mas en una traduccion revienta con FormatException al pintar.</summary>
    [Fact]
    public void Placeholders_MatchBetweenLanguages()
    {
        static string Set(string text) =>
            string.Join(",", Placeholder().Matches(text.Replace("{{", "").Replace("}}", ""))
                .Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order());

        var mismatched = TextTables.English.Keys
            .Where(k => TextTables.Spanish.ContainsKey(k) && Set(TextTables.English[k]) != Set(TextTables.Spanish[k]))
            .Select(k => $"{k}: en[{Set(TextTables.English[k])}] es[{Set(TextTables.Spanish[k])}]")
            .ToList();
        Assert.Empty(mismatched);
    }

    /// <summary>Regla de Josep: «dispositivo», nunca «aparato».</summary>
    [Fact]
    public void Spanish_SaysDispositivo() =>
        Assert.Empty(TextTables.Spanish.Where(kv => kv.Value.Contains("aparato", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key));

    /// <summary>Cada clave escrita a mano en las pantallas existe (si no, sale la clave en crudo).</summary>
    [Fact]
    public void EveryLiteralKeyUsedByTheApp_Exists()
    {
        var root = RepoRoot();
        var pattern = new Regex(@"\b(?:_l|l|loc|_loc|Loc|texts|_texts)\s*\[\s*""([A-Za-z][A-Za-z0-9_]*)""\s*\]");
        var used = new SortedSet<string>();
        foreach (var file in SourceFiles(root))
        {
            var text = string.Join('\n', File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//")));
            foreach (Match m in pattern.Matches(text))
                used.Add(m.Groups[1].Value);
        }

        Assert.True(used.Count > 100, $"Solo {used.Count} claves: el patron ya no reconoce como se piden.");
        Assert.Empty(used.Where(k => !TextTables.English.ContainsKey(k)));
    }

    private static IEnumerable<string> SourceFiles(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
            yield return file;
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (Path.GetFileName(sub) is "bin" or "obj" or "Resources" or "Credentials.Tests" or "Extension" or "constitution" or "releases" or "store" or ".git")
                continue;
            foreach (var file in SourceFiles(sub))
                yield return file;
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Credentials.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encuentra Credentials.csproj");
    }
}
