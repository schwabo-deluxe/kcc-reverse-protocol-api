using System.Security.Cryptography;
using System.Text;
using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

/// <summary>
/// Sichert die Login-Verschlüsselung gegen die echte Client-Implementierung ab.
/// Die Fixtures stammen aus der Kardex-Krypto-Bibliothek selbst — siehe Fixtures/README.md.
/// </summary>
public class RsaEncryptionTests
{
    const string KnownPassword = "GeheimesPasswort123";

    static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    static RSA LoadTestKey()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(FixturePath("throwaway-test-key.pem")));
        return rsa;
    }

    [Fact]
    public void Entschluesselt_Ciphertext_der_originalen_Clientbibliothek()
    {
        // Der eigentliche Beweis: was der Browser-Client erzeugt, verstehen wir mit OAEP-SHA1 —
        // ohne die Bytes umzudrehen.
        var cipher = File.ReadAllBytes(FixturePath("cipher.bin"));
        using var rsa = LoadTestKey();

        var plain = rsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA1);

        Assert.Equal(KnownPassword, Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public void Umgedrehter_Ciphertext_ist_nicht_entschluesselbar()
    {
        // Gegenprobe zur ursprünglichen Vermutung, .NET liefere die Bytes little-endian.
        var cipher = File.ReadAllBytes(FixturePath("cipher.bin"));
        Array.Reverse(cipher);
        using var rsa = LoadTestKey();

        Assert.ThrowsAny<CryptographicException>(() => rsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA1));
    }

    [Fact]
    public void EncryptPassword_erzeugt_entschluesselbaren_Ciphertext()
    {
        var xml = File.ReadAllText(FixturePath("key.xml"));

        var cipher = KccSession.EncryptPassword(xml, KnownPassword);

        Assert.Equal(256, cipher.Length);
        using var rsa = LoadTestKey();
        var plain = rsa.Decrypt(cipher, RSAEncryptionPadding.OaepSHA1);
        Assert.Equal(KnownPassword, Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public void ParseRsaXmlPublicKey_liest_Modulus_und_Exponent()
    {
        var xml = File.ReadAllText(FixturePath("key.xml"));

        var parameters = KccSession.ParseRsaXmlPublicKey(xml);

        Assert.Equal(256, parameters.Modulus!.Length);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x01 }, parameters.Exponent);
    }

    [Fact]
    public void ParseRsaXmlPublicKey_meldet_unbrauchbares_XML()
    {
        Assert.Throws<KccLogonException>(
            () => KccSession.ParseRsaXmlPublicKey("<RSAKeyValue></RSAKeyValue>"));
    }
}
