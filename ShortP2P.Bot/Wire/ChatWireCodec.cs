using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShortP2P.Bot.Wire;

/// <summary>
/// ChatWire <c>S2P1</c> frames and the hybrid RSA+AES envelope.
/// Text is 0x01, image is 0x02, file is 0x03, transfer offer is 0x04.
/// Available on .NET 8 builds of this library. The cipher type is not part of netstandard2.1.
/// </summary>
public static class ChatWireCodec
{
    private const byte EnvelopeVersion = 1;
    private const int AesKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static ReadOnlySpan<byte> WireMagic => "S2P1"u8;
    public const byte KindText = 0x01;
    public const byte KindImage = 0x02;
    public const byte KindFile = 0x03;
    public const byte KindTransferOffer = 0x04;

    public static (string PublicJson, string PrivateJson) CreateRsaKeyPairJson()
    {
        using var rsa = RSA.Create(2048);
        var pub = rsa.ExportParameters(false);
        var priv = rsa.ExportParameters(true);
        var publicJson = JsonSerializer.Serialize(new PublicDto
        {
            M = Convert.ToBase64String(pub.Modulus!),
            E = Convert.ToBase64String(pub.Exponent!)
        });
        var privateJson = JsonSerializer.Serialize(new PrivateDto
        {
            M = Convert.ToBase64String(priv.Modulus!),
            E = Convert.ToBase64String(priv.Exponent!),
            D = Convert.ToBase64String(priv.D!),
            P = Convert.ToBase64String(priv.P!),
            Q = Convert.ToBase64String(priv.Q!),
            DP = Convert.ToBase64String(priv.DP!),
            DQ = Convert.ToBase64String(priv.DQ!),
            IQ = Convert.ToBase64String(priv.InverseQ!)
        });
        return (publicJson, privateJson);
    }

    public readonly record struct Frame(
        byte Kind,
        string? Text,
        string? MimeType,
        string? FileName,
        byte[]? Bytes,
        ChatWireTransferOffer? Offer);

    public static string EncryptTextToEnvelopeBase64(string text, string recipientPublicKeyJson)
    {
        var wire = EncodeText(text);
        return Convert.ToBase64String(EncryptEnvelope(wire, recipientPublicKeyJson));
    }

    public static byte[] EncodeText(string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var buf = new byte[WireMagic.Length + 1 + 4 + utf8.Length];
        WireMagic.CopyTo(buf);
        buf[WireMagic.Length] = KindText;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(WireMagic.Length + 1, 4), (uint)utf8.Length);
        utf8.CopyTo(buf.AsSpan(WireMagic.Length + 1 + 4));
        return buf;
    }

    public static byte[] EncodeImage(string mimeType, ReadOnlySpan<byte> imageBytes)
    {
        var mime = Encoding.UTF8.GetBytes(mimeType.Trim());
        if (mime.Length > ushort.MaxValue)
            throw new ArgumentException("MIME type is too long.", nameof(mimeType));

        var buf = new byte[WireMagic.Length + 1 + 2 + mime.Length + 4 + imageBytes.Length];
        WireMagic.CopyTo(buf);
        buf[WireMagic.Length] = KindImage;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(WireMagic.Length + 1, 2), (ushort)mime.Length);
        mime.CopyTo(buf.AsSpan(WireMagic.Length + 1 + 2));
        var offset = WireMagic.Length + 1 + 2 + mime.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), (uint)imageBytes.Length);
        imageBytes.CopyTo(buf.AsSpan(offset + 4));
        return buf;
    }

    public static byte[] EncodeFile(string fileName, string mimeType, ReadOnlySpan<byte> fileBytes)
    {
        var mime = Encoding.UTF8.GetBytes(mimeType.Trim());
        if (mime.Length > ushort.MaxValue)
            throw new ArgumentException("MIME type is too long.", nameof(mimeType));

        var name = Encoding.UTF8.GetBytes(NormalizeFileName(fileName));
        if (name.Length > ushort.MaxValue)
            throw new ArgumentException("File name is too long.", nameof(fileName));

        var buf = new byte[WireMagic.Length + 1 + 2 + mime.Length + 2 + name.Length + 4 + fileBytes.Length];
        WireMagic.CopyTo(buf);
        buf[WireMagic.Length] = KindFile;
        var offset = WireMagic.Length + 1;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), (ushort)mime.Length);
        offset += 2;
        mime.CopyTo(buf.AsSpan(offset));
        offset += mime.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset, 2), (ushort)name.Length);
        offset += 2;
        name.CopyTo(buf.AsSpan(offset));
        offset += name.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(offset, 4), (uint)fileBytes.Length);
        offset += 4;
        fileBytes.CopyTo(buf.AsSpan(offset));
        return buf;
    }

    public static byte[] EncodeTransferOffer(ChatWireTransferOffer offer)
    {
        if (offer is null)
            throw new ArgumentNullException(nameof(offer));

        var json = JsonSerializer.SerializeToUtf8Bytes(offer);
        var buf = new byte[WireMagic.Length + 1 + 4 + json.Length];
        WireMagic.CopyTo(buf);
        buf[WireMagic.Length] = KindTransferOffer;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(WireMagic.Length + 1, 4), (uint)json.Length);
        json.CopyTo(buf.AsSpan(WireMagic.Length + 1 + 4));
        return buf;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static byte[] EncryptEnvelope(ReadOnlySpan<byte> plaintext, string recipientPublicKeyJson)
    {
        var pub = JsonSerializer.Deserialize<PublicDto>(recipientPublicKeyJson)
                  ?? throw new FormatException("Invalid public key JSON.");

        var aesKey = RandomNumberGenerator.GetBytes(AesKeyBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(aesKey, TagBytes))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var keyMaterial = new byte[AesKeyBytes + NonceBytes];
        Buffer.BlockCopy(aesKey, 0, keyMaterial, 0, AesKeyBytes);
        Buffer.BlockCopy(nonce, 0, keyMaterial, AesKeyBytes, NonceBytes);

        byte[] wrappedKey;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(pub.M),
                Exponent = Convert.FromBase64String(pub.E)
            });
            wrappedKey = rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256);
        }

        CryptographicOperations.ZeroMemory(aesKey);
        CryptographicOperations.ZeroMemory(keyMaterial);

        var result = new byte[1 + 2 + wrappedKey.Length + ciphertext.Length + TagBytes];
        result[0] = EnvelopeVersion;
        result[1] = (byte)(wrappedKey.Length >> 8);
        result[2] = (byte)wrappedKey.Length;
        Buffer.BlockCopy(wrappedKey, 0, result, 3, wrappedKey.Length);
        var bodyOffset = 3 + wrappedKey.Length;
        Buffer.BlockCopy(ciphertext, 0, result, bodyOffset, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, bodyOffset + ciphertext.Length, TagBytes);
        return result;
    }

    public static byte[] DecryptEnvelope(ReadOnlySpan<byte> blob, string privateKeyJson)
    {
        if (blob.Length < 1 + 2 + 1 + TagBytes)
            throw new CryptographicException("Server payload is too short.");
        if (blob[0] != EnvelopeVersion)
            throw new CryptographicException($"Unsupported server payload version: {blob[0]}.");

        var keyLen = (blob[1] << 8) | blob[2];
        if (keyLen <= 0 || 3 + keyLen + TagBytes > blob.Length)
            throw new CryptographicException("Invalid server payload key length.");

        var wrappedKey = blob.Slice(3, keyLen).ToArray();
        var cipherAndTag = blob.Slice(3 + keyLen);
        var cipherLen = cipherAndTag.Length - TagBytes;
        if (cipherLen < 0)
            throw new CryptographicException("Invalid server payload ciphertext.");

        var ciphertext = cipherAndTag[..cipherLen].ToArray();
        var tag = cipherAndTag[cipherLen..].ToArray();

        var dto = JsonSerializer.Deserialize<PrivateDto>(privateKeyJson)
                  ?? throw new FormatException("Invalid private key JSON.");
        byte[] keyMaterial;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(dto.M),
                Exponent = Convert.FromBase64String(dto.E),
                D = Convert.FromBase64String(dto.D),
                P = Convert.FromBase64String(dto.P),
                Q = Convert.FromBase64String(dto.Q),
                DP = Convert.FromBase64String(dto.DP),
                DQ = Convert.FromBase64String(dto.DQ),
                InverseQ = Convert.FromBase64String(dto.IQ)
            });
            keyMaterial = rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
        }

        if (keyMaterial.Length != AesKeyBytes + NonceBytes)
            throw new CryptographicException("Invalid unwrapped key material.");

        var aesKey = keyMaterial.AsSpan(0, AesKeyBytes).ToArray();
        var nonce = keyMaterial.AsSpan(AesKeyBytes, NonceBytes);
        var plaintext = new byte[cipherLen];
        using (var aes = new AesGcm(aesKey, TagBytes))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        CryptographicOperations.ZeroMemory(keyMaterial);
        CryptographicOperations.ZeroMemory(aesKey);
        return plaintext;
    }

    public static string DecryptEnvelopeToText(string encryptedMessageBase64, string privateKeyJson)
    {
        var wire = DecryptEnvelope(Convert.FromBase64String(encryptedMessageBase64.Trim()), privateKeyJson);
        return TryDecodeText(wire) ?? Encoding.UTF8.GetString(wire);
    }

    public static string? TryDecodeText(ReadOnlySpan<byte> payload)
    {
        if (!TryDecode(payload, out var frame) || frame.Kind != KindText)
            return null;
        return frame.Text;
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out Frame frame)
    {
        frame = default;
        if (payload.Length < WireMagic.Length + 1 || !payload.StartsWith(WireMagic))
            return false;

        var kind = payload[WireMagic.Length];
        var rest = payload[(WireMagic.Length + 1)..];
        switch (kind)
        {
            case KindText:
                if (!TryReadBlock(rest, out var textBytes))
                    return false;
                frame = new Frame(kind, Encoding.UTF8.GetString(textBytes), null, null, null, null);
                return true;
            case KindTransferOffer:
                if (!TryReadBlock(rest, out var jsonBytes))
                    return false;
                var offer = JsonSerializer.Deserialize<ChatWireTransferOffer>(jsonBytes);
                if (offer is null)
                    return false;
                frame = new Frame(kind, null, offer.MimeType, offer.FileName, null, offer);
                return true;
            case KindImage:
                return TryDecodeImage(rest, out frame);
            case KindFile:
                return TryDecodeFile(rest, out frame);
            default:
                return false;
        }
    }

    public static bool IsSound(string? mimeType, string? payloadKind)
    {
        if (!string.IsNullOrWhiteSpace(mimeType)
            && mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(payloadKind, "audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodeImage(ReadOnlySpan<byte> rest, out Frame frame)
    {
        frame = default;
        if (!TryReadCounted(rest, out var mime, out var afterMime))
            return false;
        if (!TryReadBlock(afterMime, out var bytes))
            return false;

        frame = new Frame(KindImage, null, Encoding.UTF8.GetString(mime), null, bytes, null);
        return true;
    }

    private static bool TryDecodeFile(ReadOnlySpan<byte> rest, out Frame frame)
    {
        frame = default;
        if (!TryReadCounted(rest, out var mime, out var afterMime))
            return false;
        if (!TryReadCounted(afterMime, out var name, out var afterName))
            return false;
        if (!TryReadBlock(afterName, out var bytes))
            return false;

        frame = new Frame(
            KindFile,
            null,
            Encoding.UTF8.GetString(mime),
            Encoding.UTF8.GetString(name),
            bytes,
            null);
        return true;
    }

    private static bool TryReadBlock(ReadOnlySpan<byte> rest, out byte[] bytes)
    {
        bytes = [];
        if (rest.Length < 4)
            return false;
        var len = BinaryPrimitives.ReadUInt32LittleEndian(rest[..4]);
        if (len > (uint)(rest.Length - 4))
            return false;
        bytes = rest.Slice(4, (int)len).ToArray();
        return true;
    }

    private static bool TryReadCounted(ReadOnlySpan<byte> rest, out byte[] bytes, out ReadOnlySpan<byte> after)
    {
        bytes = [];
        after = default;
        if (rest.Length < 2)
            return false;
        var len = BinaryPrimitives.ReadUInt16LittleEndian(rest[..2]);
        if (2 + len > rest.Length)
            return false;
        bytes = rest.Slice(2, len).ToArray();
        after = rest[(2 + len)..];
        return true;
    }

    private static string NormalizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        return string.IsNullOrEmpty(name) ? "file" : name;
    }

    private sealed class PublicDto
    {
        public string M { get; set; } = "";
        public string E { get; set; } = "";
    }

    private sealed class PrivateDto
    {
        public string M { get; set; } = "";
        public string E { get; set; } = "";
        public string D { get; set; } = "";
        public string P { get; set; } = "";
        public string Q { get; set; } = "";
        public string DP { get; set; } = "";
        public string DQ { get; set; } = "";
        public string IQ { get; set; } = "";
    }
}

/// <summary>Locator for a picture or sound file uploaded in ciphertext chunks.</summary>
public sealed record ChatWireTransferOffer(
    string TransferId,
    string TransferToken,
    string PayloadKind,
    string FileName,
    string MimeType,
    long SizeBytes,
    string Host,
    int Port,
    long ExpiresUtcTicks,
    string BlobId,
    int TotalChunks,
    string CiphertextSha256);
