using System.Security.Cryptography;

namespace ShortP2P.Bot.Sample.Storage;

internal static class CertificateFingerprint
{
    public static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var chars = new char[hash.Length * 2];
        for (var i = 0; i < hash.Length; i++)
        {
            var value = hash[i];
            chars[i * 2] = Hex(value >> 4);
            chars[i * 2 + 1] = Hex(value & 0xF);
        }

        return new string(chars);
    }

    private static char Hex(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
}
