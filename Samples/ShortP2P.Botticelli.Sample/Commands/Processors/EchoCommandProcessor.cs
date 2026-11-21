using Botticelli.Framework.Commands.Processors;
using Botticelli.Framework.Commands.Validators;
using Botticelli.Shared.API.Client.Requests;
using Botticelli.Shared.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ShortP2P.Samples.Botticelli.Commands.Processors;

public class EchoCommandProcessor : CommandProcessor<EchoCommand>
{
    public EchoCommandProcessor(
        ILogger<EchoCommandProcessor> logger,
        ICommandValidator<EchoCommand> commandValidator,
        IValidator<Message> messageValidator)
        : base(logger, commandValidator, messageValidator)
    {
    }

    protected override Task InnerProcess(Message message, CancellationToken token)
    {
        return SendMessage(new SendMessageRequest
        {
            Message = new Message
            {
                Uid = Guid.NewGuid().ToString(),
                ChatIds = message.ChatIds,
                Body = $"Hello! You just typed: {message.Body}"
            }
        }, token);
    }
}
