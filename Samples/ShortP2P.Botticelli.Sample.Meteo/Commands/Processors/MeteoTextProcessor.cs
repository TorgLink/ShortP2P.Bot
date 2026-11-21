using Botticelli.Bot.Interfaces.Processors;
using Botticelli.Framework.ShortP2P.Commands;
using Botticelli.Interfaces;
using Botticelli.Shared.API.Client.Requests;
using Botticelli.Shared.ValueObjects;

namespace ShortP2P.Samples.Botticelli.Meteo.Commands.Processors;

/// <summary>
/// Answers text that is not a command. <c>/start</c>, <c>/stop</c>, <c>/lang</c> and <c>/weather</c>
/// are handled by their own command processors.
/// </summary>
internal sealed class MeteoTextProcessor : ICommandProcessor
{
    private readonly MeteoReply _reply;
    private IBot? _bot;

    public MeteoTextProcessor(MeteoReply reply)
    {
        _reply = reply ?? throw new ArgumentNullException(nameof(reply));
    }

    public void SetBot(IBot bot) => _bot = bot;

    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
    }

    public async Task ProcessAsync(Message message, CancellationToken token)
    {
        if (_bot is null || string.IsNullOrWhiteSpace(message.Body) || BotticelliCommandText.IsCommand(message.Body))
            return;

        await _bot.SendMessageAsync(new SendMessageRequest
        {
            Message = await _reply.BuildAsync(message, token).ConfigureAwait(false)
        }, token).ConfigureAwait(false);
    }
}
