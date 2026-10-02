using System.Net;
using System.Text;
using System.Text.Json;
using Credentials.Services;
using Credentials.Tests;

namespace Credentials.Ui.Tests;

/// <summary>Google de mentira sobre <see cref="FakeHttp"/>: la entrada (token) y el fichero de la boveda en appDataFolder.</summary>
internal sealed class FakeGoogle
{
    public string? Content { get; set; }

    public FakeGoogle(FakeHttp http, string scope = "openid email https://www.googleapis.com/auth/drive.appdata")
    {
        http.OnJson(HttpMethod.Post, "oauth2.googleapis.com/token", new
        {
            access_token = "acc",
            refresh_token = "ref",
            expires_in = 3600,
            id_token = Jwt("{\"email\":\"ana@gmail.com\"}"),
            scope,
        });
        http.On(HttpMethod.Get, "drive/v3/files?spaces=appDataFolder", _ => FakeHttp.Response(HttpStatusCode.OK,
            Content is null ? "{\"files\":[]}" : "{\"files\":[{\"id\":\"F\",\"modifiedTime\":\"2026-09-30T00:00:00Z\"}]}"));
        http.On(HttpMethod.Get, "drive/v3/files/F?alt=media", _ => FakeHttp.Response(HttpStatusCode.OK, Content!));
        http.On(HttpMethod.Post, "upload/drive/v3/files?uploadType=multipart", r =>
        {
            var body = r.Content!.ReadAsStringAsync().Result;
            Content = body[body.IndexOf(VaultCrypto.Magic, StringComparison.Ordinal)..body.LastIndexOf('\n')] + "\n";
            return FakeHttp.Response(HttpStatusCode.OK, "{}");
        });
        http.On(HttpMethod.Patch, "upload/drive/v3/files/F", r =>
        {
            Content = r.Content!.ReadAsStringAsync().Result;
            return FakeHttp.Response(HttpStatusCode.OK, "{}");
        });
    }

    private static string Jwt(string claims)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64(claims)}.firma";
    }

    /// <summary>Una boveda de otra instalacion (otra sal) con esa contraseña y una entrada, como la dejaria en la nube.</summary>
    public static string OtherVault(string password, string title)
    {
        var other = new Models.VaultData();
        other.Entries.Add(new Models.Credential { Title = title });
        var header = VaultCrypto.NewHeader(other.VaultId);
        return VaultCrypto.Encrypt(JsonSerializer.Serialize(other), VaultCrypto.DeriveKey(password, header), header);
    }
}
