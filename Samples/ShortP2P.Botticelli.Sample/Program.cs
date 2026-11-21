using Botticelli.Framework.Commands.Validators;
using Botticelli.Framework.Extensions;
using Botticelli.Framework.ShortP2P;
using Botticelli.Framework.ShortP2P.Extensions;
using Botticelli.Shared.Extensions;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using ShortP2P.Bot;
using ShortP2P.Bot.Dal;
using ShortP2P.Bot.DependencyInjection;
using ShortP2P.Bot.Hosting;
using ShortP2P.Bot.Http;
using ShortP2P.Bot.Sample.Configuration;
using ShortP2P.Bot.Sample.Storage;
using ShortP2P.Bot.Security;
using ShortP2P.Samples.Botticelli.Commands;
using ShortP2P.Samples.Botticelli.Commands.Processors;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(AppContext.BaseDirectory, "nlog.config"));

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddNLog();

builder.Services.Configure<SampleBotOptions>(builder.Configuration);
builder.Services.AddSingleton<SampleBotDatabase>();
builder.Services.AddSingleton<IMessengerServerRepository, SqliteMessengerServerRepository>();
builder.Services.AddSingleton<IBotCertificateStore, SqliteBotCertificateStore>();
builder.Services.AddSingleton<IMessageKeyStore, SqliteMessageKeyStore>();
builder.Services.Configure<BotRegistrationOptions>(options =>
{
    builder.Configuration.GetSection("MessengerServer").Bind(options);
    builder.Configuration.GetSection("Bot").Bind(options);
});
builder.Services.AddShortP2PBot();
builder.Services.Configure<BotHostOptions>(options =>
    options.Version = builder.Configuration.GetValue("Bot:Version", 1));
builder.Services.AddSingleton<IBotCertificateProvider, StoreBackedBotCertificateProvider>();

if (builder.Configuration.GetValue("MessengerServer:AllowInvalidCertificate", false))
{
    builder.Services.AddHttpClient(HttpClientNames.MessengerServer)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        });
}

builder.Services.AddSharedValidation();
builder.Services.AddBotCommand<EchoCommand>()
    .AddProcessor<EchoCommandProcessor>()
    .AddValidator<PassValidator<EchoCommand>>();

builder.Services.AddBotRegistration();
var pollTimeout = builder.Configuration.GetValue<int?>("MessengerServer:LongPollTimeoutSeconds");
builder.Services.AddBotticelliShortP2P();
builder.Services.AddOptions<ShortP2PBotOptions>()
    .Configure<MessageKeyHolder>((options, holder) =>
    {
        options.PrivateKeyJson = holder.PrivateKeyJson;
        options.PollTimeoutSeconds = pollTimeout;
    });

var host = builder.Build();
try
{
    await host.RunAsync();
}
finally
{
    LogManager.Shutdown();
}
