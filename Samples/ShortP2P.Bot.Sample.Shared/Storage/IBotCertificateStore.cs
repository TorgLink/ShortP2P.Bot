namespace ShortP2P.Bot.Sample.Storage;

public interface IBotCertificateStore
{
    BotCertificateRecord GetOrCreate(string subjectCommonName);
}
