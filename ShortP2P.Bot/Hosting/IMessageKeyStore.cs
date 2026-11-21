namespace ShortP2P.Bot.Hosting;

/// <summary>Stores the RSA key pair used to open client messages.</summary>
public interface IMessageKeyStore
{
    (string PublicJson, string PrivateJson)? TryGet();

    void Save(string publicJson, string privateJson);
}
