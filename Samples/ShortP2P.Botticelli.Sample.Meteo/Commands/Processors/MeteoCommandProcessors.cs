using Botticelli.Framework.Commands.Validators;
using Botticelli.Shared.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;
using ShortP2P.Samples.Botticelli.Meteo.Commands;

namespace ShortP2P.Samples.Botticelli.Meteo.Commands.Processors;

internal sealed class StartCommandProcessor(
    ILogger<StartCommandProcessor> logger,
    ICommandValidator<StartCommand> commandValidator,
    IValidator<Message> messageValidator,
    MeteoReply reply)
    : MeteoReplyProcessor<StartCommand>(logger, commandValidator, messageValidator, reply);

internal sealed class StopCommandProcessor(
    ILogger<StopCommandProcessor> logger,
    ICommandValidator<StopCommand> commandValidator,
    IValidator<Message> messageValidator,
    MeteoReply reply)
    : MeteoReplyProcessor<StopCommand>(logger, commandValidator, messageValidator, reply);

internal sealed class LangCommandProcessor(
    ILogger<LangCommandProcessor> logger,
    ICommandValidator<LangCommand> commandValidator,
    IValidator<Message> messageValidator,
    MeteoReply reply)
    : MeteoReplyProcessor<LangCommand>(logger, commandValidator, messageValidator, reply);

internal sealed class WeatherCommandProcessor(
    ILogger<WeatherCommandProcessor> logger,
    ICommandValidator<WeatherCommand> commandValidator,
    IValidator<Message> messageValidator,
    MeteoReply reply)
    : MeteoReplyProcessor<WeatherCommand>(logger, commandValidator, messageValidator, reply);
