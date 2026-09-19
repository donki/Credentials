using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace Credentials.Services;

/// <summary>
/// El cifrado de la boveda. Lo unico que sale del aparato (a Google Drive, a OneDrive, a una copia)
/// es lo que produce <see cref="Encrypt"/>; sin la contraseña maestra no hay nada que leer.
/// </summary>
/// <remarks>
/// <para><b>Clave</b>: Argon2id sobre la contraseña maestra (sal aleatoria de 16 bytes, 3 pasadas,
/// 64 MB, 2 hilos). Argon2id es lo que recomiendan OWASP y el RFC 9106 para contraseñas: cuesta
/// memoria, y eso frena a las GPU. Los parametros viajan en la cabecera: se pueden subir mas
/// adelante sin dejar ilegible lo ya cifrado.</para>
/// <para><b>Cifrado</b>: AES-256-GCM (autenticado: un fichero manipulado no descifra) con nonce de
/// 12 bytes por cada escritura. La cabecera (KDF y parametros) va como datos asociados, asi que
/// tampoco se puede cambiar sin que se note.</para>
/// <para><b>Formato</b>: primera linea <c>soccred1</c>, segunda la cabecera JSON, tercera el
/// base64 de <c>nonce(12) | etiqueta(16) | cifrado</c>. Texto plano, para que un editor lo
/// enseñe y una sincronizacion no lo tome por binario raro.</para>
/// <para><b>Clave envuelta</b>: para desbloquear con Windows Hello o la huella no se guarda la
/// contraseña maestra, sino la clave derivada, y no en claro: la guarda la boveda del sistema
/// (DPAPI / Keystore) a traves de <c>SecureStorage</c>. Ver <see cref="VaultStore"/>.</para>
/// </remarks>
public static class VaultCrypto
{
    public const string Magic = "soccred1";

    /// <summary>Parametros de derivacion, en la cabecera del fichero.</summary>
    public sealed class Header
    {
        public string Kdf { get; set; } = "argon2id";
        public string Salt { get; set; } = string.Empty;
        public int Iterations { get; set; } = 3;
        public int MemoryKb { get; set; } = 64 * 1024;
        public int Parallelism { get; set; } = 2;
        public Guid VaultId { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Deriva la clave de 32 bytes con los parametros de la cabecera.</summary>
    public static byte[] DeriveKey(string masterPassword, Header header)
    {
        var salt = Convert.FromBase64String(header.Salt);
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(masterPassword))
        {
            Salt = salt,
            Iterations = header.Iterations,
            MemorySize = header.MemoryKb,
            DegreeOfParallelism = header.Parallelism,
        };
        return argon.GetBytes(32);
    }

    public static Header NewHeader(Guid vaultId) => new()
    {
        Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        VaultId = vaultId,
        ModifiedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Cifra el JSON de la boveda con una clave ya derivada.</summary>
    public static string Encrypt(string plainJson, byte[] key, Header header)
    {
        header.ModifiedAt = DateTimeOffset.UtcNow;
        var headerJson = JsonSerializer.Serialize(header, Json);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plainJson);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(headerJson));
        var all = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(all, 0);
        tag.CopyTo(all, 12);
        cipher.CopyTo(all, 28);
        return Magic + "\n" + headerJson + "\n" + Convert.ToBase64String(all) + "\n";
    }

    /// <summary>Solo la cabecera: para derivar la clave o saber la fecha sin descifrar.</summary>
    public static Header ReadHeader(string stored)
    {
        var lines = stored.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != Magic)
            throw new CryptographicException("El fichero no es una boveda de sOC Credentials.");
        return JsonSerializer.Deserialize<Header>(lines[1], Json) ?? throw new CryptographicException("Cabecera ilegible.");
    }

    /// <summary>Descifra con una clave ya derivada. Lanza <see cref="CryptographicException"/> si no es la clave.</summary>
    public static string Decrypt(string stored, byte[] key)
    {
        var lines = stored.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != Magic)
            throw new CryptographicException("El fichero no es una boveda de sOC Credentials.");
        var all = Convert.FromBase64String(lines[2].Trim());
        var nonce = all[..12];
        var tag = all[12..28];
        var cipher = all[28..];
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(lines[1]));
        return Encoding.UTF8.GetString(plain);
    }

    public static bool IsVault(string text) => text.StartsWith(Magic + "\n", StringComparison.Ordinal) || text.StartsWith(Magic + "\r\n", StringComparison.Ordinal);
}
