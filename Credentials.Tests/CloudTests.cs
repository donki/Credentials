using System.Net;
using System.Text.Json;
using Credentials.Services;

namespace Credentials.Tests;

public class OAuthClientTests
{
    private static readonly OAuthProvider Google = new("Google", "cliente", "secreto", "https://accounts.example/auth", "https://accounts.example/token", "openid email", "&access_type=offline");
    private static readonly OAuthProvider Microsoft = new("Microsoft", "cliente-ms", "", "https://login.example/authorize", "https://login.example/token", "openid Files.ReadWrite.AppFolder");

    [Fact]
    public async Task SignIn_Pkce_State_AndTokens()
    {
        var http = new FakeHttp().OnJson(HttpMethod.Post, "/token", new
        {
            access_token = "acc",
            refresh_token = "ref",
            expires_in = 120,
            id_token = Jwt.Make(new { email = "ana@example.com" }),
            scope = "openid https://www.googleapis.com/auth/drive.appdata",
        });
        var browser = new FakeBrowser();
        var client = new OAuthClient(http.Client(), browser, Google);

        var tokens = await client.SignInAsync();

        Assert.True(client.IsConfigured);
        Assert.Equal(("acc", "ref"), (tokens.AccessToken, tokens.RefreshToken));
        Assert.InRange(tokens.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(100), DateTimeOffset.UtcNow.AddSeconds(130));
        Assert.True(tokens.Has("HTTPS://www.googleapis.com/auth/drive.appdata"));
        Assert.False(tokens.Has("drive"));
        Assert.Equal("ana@example.com", OAuthClient.EmailOf(tokens));

        var authorize = browser.LastAuthorize!.AbsoluteUri;
        Assert.StartsWith("https://accounts.example/auth?client_id=cliente&response_type=code", authorize);
        Assert.Contains("code_challenge_method=S256", authorize);
        Assert.EndsWith("&access_type=offline", authorize);
        var body = http.Calls.Single().Body;
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=codigo-1", body);
        Assert.Contains("client_secret=secreto", body);
        Assert.Contains("code_verifier=", body);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/auth/?code=c&state=otro", "no corresponde")]
    [InlineData("http://127.0.0.1:5000/auth/?error=access_denied&error_description=Cancelado", "Cancelado")]
    [InlineData("http://127.0.0.1:5000/auth/?error=access_denied", "access_denied")]
    [InlineData("http://127.0.0.1:5000/auth/", "ningun codigo")]
    public async Task SignIn_BadCallback(string callback, string expected)
    {
        var client = new OAuthClient(new FakeHttp().Client(), new FakeBrowser { Callback = callback }, Microsoft);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync());
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70000: The grant is expired. Trace ID: 1\"}", "Microsoft: 400 invalid_grant AADSTS70000: The grant is expired")]
    [InlineData("<html>caido</html>", "Microsoft: 400 <html>caido</html>")]
    public async Task TokenError_IsShortAndLogged(string body, string expected)
    {
        using var box = Sandbox.Create();
        var http = new FakeHttp().On(HttpMethod.Post, "/token", HttpStatusCode.BadRequest, body);
        var client = new OAuthClient(http.Client(), new FakeBrowser(), Microsoft);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SignInAsync());
        Assert.Equal(expected, ex.Message);
        Assert.Contains(AppLog.Lines, l => l.StartsWith("[token] Microsoft 400"));
        Assert.DoesNotContain("client_secret", http.Calls.Single().Body);
    }

    [Fact]
    public async Task Refresh_OnlyWhenNeeded_KeepsRefreshToken()
    {
        var http = new FakeHttp().OnJson(HttpMethod.Post, "/token", new { access_token = "nuevo" });
        var client = new OAuthClient(http.Client(), new FakeBrowser(), Microsoft);

        var fresh = new OAuthTokens { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        Assert.Same(fresh, await client.RefreshIfNeededAsync(fresh));
        Assert.Empty(http.Calls);

        var old = new OAuthTokens { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTimeOffset.UtcNow };
        var renewed = await client.RefreshIfNeededAsync(old);
        Assert.Equal(("nuevo", "r"), (renewed.AccessToken, renewed.RefreshToken));
        Assert.Contains("scope=openid", http.Calls.Single().Body); // Microsoft pide el ambito al renovar

        var noRefresh = new OAuthTokens { AccessToken = "", RefreshToken = "", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshIfNeededAsync(noRefresh));
    }

    [Fact]
    public async Task Refresh_Google_SendsSecret_NoScope()
    {
        var http = new FakeHttp().OnJson(HttpMethod.Post, "/token", new { access_token = "n", refresh_token = "r2" });
        var tokens = await new OAuthClient(http.Client(), new FakeBrowser(), Google)
            .RefreshIfNeededAsync(new OAuthTokens { RefreshToken = "r" });
        Assert.Equal("r2", tokens.RefreshToken);
        Assert.Contains("client_secret=secreto", http.Calls.Single().Body);
        Assert.DoesNotContain("scope=", http.Calls.Single().Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-puntos")]
    [InlineData("a.@@@.b")]
    public void EmailOf_Garbage_IsEmpty(string idToken) => Assert.Equal(string.Empty, OAuthClient.EmailOf(new OAuthTokens { IdToken = idToken }));

    [Fact]
    public void EmailOf_FallsBackToOtherClaims()
    {
        Assert.Equal("ana@empresa.com", OAuthClient.EmailOf(new OAuthTokens { IdToken = Jwt.Make(new { preferred_username = "ana@empresa.com" }) }));
        Assert.Equal("upn@x.com", OAuthClient.EmailOf(new OAuthTokens { IdToken = Jwt.Make(new { upn = "upn@x.com" }) }));
        Assert.Equal(string.Empty, OAuthClient.EmailOf(new OAuthTokens { IdToken = Jwt.Make(new { sub = "1" }) }));
    }
}

public class CloudDriveTests
{
    private static Task<string> Token(CancellationToken _) => Task.FromResult("tok");

    [Fact]
    public async Task GoogleDrive_FirstUploadCreates_ThenPatches_ThenDownloads()
    {
        var http = new FakeHttp();
        var drive = new GoogleDrive(http.Client(), Token);
        Assert.Equal(StorageMode.GoogleDrive, drive.Mode);

        http.OnJson(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", new { files = Array.Empty<object>() });
        http.On(HttpMethod.Post, "upload/drive/v3/files?uploadType=multipart", HttpStatusCode.OK, "{}");
        Assert.Null(await drive.DownloadAsync());
        await drive.UploadAsync("contenido");

        var create = http.Calls.Single(c => c.Method == HttpMethod.Post);
        Assert.Contains("appDataFolder", create.Body);
        Assert.Contains("contenido", create.Body);
        Assert.Equal("tok", create.Request.Headers.Authorization!.Parameter);

        http.OnJson(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", new { files = new[] { new { id = "F1", modifiedTime = "2026-09-30T10:00:00Z" } } });
        http.On(HttpMethod.Patch, "upload/drive/v3/files/F1?uploadType=media", HttpStatusCode.OK, "{}");
        http.On(HttpMethod.Get, "drive/v3/files/F1?alt=media", HttpStatusCode.OK, "boveda");
        await drive.UploadAsync("v2");
        Assert.Equal("v2", http.Calls.Single(c => c.Method == HttpMethod.Patch).Body);

        var (content, modified) = (await drive.DownloadAsync())!.Value;
        Assert.Equal("boveda", content);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), modified);
    }

    [Fact]
    public async Task GoogleDrive_ErrorsBecomeCloudException()
    {
        var http = new FakeHttp().On(HttpMethod.Get, "drive/v3/files", HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":403,\"message\":\"Insufficient Permission\"}}");
        var ex = await Assert.ThrowsAsync<CloudException>(() => new GoogleDrive(http.Client(), Token).DownloadAsync());
        Assert.Equal("Google Drive: 403 Insufficient Permission", ex.Message);
        Assert.True(ex.IsScopeProblem);
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
        Assert.Equal("Google Drive", ex.Service);
        Assert.Contains("GET https://www.googleapis.com/drive/v3/files", ex.Detail);
        Assert.DoesNotContain("tok", ex.Detail);
    }

    [Fact]
    public async Task OneDrive_NotFound_Upload_Download()
    {
        var http = new FakeHttp();
        var drive = new OneDrive(http.Client(), Token);
        Assert.Equal(StorageMode.OneDrive, drive.Mode);

        http.On(HttpMethod.Get, "approot:/vault.soccred?select", HttpStatusCode.NotFound, "{}");
        Assert.Null(await drive.DownloadAsync());

        http.On(HttpMethod.Put, "approot:/vault.soccred:/content", HttpStatusCode.Created, "{}");
        await drive.UploadAsync("boveda");
        Assert.Equal("boveda", http.Calls.Single(c => c.Method == HttpMethod.Put).Body);

        http.OnJson(HttpMethod.Get, "approot:/vault.soccred?select", new { id = "ID9", lastModifiedDateTime = "2026-09-30T08:00:00Z" });
        http.On(HttpMethod.Get, "drive/items/ID9/content", HttpStatusCode.OK, "boveda");
        var got = await drive.DownloadAsync();
        Assert.Equal("boveda", got!.Value.Item1);
    }

    [Fact]
    public async Task OneDrive_Errors()
    {
        var http = new FakeHttp().On(HttpMethod.Get, "approot", HttpStatusCode.InternalServerError, "{\"error\":\"texto\"}");
        var ex = await Assert.ThrowsAsync<CloudException>(() => new OneDrive(http.Client(), Token).DownloadAsync());
        Assert.Equal("OneDrive: 500 texto", ex.Message);
        Assert.False(ex.IsScopeProblem);

        http.On(HttpMethod.Put, "approot", HttpStatusCode.Unauthorized, "");
        Assert.True((await Assert.ThrowsAsync<CloudException>(() => new OneDrive(http.Client(), Token).UploadAsync("x"))).IsScopeProblem);
    }

    [Fact]
    public void CloudException_LongBodiesAreCut()
    {
        var body = new string('x', 3000);
        var ex = new CloudException("S", HttpStatusCode.BadGateway, body);
        Assert.Equal($"S: 502 {new string('x', 160)}…", ex.Message);
        Assert.Equal(2000, ex.Detail.Length - "S 502  -> ".Length);

        Assert.Equal("S: 400 a b", new CloudException("S", HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"a\\nb\"}}").Message);
        Assert.Equal("S: 400 {\"error\":5}", new CloudException("S", HttpStatusCode.BadRequest, "{\"error\":5}").Message);
    }
}
