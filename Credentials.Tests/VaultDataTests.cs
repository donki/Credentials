using System.Security.Cryptography;
using System.Text.Json;
using Credentials.Models;
using Credentials.Services;

namespace Credentials.Tests;

public class VaultCryptoTests
{
    private static (VaultCrypto.Header Header, byte[] Key) NewKey(string password = "contraseña maestra")
    {
        var header = VaultCrypto.NewHeader(Guid.NewGuid());
        return (header, VaultCrypto.DeriveKey(password, header));
    }

    [Fact]
    public void RoundTrip_AndFormat()
    {
        var (header, key) = NewKey();
        var stored = VaultCrypto.Encrypt("{\"secreto\":\"ñ€\"}", key, header);

        var lines = stored.Split('\n');
        Assert.Equal(VaultCrypto.Magic, lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.True(VaultCrypto.IsVault(stored));
        Assert.DoesNotContain("secreto", stored);
        Assert.Equal("{\"secreto\":\"ñ€\"}", VaultCrypto.Decrypt(stored, key));

        var read = VaultCrypto.ReadHeader(stored);
        Assert.Equal((header.Salt, header.VaultId, "argon2id", 3, 65536, 2), (read.Salt, read.VaultId, read.Kdf, read.Iterations, read.MemoryKb, read.Parallelism));
    }

    [Fact]
    public void DeriveKey_IsDeterministic_AndDependsOnSaltAndPassword()
    {
        var header = VaultCrypto.NewHeader(Guid.NewGuid());
        var a = VaultCrypto.DeriveKey("uno", header);
        Assert.Equal(32, a.Length);
        Assert.Equal(a, VaultCrypto.DeriveKey("uno", header));
        Assert.NotEqual(a, VaultCrypto.DeriveKey("dos", header));
        Assert.NotEqual(a, VaultCrypto.DeriveKey("uno", VaultCrypto.NewHeader(header.VaultId)));
    }

    [Fact]
    public void WrongKey_Fails()
    {
        var (header, key) = NewKey("buena");
        var stored = VaultCrypto.Encrypt("{}", key, header);
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(stored, VaultCrypto.DeriveKey("mala", header)));
    }

    [Fact]
    public void TamperedHeaderOrCiphertext_Fails()
    {
        var (header, key) = NewKey();
        var stored = VaultCrypto.Encrypt("{\"a\":1}", key, header);
        var lines = stored.Split('\n');

        // La cabecera va como datos asociados: bajar las pasadas de Argon2 no puede pasar desapercibido.
        var weaker = lines[1].Replace("\"Iterations\":3", "\"Iterations\":1");
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt($"{lines[0]}\n{weaker}\n{lines[2]}\n", key));

        var bytes = Convert.FromBase64String(lines[2]);
        bytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt($"{lines[0]}\n{lines[1]}\n{Convert.ToBase64String(bytes)}\n", key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("soccred1")]
    [InlineData("otra cosa\n{}\nAAAA\n")]
    public void NotAVault_IsRejected(string text)
    {
        Assert.False(VaultCrypto.IsVault(text) && text.Split('\n').Length >= 3);
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.ReadHeader(text));
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(text, new byte[32]));
    }

    [Fact]
    public void UnreadableHeader_IsRejected() =>
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.ReadHeader("soccred1\nnull\nAAAA\n"));

    /// <summary>
    /// Fallo encontrado por esta prueba (2026-09-30): <see cref="VaultCrypto.IsVault"/> da por buena
    /// una boveda con saltos de linea de Windows (la que deja un editor o una copia que convierte los
    /// finales de linea), pero al descifrarla el «\r» se quedaba pegado a la cabecera, que es parte
    /// de los datos autenticados, y la contraseña correcta daba «contraseña incorrecta».
    /// </summary>
    [Fact]
    public void WindowsLineEndings_StillDecrypt()
    {
        var (header, key) = NewKey();
        var stored = VaultCrypto.Encrypt("{\"a\":1}", key, header).Replace("\n", "\r\n");

        Assert.True(VaultCrypto.IsVault(stored));
        Assert.Equal(header.Salt, VaultCrypto.ReadHeader(stored).Salt);
        Assert.Equal("{\"a\":1}", VaultCrypto.Decrypt(stored, key));
    }
}

public class VaultDataTests
{
    private static Credential Entry(string title, DateTimeOffset modified, Guid? id = null) =>
        new() { Id = id ?? Guid.NewGuid(), Title = title, ModifiedAt = modified };

    [Fact]
    public void Merge_NewestEntryWins_AndNewOnesAreAdded()
    {
        var t0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var shared = Guid.NewGuid();
        var older = Guid.NewGuid();
        var mine = new VaultData { ModifiedAt = t0, Folders = ["Trabajo"] };
        mine.Entries.Add(Entry("mia vieja", t0, shared));
        mine.Entries.Add(Entry("mia nueva", t0.AddDays(2), older));
        mine.Entries.Add(Entry("solo aqui", t0));

        var theirs = new VaultData { ModifiedAt = t0.AddDays(5), Folders = ["trabajo", "Casa"] };
        theirs.Entries.Add(Entry("suya nueva", t0.AddDays(1), shared));
        theirs.Entries.Add(Entry("suya vieja", t0.AddDays(1), older));
        theirs.Entries.Add(Entry("solo alli", t0));

        var changed = mine.Merge(theirs);

        Assert.Equal(3, changed); // la que gana, la nueva y la carpeta «Casa»
        Assert.Equal("suya nueva", mine.Entries.Single(e => e.Id == shared).Title);
        Assert.Equal("mia nueva", mine.Entries.Single(e => e.Id == older).Title);
        Assert.Contains(mine.Entries, e => e.Title == "solo alli");
        Assert.Equal(["Trabajo", "Casa"], mine.Folders);
        Assert.Equal(t0.AddDays(5), mine.ModifiedAt);

        // Mezclar otra vez lo mismo no cambia nada.
        Assert.Equal(0, mine.Merge(theirs));
    }

    [Fact]
    public void Merge_Tombstone_PropagatesTheDeletion()
    {
        var id = Guid.NewGuid();
        var mine = new VaultData();
        mine.Entries.Add(Entry("viva", DateTimeOffset.UtcNow.AddMinutes(-5), id));
        var theirs = new VaultData { ModifiedAt = DateTimeOffset.MinValue };
        theirs.Entries.Add(new Credential { Id = id, Deleted = true, ModifiedAt = DateTimeOffset.UtcNow });

        Assert.Equal(1, mine.Merge(theirs));
        Assert.True(mine.Entries.Single().Deleted);
        Assert.NotEqual(DateTimeOffset.MinValue, mine.ModifiedAt);
    }

    [Fact]
    public void Purge_RemovesOnlyOldTombstones()
    {
        var data = new VaultData();
        data.Entries.Add(new Credential { Title = "vieja", Deleted = true, ModifiedAt = DateTimeOffset.UtcNow.AddDays(-91) });
        data.Entries.Add(new Credential { Title = "reciente", Deleted = true, ModifiedAt = DateTimeOffset.UtcNow.AddDays(-89) });
        data.Entries.Add(new Credential { Title = "viva", ModifiedAt = DateTimeOffset.UtcNow.AddDays(-400) });
        data.Purge();
        Assert.Equal(["reciente", "viva"], data.Entries.Select(e => e.Title));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var e = new Credential { Title = "t", Tags = ["a"], Password = "p" };
        e.Fields.Add(new CustomField { Name = "pin", Value = "1234", Hidden = true });
        e.RecoveryCodes.Add(new RecoveryCode { Code = "r1" });
        e.History.Add(new PasswordHistoryItem("vieja", DateTimeOffset.UtcNow));

        var c = e.Clone();
        c.Tags.Add("b");
        c.Fields[0].Value = "0000";
        c.RecoveryCodes[0].Used = true;
        c.History.Clear();

        Assert.Equal(["a"], e.Tags);
        Assert.Equal("1234", e.Fields[0].Value);
        Assert.True(e.Fields[0].Hidden && c.Fields[0].Hidden);
        Assert.False(e.RecoveryCodes[0].Used);
        Assert.Single(e.History);
        Assert.Equal(e.Id, c.Id);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("github.com/login", "github.com")]
    [InlineData("https://Login.Example.com:8443/x", "login.example.com")]
    [InlineData("http://[::1", "http://[::1")]
    public void Host(string url, string host) => Assert.Equal(host, new Credential { Url = url }.Host);

    [Fact]
    public void Flags_AndJsonShape()
    {
        var e = new Credential { Password = "p", Totp = "otpauth://totp/x?secret=A" };
        Assert.True(e.HasPassword);
        Assert.True(e.HasTotp);
        Assert.False(new Credential().HasPassword);

        var json = JsonSerializer.Serialize(e);
        Assert.DoesNotContain("HasTotp", json);
        Assert.DoesNotContain("\"Host\"", json);

        var data = new VaultData();
        data.Entries.Add(e);
        var back = JsonSerializer.Deserialize<VaultData>(JsonSerializer.Serialize(data))!;
        Assert.Equal(e.Id, back.Entries.Single().Id);
        Assert.Equal(1, back.Version);
    }
}
