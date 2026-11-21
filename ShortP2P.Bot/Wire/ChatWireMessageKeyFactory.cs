using ShortP2P.Bot.Wire;

namespace ShortP2P.Bot.Hosting;

public sealed class ChatWireMessageKeyFactory : IMessageKeyFactory
{
    public (string PublicJson, string PrivateJson) Create() => ChatWireCodec.CreateRsaKeyPairJson();
}
