using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Credentials.Services;

/// <summary>Donde vive el fichero de la boveda.</summary>
public enum StorageMode
{
    Local,
    GoogleDrive,
    OneDrive,
}

/// <summary>Con que proveedor se entra: identificadores de cliente y direcciones.</summary>
public sealed record OAuthProvider(string Name, string ClientId, string ClientSecret, string AuthorizeUrl, string TokenUrl, string Scopes, string ExtraAuthorizeParameters = "");

/// <summary>Lo que devuelve el proveedor y se guarda (en la boveda del sistema) para no volver a entrar.</summary>
public sealed class OAuthTokens
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string IdToken { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;

    public bool Has(string scope) => Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// El navegador del sistema por el que se entra y la direccion de vuelta. En Windows es un servidor
/// local de un solo uso (<c>http://127.0.0.1:puerto/auth/</c>); en Android, WebAuthenticator y un
/// esquema propio de la aplicacion (Google exige el identificador de cliente invertido).
/// </summary>
public interface IOAuthBrowser
{
    /// <summary>Direccion de vuelta para este proveedor.</summary>
    string RedirectUri(string providerName, string clientId);

    /// <summary>Abre la URL de autorizacion y devuelve la URL de vuelta con el codigo.</summary>
    Task<Uri> AuthenticateAsync(Uri authorizeUrl, Uri callback, CancellationToken cancellationToken);
}

/// <summary>Entrada OAuth con PKCE hablando directamente con Google o Microsoft (tres peticiones HTTP, sin bibliotecas).</summary>
public sealed class OAuthClient(HttpClient http, IOAuthBrowser browser, OAuthProvider provider)
{
    public bool IsConfigured => provider.ClientId.Length > 0;

    public async Task<OAuthTokens> SignInAsync(CancellationToken cancellationToken = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirect = browser.RedirectUri(provider.Name, provider.ClientId);
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var authorize = new Uri(
            $"{provider.AuthorizeUrl}?client_id={Uri.EscapeDataString(provider.ClientId)}" +
            $"&response_type=code&redirect_uri={Uri.EscapeDataString(redirect)}" +
            $"&scope={Uri.EscapeDataString(provider.Scopes)}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state={state}" +
            provider.ExtraAuthorizeParameters);

        var callback = await browser.AuthenticateAsync(authorize, new Uri(redirect), cancellationToken).ConfigureAwait(false);
        var query = System.Web.HttpUtility.ParseQueryString(callback.Query);
        if (query["state"] is { } s && s != state)
            throw new InvalidOperationException("La respuesta no corresponde a esta entrada.");
        var code = query["code"];
        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException(query["error_description"] ?? query["error"] ?? "El proveedor no devolvio ningun codigo.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = provider.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
        };
        if (provider.ClientSecret.Length > 0)
            form["client_secret"] = provider.ClientSecret;
        return await PostTokenAsync(form, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OAuthTokens> RefreshIfNeededAsync(OAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        if (tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2) && tokens.AccessToken.Length > 0)
            return tokens;
        if (tokens.RefreshToken.Length == 0)
            throw new InvalidOperationException("La sesion ha caducado: hay que volver a entrar.");
        var form = new Dictionary<string, string>
        {
            ["client_id"] = provider.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
        };
        if (provider.ClientSecret.Length > 0)
            form["client_secret"] = provider.ClientSecret;
        if (provider.Name == "Microsoft")
            form["scope"] = provider.Scopes;
        return await PostTokenAsync(form, tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>El correo de la cuenta, sacado del id_token (sin verificar: es solo para enseñarlo).</summary>
    public static string EmailOf(OAuthTokens tokens)
    {
        try
        {
            var parts = tokens.IdToken.Split('.');
            if (parts.Length < 2) return string.Empty;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            foreach (var claim in new[] { "email", "preferred_username", "upn" })
                if (doc.RootElement.TryGetProperty(claim, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString() ?? string.Empty;
        }
        catch (Exception) { }
        return string.Empty;
    }

    private async Task<OAuthTokens> PostTokenAsync(Dictionary<string, string> form, string? previousRefresh, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(provider.TokenUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{provider.Name}: {(int)response.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new OAuthTokens
        {
            AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty,
            RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? previousRefresh ?? string.Empty : previousRefresh ?? string.Empty,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600),
            IdToken = root.TryGetProperty("id_token", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? string.Empty : string.Empty,
        };
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Un fichero en la nube: bajarlo, subirlo y saber de cuando es.</summary>
public interface ICloudDrive
{
    StorageMode Mode { get; }
    Task<(string Content, DateTimeOffset ModifiedAt)?> DownloadAsync(CancellationToken cancellationToken = default);
    Task UploadAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>Google Drive, carpeta de datos de la aplicacion (<c>drive.appdata</c>): invisible para el usuario, solo esta aplicacion la lee.</summary>
public sealed class GoogleDrive(HttpClient http, Func<CancellationToken, Task<string>> token) : ICloudDrive
{
    public const string Scopes = "openid email https://www.googleapis.com/auth/drive.appdata";
    public const string FileName = "vault.soccred";

    public StorageMode Mode => StorageMode.GoogleDrive;

    public async Task<(string, DateTimeOffset)?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(cancellationToken).ConfigureAwait(false);
        if (file is null)
            return null;
        using var request = await RequestAsync(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{file.Value.Id}?alt=media", cancellationToken).ConfigureAwait(false);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), file.Value.ModifiedAt);
    }

    public async Task UploadAsync(string content, CancellationToken cancellationToken = default)
    {
        var file = await FindAsync(cancellationToken).ConfigureAwait(false);
        HttpRequestMessage request;
        if (file is null)
        {
            request = await RequestAsync(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart", cancellationToken).ConfigureAwait(false);
            var multipart = new MultipartContent("related");
            multipart.Add(new StringContent(JsonSerializer.Serialize(new { name = FileName, parents = new[] { "appDataFolder" } }), Encoding.UTF8, "application/json"));
            multipart.Add(new StringContent(content, Encoding.UTF8, "application/octet-stream"));
            request.Content = multipart;
        }
        else
        {
            request = await RequestAsync(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{file.Value.Id}?uploadType=media", cancellationToken).ConfigureAwait(false);
            request.Content = new StringContent(content, Encoding.UTF8, "application/octet-stream");
        }
        using (request)
        using (var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Id, DateTimeOffset ModifiedAt)?> FindAsync(CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"name = '{FileName}' and 'appDataFolder' in parents and trashed = false");
        using var request = await RequestAsync(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={query}&fields=files(id,modifiedTime)", cancellationToken).ConfigureAwait(false);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
            return (f.GetProperty("id").GetString()!, f.GetProperty("modifiedTime").GetDateTimeOffset());
        return null;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellationToken).ConfigureAwait(false));
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new CloudException("Google Drive", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>OneDrive, carpeta de la aplicacion (<c>special/approot</c>, ambito <c>Files.ReadWrite.AppFolder</c>): no da acceso al resto del OneDrive.</summary>
public sealed class OneDrive(HttpClient http, Func<CancellationToken, Task<string>> token) : ICloudDrive
{
    public const string Scopes = "openid email offline_access Files.ReadWrite.AppFolder";
    private const string Item = "https://graph.microsoft.com/v1.0/me/drive/special/approot:/vault.soccred";

    public StorageMode Mode => StorageMode.OneDrive;

    public async Task<(string, DateTimeOffset)?> DownloadAsync(CancellationToken cancellationToken = default)
    {
        using var meta = await RequestAsync(HttpMethod.Get, Item + "?select=id,lastModifiedDateTime", cancellationToken).ConfigureAwait(false);
        using var metaResponse = await http.SendAsync(meta, cancellationToken).ConfigureAwait(false);
        if (metaResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureAsync(metaResponse, cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await metaResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var modified = doc.RootElement.GetProperty("lastModifiedDateTime").GetDateTimeOffset();
        using var request = await RequestAsync(HttpMethod.Get, Item + ":/content", cancellationToken).ConfigureAwait(false);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), modified);
    }

    public async Task UploadAsync(string content, CancellationToken cancellationToken = default)
    {
        using var request = await RequestAsync(HttpMethod.Put, Item + ":/content", cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(content, Encoding.UTF8, "application/octet-stream");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellationToken).ConfigureAwait(false));
        return request;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new CloudException("OneDrive", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Un error HTTP de la nube: servicio, codigo y el mensaje del JSON de error (no el JSON entero).</summary>
public sealed class CloudException : Exception
{
    public string Service { get; }
    public System.Net.HttpStatusCode Status { get; }

    /// <summary>401/403: casi siempre el permiso de la carpeta no se concedio (Google lo enseña como una casilla).</summary>
    public bool IsScopeProblem => Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    public CloudException(string service, System.Net.HttpStatusCode status, string body) : base(Describe(service, status, body))
    {
        Service = service;
        Status = status;
    }

    private static string Describe(string service, System.Net.HttpStatusCode status, string body)
    {
        var message = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m))
                    message = m.GetString() ?? string.Empty;
                else if (error.ValueKind == JsonValueKind.String)
                    message = error.GetString() ?? string.Empty;
            }
        }
        catch (JsonException) { }
        if (message.Length == 0)
            message = body.Length > 160 ? body[..160] + "…" : body;
        return $"{service}: {(int)status} {message.ReplaceLineEndings(" ")}";
    }
}
