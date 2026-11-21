namespace ShortP2P.Bot.Hosting;

/// <summary>Private key published after registration. The host copies it into the messenger before the first message.</summary>
public sealed class MessageKeyHolder
{
    public string PrivateKeyJson { get; set; } = "";
}
