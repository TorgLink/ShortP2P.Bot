using Botticelli.Framework.Commands.Validators;
using Botticelli.Shared.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;
using ShortP2P.Samples.Botticelli.Meteo.Commands;

namespace ShortP2P.Samples.Botticelli.Meteo.Commands.Processors;

internal sealed class StartCommandProcessor : MeteoReplyProcessor<StartCommand>
{
    public StartCommandProcessor(
        ILogger<StartCommandProcessor> logger,
        ICommandValidator<StartCommand> commandValidator,
        IValidator<Message> messageValidator,
        MeteoReply reply)
        : base(logger, commandValidator, messageValidator, reply)
    {
    }
}

internal sealed class StopCommandProcessor : MeteoReplyProcessor<StopCommand>
{
    public StopCommandProcessor(
        ILogger<StopCommandProcessor> logger,
        ICommandValidator<StopCommand> commandValidator,
        IValidator<Message> messageValidator,
        MeteoReply reply)
        : base(logger, commandValidator, messageValidator, reply)
    {
    }
}

internal sealed class LangCommandProcessor : MeteoReplyProcessor<LangCommand>
{
    public LangCommandProcessor(
        ILogger<LangCommandProcessor> logger,
        ICommandValidator<LangCommand> commandValidator,
        IValidator<Message> messageValidator,
        MeteoReply reply)
        : base(logger, commandValidator, messageValidator, reply)
    {
    }
}

internal sealed class WeatherCommandProcessor : MeteoReplyProcessor<WeatherCommand>
{
    public WeatherCommandProcessor(
        ILogger<WeatherCommandProcessor> logger,
        ICommandValidator<WeatherCommand> commandValidator,
        IValidator<Message> messageValidator,
        MeteoReply reply)
        : base(logger, commandValidator, messageValidator, reply)
    {
    }
}
