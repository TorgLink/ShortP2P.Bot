namespace ShortP2P.Bot.Hosting;

/// <summary>Identity sent to the messenger server. Bind <c>MessengerServer</c> and then <c>Bot</c> from configuration.</summary>
public sealed class BotRegistrationOptions
{
    public string? BaseUrl { get; set; }

    public string NetworkId { get; set; } = "";

    public string Name { get; set; } = "";

    public string ReadableName { get; set; } = "";

    public string Description { get; set; } = "";

    public int Version { get; set; } = 1;
}
