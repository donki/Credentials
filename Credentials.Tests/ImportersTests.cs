using System.Text;
using System.Text.Json;
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Tests;

public class ImportersTests
{
    private static Importers.Result Parse(string content) => Importers.Parse(content) ?? throw new Xunit.Sdk.XunitException("No se reconocio el formato.");

    [Fact]
    public void Chrome_Csv()
    {
        var r = Parse("﻿name,url,username,password,note\r\n" +
                      "GitHub,https://github.com/login,ana,\"con,coma\",\"nota \"\"citada\"\"\r\nen dos lineas\"\r\n" +
                      ",https://sin-nombre.example.com/x,bea,p2,\r\n" +
                      ",,,,\r\n" +
                      ",,,solo-clave,\r\n");
        Assert.Equal("CSV", r.Source);
        Assert.Equal(2, r.Entries.Count);
        var gh = r.Entries[0];
        Assert.Equal(("GitHub", "https://github.com/login", "ana", "con,coma"), (gh.Title, gh.Url, gh.Username, gh.Password));
        // Dentro de comillas el salto de linea se conserva tal cual (RFC 4180).
        Assert.Equal("nota \"citada\"\r\nen dos lineas", gh.Notes);
        Assert.Equal(EntryKind.Login, gh.Kind);
        Assert.Equal("sin-nombre.example.com", r.Entries[1].Title);
    }

    [Fact]
    public void Firefox_Csv_WithTimestamps()
    {
        var r = Parse("\"url\",\"username\",\"password\",\"httpRealm\",\"formActionOrigin\",\"guid\",\"timeCreated\",\"timeLastUsed\",\"timePasswordChanged\"\n" +
                      "\"https://www.mozilla.org\",\"ana\",\"pw\",,\"https://www.mozilla.org\",\"{x}\",\"1700000000000\",\"1700000000000\",\"1700000500\"\n");
        Assert.Equal("Firefox", r.Source);
        var e = r.Entries.Single();
        Assert.Equal("www.mozilla.org", e.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), e.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000500), e.ModifiedAt);
    }

    [Fact]
    public void Safari_Csv_WithOtp()
    {
        var r = Parse("Title,URL,Username,Password,Notes,OTPAuth\n" +
                      "Banco,https://banco.es,ana,pw,,otpauth://totp/Banco:ana?secret=JBSWY3DPEHPK3PXP&issuer=Banco\n" +
                      "Nota suelta,,,,texto secreto,\n");
        Assert.Equal("Safari", r.Source);
        Assert.Equal("JBSWY3DPEHPK3PXP", Totp.Parse(r.Entries[0].Totp)!.Secret);
        Assert.Equal(EntryKind.Note, r.Entries[1].Kind);
    }

    [Fact]
    public void KeePass_And_KeePassXC_Csv()
    {
        var kp = Parse("\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\n\"Correo\",\"ana\",\"pw\",\"mail.example.com\",\"c\"\n");
        Assert.Equal("KeePass", kp.Source);
        Assert.Equal(("Correo", "ana", "mail.example.com", "c"), (kp.Entries[0].Title, kp.Entries[0].Username, kp.Entries[0].Url, kp.Entries[0].Notes));

        var xc = Parse("Group,Title,Username,Password,URL,Notes,TOTP\n\"Root\\Trabajo\\\",Servidor,root,pw,ssh.example.com,,JBSW Y3DP EHPK 3PXP\n");
        Assert.Equal("KeePassXC", xc.Source);
        Assert.Equal("Root/Trabajo", xc.Entries[0].Folder);
        Assert.Equal("JBSWY3DPEHPK3PXP", Totp.Parse(xc.Entries[0].Totp)!.Secret);
    }

    [Fact]
    public void Bitwarden_Csv()
    {
        var r = Parse("folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n" +
                      "Personal,1,login,Google,,,0,https://accounts.google.com,ana,pw,\n" +
                      ",,note,Alarma,codigo 1234,,0,,,,\n");
        Assert.Equal("Bitwarden", r.Source);
        Assert.True(r.Entries[0].Favorite);
        Assert.Equal("Personal", r.Entries[0].Folder);
        Assert.Equal(EntryKind.Note, r.Entries[1].Kind);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    public void OtherSeparators(char sep)
    {
        var r = Parse($"name{sep}url{sep}username{sep}password\nA{sep}a.com{sep}u{sep}p\n");
        Assert.Equal(("A", "u", "p"), (r.Entries[0].Title, r.Entries[0].Username, r.Entries[0].Password));
    }

    [Theory]
    [InlineData("name,url,username\nA,a.com,u\n")]
    [InlineData("password,notes\npw,n\n")]
    [InlineData("name,url,username,password\n")]
    [InlineData("name,url,username,password\n,,,\n")]
    [InlineData("hola que tal")]
    [InlineData("")]
    public void Csv_NotRecognised(string content) => Assert.Null(Importers.Parse(content));

    [Fact]
    public void ParseCsv_Rfc4180()
    {
        var rows = Importers.ParseCsv("a,\"b,1\",\"c\"\"d\"\r\n\"multi\nlinea\",,x");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b,1", "c\"d"], rows[0]);
        Assert.Equal(["multi\nlinea", "", "x"], rows[1]);
    }

    [Fact]
    public void SocCredentials_Json_SkipsDeleted()
    {
        var data = new VaultData();
        data.Entries.Add(new Credential { Title = "viva" });
        data.Entries.Add(new Credential { Title = "borrada", Deleted = true });
        var r = Parse(JsonSerializer.Serialize(data));
        Assert.Equal("sOC Credentials", r.Source);
        Assert.Equal(["viva"], r.Entries.Select(e => e.Title));
    }

    [Fact]
    public void Aegis_Json()
    {
        var json = """
        { "version": 1, "db": { "entries": [
            { "type": "totp", "name": "ana@example.com", "issuer": "GitHub", "note": "trabajo",
              "info": { "secret": "JBSWY3DPEHPK3PXP", "algo": "sha256", "digits": 8, "period": 60 } },
            { "type": "hotp", "info": { "secret": "GEZDGNBVGY3TQOJQ", "counter": 5 } },
            { "type": "totp", "name": "sin secreto", "info": { "secret": "" } }
        ] } }
        """;
        var r = Parse(json);
        Assert.Equal("Aegis", r.Source);
        Assert.Equal(2, r.Entries.Count);
        var gh = r.Entries[0];
        Assert.Equal((EntryKind.Totp, "GitHub", "ana@example.com", "trabajo"), (gh.Kind, gh.Title, gh.Username, gh.Notes));
        var t = Totp.Parse(gh.Totp)!;
        Assert.Equal(("SHA256", 8, 60), (t.Algorithm, t.Digits, t.Period));
        var h = Totp.Parse(r.Entries[1].Totp)!;
        Assert.True(h.IsCounter);
        Assert.Equal(5, h.Counter);
        Assert.Equal("TOTP", r.Entries[1].Title);
    }

    [Fact]
    public void TwoFas_Json()
    {
        var json = """
        { "services": [
            { "name": "Dropbox", "secret": "JBSWY3DPEHPK3PXP", "otp": { "account": "ana", "digits": 6, "period": 30, "algorithm": "SHA1", "tokenType": "TOTP" } },
            { "name": "Contador", "secret": "GEZDGNBVGY3TQOJQ", "otp": { "issuer": "Emisor", "tokenType": "HOTP", "counter": 3 } },
            { "name": "Sin otp", "secret": "GEZDGNBVGY3TQOJQ" },
            { "name": "Vacio" }
        ] }
        """;
        var r = Parse(json);
        Assert.Equal("2FAS", r.Source);
        Assert.Equal(["Dropbox", "Emisor", "Sin otp"], r.Entries.Select(e => e.Title));
        Assert.True(Totp.Parse(r.Entries[1].Totp)!.IsCounter);
        Assert.Equal(3, Totp.Parse(r.Entries[1].Totp)!.Counter);
    }

    [Fact]
    public void Bitwarden_Json()
    {
        var json = """
        { "folders": [ { "id": "f1", "name": "Trabajo" } ],
          "items": [
            { "type": 1, "name": "Jira", "notes": null, "favorite": true, "folderId": "f1",
              "login": { "username": "ana", "password": "pw", "totp": "JBSWY3DPEHPK3PXP", "uris": [ { "uri": "" }, { "uri": "https://jira.example.com" } ] },
              "fields": [ { "name": "PIN", "value": "1234", "type": 1 }, { "name": "Nota", "value": "x", "type": 0 } ] },
            { "type": 2, "name": "Caja fuerte", "notes": "combinacion", "folderId": "otra" },
            { "type": 1, "name": "", "login": { } }
          ] }
        """;
        var r = Parse(json);
        Assert.Equal("Bitwarden", r.Source);
        Assert.Equal(2, r.Entries.Count);
        var jira = r.Entries[0];
        Assert.Equal(("Trabajo", "https://jira.example.com", true, ""), (jira.Folder, jira.Url, jira.Favorite, jira.Notes));
        Assert.Equal("JBSWY3DPEHPK3PXP", Totp.Parse(jira.Totp)!.Secret);
        Assert.True(jira.Fields[0].Hidden);
        Assert.False(jira.Fields[1].Hidden);
        Assert.Equal((EntryKind.Note, "combinacion", ""), (r.Entries[1].Kind, r.Entries[1].Notes, r.Entries[1].Folder));
    }

    [Theory]
    [InlineData("{ \"otra\": 1 }")]
    [InlineData("[1, 2]")]
    public void UnknownJson_IsNull(string json) => Assert.Null(Importers.Parse(json));

    [Fact]
    public void SingleOtpauthUri()
    {
        var r = Parse("otpauth://totp/Steam:ana?secret=JBSWY3DPEHPK3PXP\nlinea que sobra");
        Assert.Equal("otpauth", r.Source);
        Assert.Equal("Steam", r.Entries.Single().Title);
        Assert.Null(Importers.Parse("otpauth://totp/x?issuer=sin"));
    }

    // ---------------------------------------------------------------- Google Authenticator

    private static void Varint(List<byte> o, long v)
    {
        var u = (ulong)v;
        while (u >= 0x80) { o.Add((byte)(u | 0x80)); u >>= 7; }
        o.Add((byte)u);
    }

    private static void Bytes(List<byte> o, int field, byte[] b)
    {
        Varint(o, (field << 3) | 2);
        Varint(o, b.Length);
        o.AddRange(b);
    }

    private static void Number(List<byte> o, int field, long v)
    {
        Varint(o, field << 3);
        Varint(o, v);
    }

    private static byte[] Otp(byte[] secret, string name, string issuer, int algo, int digits, int type, long counter = 0)
    {
        var o = new List<byte>();
        Bytes(o, 1, secret);
        Bytes(o, 2, Encoding.UTF8.GetBytes(name));
        Bytes(o, 3, Encoding.UTF8.GetBytes(issuer));
        Number(o, 4, algo);
        Number(o, 5, digits);
        Number(o, 6, type);
        if (counter > 0) Number(o, 7, counter);
        return [.. o];
    }

    [Fact]
    public void GoogleAuthenticator_MigrationQr()
    {
        var payload = new List<byte>();
        Bytes(payload, 1, Otp("12345678901234567890"u8.ToArray(), "ana@gmail.com", "Google", algo: 1, digits: 1, type: 2));
        Bytes(payload, 1, Otp([1, 2, 3, 4, 5], "contador", "", algo: 2, digits: 2, type: 1, counter: 300));
        Bytes(payload, 1, Otp([], "sin secreto", "X", 1, 1, 2));
        Bytes(payload, 1, Otp([9, 9, 9], "sha512", "Y", algo: 3, digits: 1, type: 2));
        Number(payload, 2, 1);              // version
        Varint(payload, (9 << 3) | 1);      // un fixed64 que no se usa
        payload.AddRange(new byte[8]);
        Varint(payload, (10 << 3) | 5);     // un fixed32 que no se usa
        payload.AddRange(new byte[4]);
        Bytes(payload, 4, [7]);             // un mensaje que no es de cuentas
        Varint(payload, (11 << 3) | 3);     // tipo de cable desconocido: se para aqui
        Bytes(payload, 1, Otp([8, 8, 8], "no llega", "Z", 1, 1, 2));

        var uri = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String([.. payload]));
        var r = Parse(uri);

        Assert.Equal("Google Authenticator", r.Source);
        Assert.Equal(["Google", "contador", "Y"], r.Entries.Select(e => e.Title));
        var google = Totp.Parse(r.Entries[0].Totp)!;
        Assert.Equal(("SHA1", 6, false), (google.Algorithm, google.Digits, google.IsCounter));
        Assert.Equal("755224", google.Code(0)); // el secreto del RFC 4226 llega intacto
        var counter = Totp.Parse(r.Entries[1].Totp)!;
        Assert.Equal(("SHA256", 8, true, 300L), (counter.Algorithm, counter.Digits, counter.IsCounter, counter.Counter));
        Assert.Equal("SHA512", Totp.Parse(r.Entries[2].Totp)!.Algorithm);
    }

    // ---------------------------------------------------------------- mezcla

    [Fact]
    public void MergeInto_SkipsDuplicates_AddsFolders_RenewsIds()
    {
        var data = new VaultData();
        data.Entries.Add(new Credential { Url = "https://github.com", Username = "Ana", Password = "pw" });
        data.Entries.Add(new Credential { Kind = EntryKind.Totp, Totp = "otpauth://totp/x?secret=JBSWY3DPEHPK3PXP" });
        data.Entries.Add(new Credential { Kind = EntryKind.Note, Title = "Nota", Notes = "texto" });
        data.Entries.Add(new Credential { Url = "https://borrada.com", Username = "a", Password = "p", Deleted = true });

        var importedId = Guid.NewGuid();
        var incoming = new List<Credential>
        {
            new() { Url = "github.com/login", Username = "ana", Password = "pw" },                           // repetida
            new() { Url = "github.com", Username = "ana", Password = "otra" },                               // otra clave: nueva
            new() { Kind = EntryKind.Totp, Totp = "otpauth://totp/y?secret=JBSWY3DPEHPK3PXP====" },          // mismo secreto
            new() { Kind = EntryKind.Note, Title = "Nota", Notes = "texto" },                                // misma nota
            new() { Url = "https://borrada.com", Username = "a", Password = "p", Folder = "Casa" },          // la borrada no cuenta
            new() { Id = importedId, Title = "Duplicada en el propio fichero", Url = "x.com", Username = "u", Password = "p" },
            new() { Title = "Duplicada en el propio fichero", Url = "x.com", Username = "u", Password = "p", Folder = "casa" },
        };

        var (added, skipped) = Importers.MergeInto(data, incoming);

        Assert.Equal((3, 4), (added, skipped));
        Assert.Equal(["Casa"], data.Folders);
        Assert.DoesNotContain(data.Entries, e => e.Id == importedId);
        Assert.Equal(7, data.Entries.Count);
    }
}
