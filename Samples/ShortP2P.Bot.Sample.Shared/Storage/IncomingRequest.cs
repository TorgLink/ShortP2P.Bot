namespace ShortP2P.Bot.Sample.Storage;

public sealed class IncomingRequest
{
    public DateTime ReceivedAtUtc { get; set; }

    public string ClientNetworkId { get; set; } = "";

    public string? CorrelationId { get; set; }

    public string MessageText { get; set; } = "";

    public string EncryptedMessageBase64 { get; set; } = "";

    public string ReplyText { get; set; } = "";
}
