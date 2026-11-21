namespace ShortP2P.Bot.Sample.Meteo.Commands;

public abstract record BotCommand;

public sealed record StartCommand : BotCommand;

public sealed record StopCommand : BotCommand;

public sealed record LangCommand(string Code) : BotCommand;

public sealed record WeatherCommand(string Query) : BotCommand;

public sealed record UnknownCommand(string Text) : BotCommand;
