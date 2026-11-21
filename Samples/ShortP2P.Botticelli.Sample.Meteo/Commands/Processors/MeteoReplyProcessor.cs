using Botticelli.Framework.Commands;
using Botticelli.Framework.Commands.Processors;
using Botticelli.Framework.Commands.Validators;
using Botticelli.Shared.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Samples.Botticelli.Meteo.Commands.Processors;

internal abstract class MeteoReplyProcessor<TCommand>(
    ILogger logger,
    ICommandValidator<TCommand> commandValidator,
    IValidator<Message> messageValidator,
    MeteoReply reply)
    : CommandProcessor<TCommand>(logger, commandValidator, messageValidator)
    where TCommand : class, ICommand
{
    private readonly MeteoReply _reply = reply ?? throw new ArgumentNullException(nameof(reply));

    protected override async Task InnerProcess(Message message, CancellationToken token)
    {
        await SendMessage(await _reply.BuildAsync(message, token).ConfigureAwait(false), token).ConfigureAwait(false);
    }
}
