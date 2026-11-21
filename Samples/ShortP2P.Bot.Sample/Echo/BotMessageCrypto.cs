using ShortP2P.Bot.Wire;

namespace ShortP2P.Bot.Sample.Echo;

/// <summary>
/// Sample-facing names for <see cref="ChatWireCodec"/>. The frame and envelope live in ShortP2P.Bot.
/// </summary>
internal static class BotMessageCrypto
{
    public static (string PublicJson, string PrivateJson) CreateRsaKeyPairJson() =>
        ChatWireCodec.CreateRsaKeyPairJson();

    public static string DecryptEnvelopeToText(string encryptedMessageBase64, string privateKeyJson) =>
        ChatWireCodec.DecryptEnvelopeToText(encryptedMessageBase64, privateKeyJson);

    public static string EncryptTextToEnvelopeBase64(string text, string recipientPublicKeyJson) =>
        ChatWireCodec.EncryptTextToEnvelopeBase64(text, recipientPublicKeyJson);

    public static byte[] EncodeChatWireImage(string mimeType, ReadOnlySpan<byte> imageBytes) =>
        ChatWireCodec.EncodeImage(mimeType, imageBytes);

    public static byte[] EncodeChatWireFile(string fileName, string mimeType, ReadOnlySpan<byte> fileBytes) =>
        ChatWireCodec.EncodeFile(fileName, mimeType, fileBytes);

    public static byte[] EncodeChatWireTransferOffer(ChatWireTransferOffer offer) =>
        ChatWireCodec.EncodeTransferOffer(offer);

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        ChatWireCodec.Sha256Hex(data);

    public static byte[] EncodeChatWireText(string text) =>
        ChatWireCodec.EncodeText(text);

    public static byte[] EncryptEnvelope(ReadOnlySpan<byte> plaintext, string recipientPublicKeyJson) =>
        ChatWireCodec.EncryptEnvelope(plaintext, recipientPublicKeyJson);

    public static byte[] DecryptEnvelope(ReadOnlySpan<byte> blob, string privateKeyJson) =>
        ChatWireCodec.DecryptEnvelope(blob, privateKeyJson);

    public static string? TryDecodeChatWireText(ReadOnlySpan<byte> payload) =>
        ChatWireCodec.TryDecodeText(payload);
}
