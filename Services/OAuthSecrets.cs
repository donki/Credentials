using System.Reflection;

namespace Credentials.Services;

/// <summary>
/// Identificadores OAuth, leidos de los AssemblyMetadata que pone oauth.props (los valores vienen de
/// oauth.local.props, que no va al repositorio). Vacios si esa compilacion no los lleva: entonces el
/// proveedor no se ofrece.
/// </summary>
public static class OAuthSecrets
{
    private static readonly Dictionary<string, string> Values = typeof(OAuthSecrets).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(a => a.Key.StartsWith("Cr", StringComparison.Ordinal))
        .GroupBy(a => a.Key)
        .ToDictionary(g => g.Key, g => g.First().Value ?? string.Empty);

    private static string Get(string key) => Values.TryGetValue(key, out var v) ? v : string.Empty;

    public static string MicrosoftClientId => Get("CrMicrosoftClientId");
    public static string GoogleClientId => Get("CrGoogleClientId");
    public static string GoogleClientSecret => Get("CrGoogleClientSecret");
    public static string GoogleRedirectScheme => Get("CrGoogleRedirectScheme") is { Length: > 0 } s ? s : "com.socratic.credentials.google";
}
