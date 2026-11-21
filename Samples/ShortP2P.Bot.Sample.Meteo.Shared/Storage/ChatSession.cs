namespace ShortP2P.Bot.Sample.Meteo.Storage;

public sealed class ChatSession
{
    public required string ClientNetworkId { get; init; }

    public required string Language { get; set; }

    public required bool IsActive { get; set; }
}
