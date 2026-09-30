using System.Text;
using System.Text.Json;
using Credentials.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Credentials.Tests;

/// <summary>
/// Todo lo que una prueba necesita para usar la boveda sin tocar nada de verdad: carpeta temporal
/// propia, SecureStorage y Preferences en memoria, y un servidor HTTP falso.
/// </summary>
internal sealed class Sandbox : IDisposable
{
    private Sandbox()
    {
        Directory = Path.Combine(Path.GetTempPath(), "soccred-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        FileSystem.AppDataDirectory = Directory;
        SecureStorage.Default = new SecureStorage();
        Preferences.Values = [];
        AppLog.Lines.Clear();
        Settings = new SettingsService();
        Browser = new FakeBrowser();
        Store = new VaultStore(Settings, Browser, Http.Client());
    }

    public string Directory { get; }
    public FakeHttp Http { get; } = new();
    public FakeBrowser Browser { get; }
    public SettingsService Settings { get; }
    public VaultStore Store { get; }
    public SecureStorage Secure => SecureStorage.Default;

    /// <summary>Hay que llamarlo al principio de la prueba, antes del primer await.</summary>
    public static Sandbox Create() => new();

    public LocalizationService Texts(string language = "es")
    {
        Settings.Language = language;
        return new LocalizationService(Settings, NullLogger<LocalizationService>.Instance);
    }

    public void Dispose()
    {
        Store.Lock();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { }
    }
}

internal sealed class FakeBrowser : IOAuthBrowser
{
    public Uri? LastAuthorize { get; private set; }

    /// <summary>La vuelta del «navegador». Con {state} se copia el state de la peticion.</summary>
    public string Callback { get; set; } = "http://127.0.0.1:5000/auth/?code=codigo-1&state={state}";

    public string RedirectUri(string providerName, string clientId) => "http://127.0.0.1:5000/auth/";

    public Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken)
    {
        LastAuthorize = authorizeUrl;
        var state = System.Web.HttpUtility.ParseQueryString(authorizeUrl.Query)["state"] ?? string.Empty;
        return Task.FromResult(new Uri(Callback.Replace("{state}", state)));
    }
}

internal static class Jwt
{
    public static string Make(object claims)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(JsonSerializer.Serialize(claims))}.firma";
    }
}
