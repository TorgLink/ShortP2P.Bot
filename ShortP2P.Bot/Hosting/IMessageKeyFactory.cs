namespace ShortP2P.Bot.Hosting;

/// <summary>Creates a new RSA key pair for client to bot messages.</summary>
public interface IMessageKeyFactory
{
    (string PublicJson, string PrivateJson) Create();
}
