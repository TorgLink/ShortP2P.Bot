using ShortP2P.Bot.Wire;
using Botticelli.Shared.Constants;
using Botticelli.Shared.ValueObjects;
using ShortP2P.Bot.Contracts;
using ShortP2P.Bot.Contracts.Dtos;
using ShortP2P.Bot.Http;

namespace Botticelli.Framework.ShortP2P.Messaging;

internal sealed class ShortP2PAttachmentSender
{
    private readonly IMessengerServerApiClient _api;

    public ShortP2PAttachmentSender(IMessengerServerApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<BotClientMessageDto> UploadAsync(
        Uri serverBaseUrl,
        string bearerToken,
        string host,
        string clientNetworkId,
        string clientPublicKeyJson,
        string? correlationId,
        BinaryBaseAttachment attachment,
        CancellationToken cancellationToken)
    {
        var image = attachment.MediaType == MediaType.Image;
        var mime = Mime(attachment, image);
        var fileName = string.IsNullOrWhiteSpace(attachment.Name)
            ? image ? "image.png" : "audio.ogg"
            : attachment.Name;
        var inner = image
            ? ChatWireCodec.EncodeImage(mime, attachment.Data)
            : ChatWireCodec.EncodeFile(fileName, mime, attachment.Data);
        var ciphertext = ChatWireCodec.EncryptEnvelope(inner, clientPublicKeyJson);
        if (ciphertext.Length > BlobLimits.MaxCiphertextBytes)
            throw new InvalidOperationException("Ciphertext exceeds the blob limit.");

        var chunks = Split(ciphertext);
        var sha = ChatWireCodec.Sha256Hex(ciphertext);
        var blobId = Guid.NewGuid().ToString("N");
        for (var i = 0; i < chunks.Length; i++)
        {
            await _api.PutBlobChunkAsync(
                serverBaseUrl,
                bearerToken,
                blobId,
                i,
                clientNetworkId,
                chunks.Length,
                ciphertext.Length,
                chunks[i],
                cancellationToken).ConfigureAwait(false);
        }

        var expires = DateTime.UtcNow.AddHours(24).Ticks;
        var offer = ChatWireCodec.EncodeTransferOffer(new ChatWireTransferOffer(
            blobId,
            sha,
            image ? "image" : "audio",
            fileName,
            mime,
            attachment.Data.LongLength,
            host,
            0,
            expires,
            blobId,
            chunks.Length,
            sha));

        return new BotClientMessageDto
        {
            NetworkId = clientNetworkId,
            EncryptedMessageBase64 = Convert.ToBase64String(ChatWireCodec.EncryptEnvelope(offer, clientPublicKeyJson)),
            CorrelationId = correlationId
        };
    }

    public async Task<ChatWireCodec.Frame?> DownloadAsync(
        Uri serverBaseUrl,
        string bearerToken,
        ChatWireTransferOffer offer,
        string privateKeyJson,
        CancellationToken cancellationToken)
    {
        if (offer.TotalChunks <= 0)
            return null;

        var blobId = string.IsNullOrWhiteSpace(offer.BlobId) ? offer.TransferId : offer.BlobId;
        var parts = new byte[offer.TotalChunks][];
        for (var i = 0; i < offer.TotalChunks; i++)
        {
            parts[i] = await _api.GetBlobChunkAsync(
                serverBaseUrl,
                bearerToken,
                blobId,
                i,
                cancellationToken).ConfigureAwait(false);
        }

        var ciphertext = Join(parts);
        var plain = ChatWireCodec.DecryptEnvelope(ciphertext, privateKeyJson);
        return ChatWireCodec.TryDecode(plain, out var frame) ? frame : null;
    }

    private static string Mime(BinaryBaseAttachment attachment, bool image)
    {
        var ext = Path.GetExtension(attachment.Name ?? "").ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ogg" or ".oga" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            _ => image ? "image/png" : "audio/ogg"
        };
    }

    private static byte[][] Split(byte[] ciphertext)
    {
        var size = BlobLimits.ChunkSizeBytes;
        var count = (ciphertext.Length + size - 1) / size;
        var chunks = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            var offset = i * size;
            var length = Math.Min(size, ciphertext.Length - offset);
            chunks[i] = ciphertext.AsSpan(offset, length).ToArray();
        }

        return chunks;
    }

    private static byte[] Join(byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
            length += part.Length;
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
