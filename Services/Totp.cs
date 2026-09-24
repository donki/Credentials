using System.Security.Cryptography;
using System.Text;

namespace Credentials.Services;

/// <summary>
/// Codigos de un solo uso por tiempo (TOTP, RFC 6238) y por contador (HOTP, RFC 4226): lo que
/// piden los sitios como «app de autenticacion». Se guarda el URI <c>otpauth://</c> entero, que
/// es el formato de los QR que enseñan los servicios, y de el salen secreto, algoritmo, digitos y
/// periodo.
/// </summary>
public sealed class Totp
{
    public string Secret { get; init; } = string.Empty;   // base32
    public string Issuer { get; init; } = string.Empty;
    public string Account { get; init; } = string.Empty;
    public string Algorithm { get; init; } = "SHA1";
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;
    public bool IsCounter { get; init; }
    public long Counter { get; init; }

    /// <summary>Parsea <c>otpauth://totp/Emisor:cuenta?secret=…&amp;issuer=…</c>. Null si no lo es.</summary>
    public static Totp? Parse(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        uri = uri.Trim();
        // Tambien vale el secreto a pelo (lo que algunos sitios dan «para introducir a mano»).
        if (!uri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            // Los sitios lo enseñan en grupos («ABCD EFGH …», a veces con guiones): fuera separadores.
            var raw = new string(uri.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
            return raw.All(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567=".Contains(c)) && raw.Length >= 8 ? new Totp { Secret = raw } : null;
        }
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return null;
        var isCounter = u.Host.Equals("hotp", StringComparison.OrdinalIgnoreCase);
        var label = Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/'));
        var issuer = string.Empty;
        var account = label;
        var colon = label.IndexOf(':');
        if (colon > 0)
        {
            issuer = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }
        var query = System.Web.HttpUtility.ParseQueryString(u.Query);
        var secret = (query["secret"] ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        if (secret.Length == 0)
            return null;
        if (query["issuer"] is { Length: > 0 } qi)
            issuer = qi;
        return new Totp
        {
            Secret = secret,
            Issuer = issuer,
            Account = account,
            Algorithm = (query["algorithm"] ?? "SHA1").ToUpperInvariant(),
            Digits = int.TryParse(query["digits"], out var d) && d is >= 6 and <= 10 ? d : 6,
            Period = int.TryParse(query["period"], out var p) && p > 0 ? p : 30,
            IsCounter = isCounter,
            Counter = long.TryParse(query["counter"], out var c) ? c : 0,
        };
    }

    /// <summary>El URI otpauth de vuelta (para guardar, exportar o enseñar como QR).</summary>
    public string ToUri()
    {
        var label = Issuer.Length > 0 ? $"{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(Account)}" : Uri.EscapeDataString(Account);
        var q = $"secret={Secret}&algorithm={Algorithm}&digits={Digits}";
        q += IsCounter ? $"&counter={Counter}" : $"&period={Period}";
        if (Issuer.Length > 0)
            q += $"&issuer={Uri.EscapeDataString(Issuer)}";
        return $"otpauth://{(IsCounter ? "hotp" : "totp")}/{label}?{q}";
    }

    /// <summary>El codigo de ahora y los segundos que le quedan.</summary>
    public (string Code, int SecondsLeft) Now()
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = IsCounter ? Counter : unix / Period;
        var left = IsCounter ? 0 : (int)(Period - unix % Period);
        return (Code(counter), left);
    }

    public string Code(long counter)
    {
        var key = Base32Decode(Secret);
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(msg);
        byte[] hash = Algorithm switch
        {
            "SHA256" => HMACSHA256.HashData(key, msg),
            "SHA512" => HMACSHA512.HashData(key, msg),
            _ => HMACSHA1.HashData(key, msg),
        };
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    public static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        var bits = 0;
        var value = 0;
        var output = new List<byte>(input.Length * 5 / 8);
        foreach (var c in input)
        {
            var index = alphabet.IndexOf(c);
            if (index < 0)
                continue;
            value = (value << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. output];
    }

    public static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        var bits = 0;
        var value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(value >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(alphabet[(value << (5 - bits)) & 31]);
        return sb.ToString();
    }
}

/// <summary>Generador de contraseñas: longitud y juegos de caracteres, con aleatoriedad criptografica.</summary>
public static class PasswordGenerator
{
    public static string Generate(int length, bool upper = true, bool lower = true, bool digits = true, bool symbols = true, bool avoidAmbiguous = true)
    {
        var sets = new List<string>();
        if (lower) sets.Add(avoidAmbiguous ? "abcdefghijkmnopqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz");
        if (upper) sets.Add(avoidAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        if (digits) sets.Add(avoidAmbiguous ? "23456789" : "0123456789");
        if (symbols) sets.Add("!@#$%&*+-=?_~");
        if (sets.Count == 0)
            sets.Add("abcdefghijklmnopqrstuvwxyz");
        var all = string.Concat(sets);
        var chars = new char[Math.Max(4, length)];
        // Al menos uno de cada juego elegido, y el resto de la mezcla; luego se baraja.
        for (var i = 0; i < chars.Length; i++)
            chars[i] = i < sets.Count ? sets[i][RandomNumberGenerator.GetInt32(sets[i].Length)] : all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    /// <summary>Fortaleza aproximada, 0..4, por entropia estimada (juegos usados × longitud).</summary>
    public static int Strength(string password)
    {
        if (password.Length == 0) return 0;
        var pool = 0;
        if (password.Any(char.IsLower)) pool += 26;
        if (password.Any(char.IsUpper)) pool += 26;
        if (password.Any(char.IsDigit)) pool += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) pool += 20;
        var bits = password.Length * Math.Log2(Math.Max(pool, 1));
        return bits switch { < 28 => 0, < 40 => 1, < 60 => 2, < 80 => 3, _ => 4 };
    }
}
