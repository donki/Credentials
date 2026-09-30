using System.Text;
using Credentials.Services;

namespace Credentials.Tests;

public class TotpTests
{
    // Secretos de los vectores de prueba del RFC 6238 (apendice B) y del RFC 4226 (apendice D).
    private static readonly string Sha1Secret = Totp.Base32Encode(Encoding.ASCII.GetBytes("12345678901234567890"));
    private static readonly string Sha256Secret = Totp.Base32Encode(Encoding.ASCII.GetBytes("12345678901234567890123456789012"));
    private static readonly string Sha512Secret = Totp.Base32Encode(Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234"));

    [Theory]
    [InlineData("SHA1", 59L, "94287082")]
    [InlineData("SHA256", 59L, "46119246")]
    [InlineData("SHA512", 59L, "90693936")]
    [InlineData("SHA1", 1111111109L, "07081804")]
    [InlineData("SHA256", 1111111109L, "68084774")]
    [InlineData("SHA512", 1111111109L, "25091201")]
    [InlineData("SHA1", 1234567890L, "89005924")]
    [InlineData("SHA256", 2000000000L, "90698825")]
    [InlineData("SHA512", 20000000000L, "47863826")]
    public void Rfc6238_Vectors(string algorithm, long unixTime, string expected)
    {
        var secret = algorithm switch { "SHA256" => Sha256Secret, "SHA512" => Sha512Secret, _ => Sha1Secret };
        var totp = new Totp { Secret = secret, Algorithm = algorithm, Digits = 8, Period = 30 };
        Assert.Equal(expected, totp.Code(unixTime / 30));
    }

    [Theory]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Rfc4226_HotpVectors(long counter, string expected) =>
        Assert.Equal(expected, new Totp { Secret = Sha1Secret, IsCounter = true, Counter = counter }.Code(counter));

    [Fact]
    public void TenDigits_PadWithZeros()
    {
        var code = new Totp { Secret = Sha1Secret, Digits = 10 }.Code(1);
        Assert.Equal(10, code.Length);
        Assert.All(code, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Now_TotpCountsDown_HotpUsesCounter()
    {
        var (code, left) = new Totp { Secret = Sha1Secret }.Now();
        Assert.Equal(6, code.Length);
        Assert.InRange(left, 1, 30);

        var hotp = new Totp { Secret = Sha1Secret, IsCounter = true, Counter = 3 }.Now();
        Assert.Equal(("969429", 0), hotp);
    }

    [Fact]
    public void Parse_FullUri()
    {
        var t = Totp.Parse("  otpauth://totp/GitHub:ana%40example.com?secret=jbsw y3dp&issuer=GitHub%20Inc&algorithm=sha256&digits=8&period=60  ")!;
        Assert.Equal("JBSWY3DP", t.Secret);
        Assert.Equal("GitHub Inc", t.Issuer);
        Assert.Equal("ana@example.com", t.Account);
        Assert.Equal("SHA256", t.Algorithm);
        Assert.Equal(8, t.Digits);
        Assert.Equal(60, t.Period);
        Assert.False(t.IsCounter);
    }

    [Fact]
    public void Parse_Hotp_AndDefaults()
    {
        var t = Totp.Parse("otpauth://hotp/solo-cuenta?secret=JBSWY3DP&counter=42&digits=3&period=-1")!;
        Assert.True(t.IsCounter);
        Assert.Equal(42, t.Counter);
        Assert.Equal(6, t.Digits);   // fuera de 6..10 se queda en 6
        Assert.Equal(30, t.Period);
        Assert.Equal("SHA1", t.Algorithm);
        Assert.Equal(string.Empty, t.Issuer);
        Assert.Equal("solo-cuenta", t.Account);
    }

    [Theory]
    [InlineData("jbsw-y3dp ehpk 3pxp", "JBSWY3DPEHPK3PXP")]
    [InlineData("JBSWY3DP====", "JBSWY3DP====")]
    public void Parse_RawSecret(string input, string secret) => Assert.Equal(secret, Totp.Parse(input)!.Secret);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("NOT-BASE32-1890!")]
    [InlineData("otpauth://totp/x?issuer=sin-secreto")]
    [InlineData("otpauth://totp x y")]
    public void Parse_Invalid_IsNull(string? input) => Assert.Null(Totp.Parse(input!));

    [Fact]
    public void ToUri_RoundTrips()
    {
        var original = new Totp { Secret = "JBSWY3DP", Issuer = "Mi Banco", Account = "ana:1", Algorithm = "SHA512", Digits = 8, Period = 45 };
        var back = Totp.Parse(original.ToUri())!;
        Assert.Equal((original.Secret, original.Issuer, original.Algorithm, original.Digits, original.Period), (back.Secret, back.Issuer, back.Algorithm, back.Digits, back.Period));

        var hotp = new Totp { Secret = "JBSWY3DP", Account = "sin emisor", IsCounter = true, Counter = 7 };
        var uri = hotp.ToUri();
        Assert.StartsWith("otpauth://hotp/sin%20emisor?", uri);
        Assert.Contains("counter=7", uri);
        Assert.DoesNotContain("issuer", uri);
        Assert.Equal(7, Totp.Parse(uri)!.Counter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("f")]
    [InlineData("fo")]
    [InlineData("foo")]
    [InlineData("foob")]
    [InlineData("fooba")]
    [InlineData("foobar")]
    public void Base32_Rfc4648_RoundTrip(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var encoded = Totp.Base32Encode(bytes);
        Assert.Equal(bytes, Totp.Base32Decode(encoded));
        Assert.Equal(bytes, Totp.Base32Decode(encoded.ToLowerInvariant() + "=="));
    }

    [Fact]
    public void Base32_KnownValue_AndIgnoresForeignCharacters()
    {
        Assert.Equal("MZXW6YTBOI", Totp.Base32Encode("foobar"u8.ToArray()));
        Assert.Equal("foobar"u8.ToArray(), Totp.Base32Decode("MZXW 6YTB-OI"));
    }
}

public class PasswordGeneratorTests
{
    [Fact]
    public void Generate_RespectsLengthAndSets()
    {
        for (var i = 0; i < 50; i++)
        {
            var p = PasswordGenerator.Generate(20);
            Assert.Equal(20, p.Length);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => "!@#$%&*+-=?_~".Contains(c));
            Assert.DoesNotContain(p, c => "lIO01".Contains(c));
        }
    }

    [Fact]
    public void Generate_OnlyChosenSets_AndMinimumFour()
    {
        var digits = PasswordGenerator.Generate(30, upper: false, lower: false, digits: true, symbols: false, avoidAmbiguous: false);
        Assert.All(digits, c => Assert.True(char.IsDigit(c)));

        Assert.Equal(4, PasswordGenerator.Generate(1).Length);

        var none = PasswordGenerator.Generate(12, false, false, false, false);
        Assert.All(none, c => Assert.True(char.IsLower(c)));
    }

    [Fact]
    public void Generate_IsNotRepetitive() =>
        Assert.Equal(100, Enumerable.Range(0, 100).Select(_ => PasswordGenerator.Generate(16)).Distinct().Count());

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("abcdefgh", 1)]
    [InlineData("abcdefghi1", 2)]
    [InlineData("Abcdefgh1!x", 3)]
    [InlineData("Abcdefgh1234!xyz%&QW", 4)]
    public void Strength(string password, int expected) => Assert.Equal(expected, PasswordGenerator.Strength(password));
}
