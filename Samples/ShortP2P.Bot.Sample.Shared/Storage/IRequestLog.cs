namespace ShortP2P.Bot.Sample.Storage;

public interface IRequestLog
{
    Task AddAsync(IncomingRequest request, CancellationToken cancellationToken = default);
}
