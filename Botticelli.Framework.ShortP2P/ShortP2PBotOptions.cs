namespace Botticelli.Framework.ShortP2P;

/// <summary>Settings for <see cref="ShortP2PBot"/>.</summary>
public sealed class ShortP2PBotOptions
{
    /// <summary>RSA private key JSON used to open incoming envelopes. Do not log this value.</summary>
    public string PrivateKeyJson { get; set; } = "";

    /// <summary>Long-poll wait, in seconds. Null uses the messenger client default.</summary>
    public int? PollTimeoutSeconds { get; set; }
}
