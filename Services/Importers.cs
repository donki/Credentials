using System.Text;
using System.Text.Json;
using Credentials.Models;

namespace Credentials.Services;

/// <summary>
/// Importadores: lo que exportan los navegadores y otros gestores. Se reconoce el formato por el
/// contenido, no por la extension. Nada de esto sale del aparato: se lee el fichero, se convierten
/// las filas a entradas y se mezclan con la boveda (las repetidas —misma URL y usuario, o mismo
/// TOTP— se saltan).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Chrome, Edge, Brave, Opera, Vivaldi</b> (CSV): <c>name,url,username,password,note</c>.
/// Se exporta en «Contraseñas › ⋮ › Exportar contraseñas».</item>
/// <item><b>Firefox</b> (CSV): <c>url,username,password,httpRealm,formActionOrigin,guid,timeCreated,timeLastUsed,timePasswordChanged</c>.
/// «Contraseñas › ⋯ › Exportar inicios de sesion».</item>
/// <item><b>Safari</b> (CSV): <c>Title,URL,Username,Password,Notes,OTPAuth</c>.</item>
/// <item><b>Bitwarden</b> (CSV o JSON sin cifrar), <b>KeePass 2</b> (CSV) y <b>KeePassXC</b> (CSV).</item>
/// <item><b>Aegis</b> (JSON sin cifrar), <b>2FAS</b> (JSON de copia) y <b>Google Authenticator</b>
/// (el QR de «Transferir cuentas», <c>otpauth-migration://</c>).</item>
/// <item><b>sOC Credentials</b> (JSON en claro exportado desde Ajustes).</item>
/// </list>
/// </remarks>
public static class Importers
{
    public sealed record Result(List<Credential> Entries, string Source);

    /// <summary>Reconoce y convierte. Devuelve null si no es ningun formato conocido.</summary>
    public static Result? Parse(string content)
    {
        content = content.TrimStart('﻿', ' ', '\r', '\n');
        if (content.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase))
            return new Result(GoogleAuthenticator(content), "Google Authenticator");
        if (content.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            var t = Totp.Parse(content.Split('\n')[0].Trim());
            return t is null ? null : new Result([FromTotp(t)], "otpauth");
        }
        if (content.StartsWith('{') || content.StartsWith('['))
            return Json(content);
        return Csv(content);
    }

    // ------------------------------------------------------------------ CSV

    private static Result? Csv(string content)
    {
        var rows = ParseCsv(content);
        if (rows.Count < 2)
            return null;
        var header = rows[0].Select(h => h.Trim().Trim('"').ToLowerInvariant()).ToList();
        int Col(params string[] names) => names.Select(n => header.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
        string Get(List<string> row, int i) => i >= 0 && i < row.Count ? row[i].Trim() : string.Empty;

        // Chrome/Edge/Brave: name,url,username,password,note · Firefox: url,username,password,... ·
        // Safari: Title,URL,Username,Password,Notes,OTPAuth · KeePass 2: "Account","Login Name","Password","Web Site","Comments" ·
        // KeePassXC: Group,Title,Username,Password,URL,Notes,TOTP,... · Bitwarden: folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp
        var title = Col("name", "title", "account");
        var url = Col("url", "login_uri", "web site", "website");
        var user = Col("username", "login_username", "login name", "user name", "login");
        var pass = Col("password", "login_password");
        var notes = Col("note", "notes", "comments");
        var totp = Col("otpauth", "totp", "login_totp");
        var folder = Col("folder", "group");
        var fav = Col("favorite");
        var created = Col("timecreated", "creation time");
        var modified = Col("timepasswordchanged", "last modification time");
        if (pass < 0 || (title < 0 && url < 0))
            return null;

        var source = header.Contains("login_uri") ? "Bitwarden" : header.Contains("httprealm") ? "Firefox" : header.Contains("otpauth") ? "Safari"
            : header.Contains("web site") ? "KeePass" : header.Contains("group") && header.Contains("totp") ? "KeePassXC" : "CSV";
        var list = new List<Credential>();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(c => c.Trim().Length == 0))
                continue;
            var u = Get(row, url);
            var name = Get(row, title);
            if (name.Length == 0)
                name = HostOf(u);
            if (name.Length == 0 && Get(row, user).Length == 0)
                continue;
            var e = new Credential { Kind = EntryKind.Login,
                Title = name,
                Url = u,
                Username = Get(row, user),
                Password = Get(row, pass),
                Notes = Get(row, notes),
                Folder = Get(row, folder).Replace('\\', '/').Trim('/'),
                Favorite = Get(row, fav) is "1" or "true" or "TRUE",
            };
            if (source == "Bitwarden" && Get(row, Col("type")) == "note")
                e.Kind = EntryKind.Note;
            var t = Totp.Parse(Get(row, totp));
            if (t is not null)
                e.Totp = t.ToUri();
            if (long.TryParse(Get(row, created), out var ms) && ms > 0)
                e.CreatedAt = ms > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.FromUnixTimeSeconds(ms);
            if (long.TryParse(Get(row, modified), out var mm) && mm > 0)
                e.ModifiedAt = mm > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(mm) : DateTimeOffset.FromUnixTimeSeconds(mm);
            if (e.Url.Length == 0 && e.Password.Length == 0 && e.Notes.Length > 0)
                e.Kind = EntryKind.Note;
            list.Add(e);
        }
        return list.Count == 0 ? null : new Result(list, source);
    }

    /// <summary>CSV RFC 4180: comillas, comas dentro de comillas y saltos de linea dentro de comillas.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var separator = DetectSeparator(text);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = []; }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    private static char DetectSeparator(string text)
    {
        var first = text.Split('\n')[0];
        return first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : first.Count(c => c == '\t') > first.Count(c => c == ',') ? '\t' : ',';
    }

    // ------------------------------------------------------------------ JSON

    private static Result? Json(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        // sOC Credentials (JSON en claro)
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Entries", out _) && root.TryGetProperty("VaultId", out _))
        {
            var data = JsonSerializer.Deserialize<VaultData>(content);
            return data is null ? null : new Result(data.Entries.Where(e => !e.Deleted).ToList(), "sOC Credentials");
        }
        // Aegis: { "db": { "entries": [ { "type":"totp","name":"…","issuer":"…","info":{"secret":"…","algo":"SHA1","digits":6,"period":30} } ] } }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("db", out var db) && db.ValueKind == JsonValueKind.Object && db.TryGetProperty("entries", out var aegis))
        {
            var list = new List<Credential>();
            foreach (var e in aegis.EnumerateArray())
            {
                var info = e.GetProperty("info");
                var t = new Totp
                {
                    Secret = info.GetProperty("secret").GetString() ?? string.Empty,
                    Issuer = e.TryGetProperty("issuer", out var iss) ? iss.GetString() ?? string.Empty : string.Empty,
                    Account = e.TryGetProperty("name", out var nm) ? nm.GetString() ?? string.Empty : string.Empty,
                    Algorithm = info.TryGetProperty("algo", out var al) ? (al.GetString() ?? "SHA1").ToUpperInvariant() : "SHA1",
                    Digits = info.TryGetProperty("digits", out var dg) ? dg.GetInt32() : 6,
                    Period = info.TryGetProperty("period", out var pd) ? pd.GetInt32() : 30,
                    IsCounter = e.TryGetProperty("type", out var ty) && ty.GetString() == "hotp",
                    Counter = info.TryGetProperty("counter", out var ct) ? ct.GetInt64() : 0,
                };
                if (t.Secret.Length > 0)
                    list.Add(FromTotp(t, e.TryGetProperty("note", out var note) ? note.GetString() ?? string.Empty : string.Empty));
            }
            return new Result(list, "Aegis");
        }
        // 2FAS: { "services": [ { "name":"…","secret":"…","otp":{"account":"…","issuer":"…","digits":6,"period":30,"algorithm":"SHA1","tokenType":"TOTP"} } ] }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("services", out var services) && services.ValueKind == JsonValueKind.Array)
        {
            var list = new List<Credential>();
            foreach (var s in services.EnumerateArray())
            {
                var otp = s.TryGetProperty("otp", out var o) ? o : default;
                string Str(JsonElement el, string name) => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                int Int(JsonElement el, string name, int d) => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
                var t = new Totp
                {
                    Secret = Str(s, "secret"),
                    Issuer = Str(otp, "issuer").Length > 0 ? Str(otp, "issuer") : Str(s, "name"),
                    Account = Str(otp, "account"),
                    Algorithm = Str(otp, "algorithm").Length > 0 ? Str(otp, "algorithm").ToUpperInvariant() : "SHA1",
                    Digits = Int(otp, "digits", 6),
                    Period = Int(otp, "period", 30),
                    IsCounter = Str(otp, "tokenType").Equals("HOTP", StringComparison.OrdinalIgnoreCase),
                    Counter = Int(otp, "counter", 0),
                };
                if (t.Secret.Length > 0)
                    list.Add(FromTotp(t));
            }
            return new Result(list, "2FAS");
        }
        // Bitwarden JSON: { "items": [ { "type":1, "name", "notes", "login": { "username","password","totp","uris":[{"uri"}] }, "favorite" } ], "folders":[{"id","name"}] }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var folders = new Dictionary<string, string>();
            if (root.TryGetProperty("folders", out var fs))
                foreach (var f in fs.EnumerateArray())
                    folders[f.GetProperty("id").GetString() ?? string.Empty] = f.GetProperty("name").GetString() ?? string.Empty;
            var list = new List<Credential>();
            foreach (var it in items.EnumerateArray())
            {
                var e = new Credential { Title = it.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Notes = it.TryGetProperty("notes", out var no) && no.ValueKind == JsonValueKind.String ? no.GetString() ?? string.Empty : string.Empty,
                    Favorite = it.TryGetProperty("favorite", out var fv) && fv.ValueKind == JsonValueKind.True,
                };
                if (it.TryGetProperty("folderId", out var fid) && fid.ValueKind == JsonValueKind.String && folders.TryGetValue(fid.GetString()!, out var fname))
                    e.Folder = fname;
                if (it.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.Object)
                {
                    e.Kind = EntryKind.Login;
                    e.Username = login.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? string.Empty : string.Empty;
                    e.Password = login.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
                    if (login.TryGetProperty("uris", out var uris) && uris.ValueKind == JsonValueKind.Array)
                        e.Url = uris.EnumerateArray().Select(x => x.TryGetProperty("uri", out var uu) ? uu.GetString() ?? string.Empty : string.Empty).FirstOrDefault(x => x.Length > 0) ?? string.Empty;
                    if (login.TryGetProperty("totp", out var tp) && tp.ValueKind == JsonValueKind.String && Totp.Parse(tp.GetString() ?? string.Empty) is { } t)
                        e.Totp = t.ToUri();
                }
                else
                    e.Kind = EntryKind.Note;
                if (it.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
                    foreach (var f in fields.EnumerateArray())
                        e.Fields.Add(new CustomField { Name = f.TryGetProperty("name", out var fn) ? fn.GetString() ?? string.Empty : string.Empty, Value = f.TryGetProperty("value", out var fvv) && fvv.ValueKind == JsonValueKind.String ? fvv.GetString() ?? string.Empty : string.Empty, Hidden = f.TryGetProperty("type", out var ft) && ft.ValueKind == JsonValueKind.Number && ft.GetInt32() == 1 });
                if (e.Title.Length > 0)
                    list.Add(e);
            }
            return new Result(list, "Bitwarden");
        }
        return null;
    }

    // ------------------------------------------------------------------ Google Authenticator

    /// <summary>
    /// <c>otpauth-migration://offline?data=base64(protobuf)</c>: el mensaje lleva una lista de
    /// cuentas (secreto en bytes, nombre, emisor, algoritmo, digitos, tipo, contador). Se decodifica
    /// el protobuf a mano: son cuatro tipos de campo y no hace falta una biblioteca.
    /// </summary>
    private static List<Credential> GoogleAuthenticator(string uri)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(uri).Query);
        var data = Convert.FromBase64String(query["data"] ?? string.Empty);
        var list = new List<Credential>();
        foreach (var (field, wire, value, bytes) in Fields(data))
        {
            if (field != 1 || wire != 2 || bytes is null)
                continue;   // solo los otp_parameters (repeated message #1)
            var secret = Array.Empty<byte>();
            string name = string.Empty, issuer = string.Empty;
            long algo = 1, digits = 1, type = 2, counter = 0;
            foreach (var (f, w, v, b) in Fields(bytes))
            {
                switch (f)
                {
                    case 1: secret = b ?? []; break;
                    case 2: name = Encoding.UTF8.GetString(b ?? []); break;
                    case 3: issuer = Encoding.UTF8.GetString(b ?? []); break;
                    case 4: algo = v; break;
                    case 5: digits = v; break;
                    case 6: type = v; break;
                    case 7: counter = v; break;
                }
            }
            if (secret.Length == 0)
                continue;
            var t = new Totp
            {
                Secret = Totp.Base32Encode(secret),
                Issuer = issuer,
                Account = name,
                Algorithm = algo switch { 2 => "SHA256", 3 => "SHA512", _ => "SHA1" },
                Digits = digits == 2 ? 8 : 6,
                Period = 30,
                IsCounter = type == 1,
                Counter = counter,
            };
            list.Add(FromTotp(t));
        }
        return list;
    }

    /// <summary>Campos de un mensaje protobuf: (numero, tipo de cable, valor varint, bytes si es de longitud).</summary>
    private static IEnumerable<(int Field, int Wire, long Value, byte[]? Bytes)> Fields(byte[] data)
    {
        var i = 0;
        while (i < data.Length)
        {
            var key = Varint(data, ref i);
            var field = (int)(key >> 3);
            var wire = (int)(key & 7);
            switch (wire)
            {
                case 0:
                    yield return (field, wire, Varint(data, ref i), null);
                    break;
                case 2:
                    var len = (int)Varint(data, ref i);
                    var bytes = data.Skip(i).Take(len).ToArray();
                    i += len;
                    yield return (field, wire, 0, bytes);
                    break;
                case 1: i += 8; break;
                case 5: i += 4; break;
                default: yield break;
            }
        }
    }

    private static long Varint(byte[] data, ref int i)
    {
        long result = 0;
        var shift = 0;
        while (i < data.Length)
        {
            var b = data[i++];
            result |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
        }
        return result;
    }

    // ------------------------------------------------------------------ utilidades

    private static Credential FromTotp(Totp t, string notes = "") => new()
    {
        Kind = EntryKind.Totp,
        Title = t.Issuer.Length > 0 ? t.Issuer : t.Account.Length > 0 ? t.Account : "TOTP",
        Username = t.Account,
        Totp = t.ToUri(),
        Notes = notes,
    };

    private static string HostOf(string url)
    {
        if (url.Length == 0) return string.Empty;
        var text = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        return Uri.TryCreate(text, UriKind.Absolute, out var u) ? u.Host : url;
    }

    /// <summary>Mezcla lo importado con la boveda saltando repetidas. Devuelve (añadidas, saltadas).</summary>
    public static (int Added, int Skipped) MergeInto(VaultData data, IEnumerable<Credential> entries)
    {
        var added = 0;
        var skipped = 0;
        var existing = data.Entries.Where(e => !e.Deleted).ToList();
        foreach (var e in entries)
        {
            var dup = existing.Any(x =>
                (e.Totp.Length > 0 && x.Totp.Length > 0 && SameSecret(e.Totp, x.Totp)) ||
                (e.Totp.Length == 0 && string.Equals(x.Host, e.Host, StringComparison.OrdinalIgnoreCase) && x.Host.Length > 0
                    && string.Equals(x.Username, e.Username, StringComparison.OrdinalIgnoreCase) && x.Password == e.Password) ||
                (e.Kind == EntryKind.Note && x.Kind == EntryKind.Note && x.Title == e.Title && x.Notes == e.Notes));
            if (dup) { skipped++; continue; }
            e.Id = Guid.NewGuid();
            e.ModifiedAt = DateTimeOffset.UtcNow;
            data.Entries.Add(e);
            existing.Add(e);
            if (e.Folder.Length > 0 && !data.Folders.Contains(e.Folder, StringComparer.OrdinalIgnoreCase))
                data.Folders.Add(e.Folder);
            added++;
        }
        return (added, skipped);
    }

    private static bool SameSecret(string a, string b) => Totp.Parse(a)?.Secret.TrimEnd('=') == Totp.Parse(b)?.Secret.TrimEnd('=');
}
