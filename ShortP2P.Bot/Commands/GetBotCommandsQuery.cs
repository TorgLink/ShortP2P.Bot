namespace ShortP2P.Bot.Commands;

/// <summary>Asks a bot for the commands loaded from its <c>bot_commands</c> file.</summary>
public sealed class GetBotCommandsQuery(string botNetworkId)
{
    public string BotNetworkId { get; } = botNetworkId;
}
