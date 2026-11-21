namespace ShortP2P.Bot.Http;

/// <summary>
/// Raised when the messenger server TLS fingerprint does not match the value pinned at registration.
/// Further operations against that server must be blocked.
/// </summary>
public sealed class MessengerServerCertificateException(
    string message,
    string? expectedFingerprint = null,
    string? actualFingerprint = null)
    : Exception(message)
{
    public string? ExpectedFingerprint { get; } = expectedFingerprint;

    public string? ActualFingerprint { get; } = actualFingerprint;
}
