namespace ShortP2P.Bot.Sample.Storage;

public sealed class BotCertificateRecord
{
    public byte[] Pfx { get; set; } = Array.Empty<byte>();

    public string FingerprintSha256 { get; set; } = "";
}
