using Botticelli.Framework.Commands.Validators;
using Botticelli.Framework.Extensions;
using Botticelli.Framework.Extensions.Processors;
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
using ShortP2P.Bot.Sample.Meteo.Media;
using ShortP2P.Bot.Sample.Meteo.Storage;
using ShortP2P.Bot.Sample.Meteo.Weather;
using ShortP2P.Bot.Sample.Storage;
using ShortP2P.Bot.Security;
using ShortP2P.Samples.Botticelli.Meteo;
using ShortP2P.Samples.Botticelli.Meteo.Commands;
using ShortP2P.Samples.Botticelli.Meteo.Commands.Processors;
using MeteoCommandHandler = ShortP2P.Bot.Sample.Meteo.Commands.MeteoCommandHandler;
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
builder.Services.AddSingleton<SqliteChatStore>();
builder.Services.AddSingleton<SqliteWeatherCache>();
builder.Services.Configure<BotRegistrationOptions>(options =>
{
    builder.Configuration.GetSection("MessengerServer").Bind(options);
    builder.Configuration.GetSection("Bot").Bind(options);
});
builder.Services.Configure<WeatherOptions>(builder.Configuration.GetSection(WeatherOptions.SectionName));
builder.Services.Configure<VoiceOptions>(builder.Configuration.GetSection(VoiceOptions.SectionName));
var voice = new VoiceOptions();
builder.Configuration.GetSection(VoiceOptions.SectionName).Bind(voice);
voice.Validate();
var weatherProvider = WeatherProviders.Parse(builder.Configuration["Weather:Provider"]);
if (weatherProvider == WeatherProviderKind.BrightSky)
    WeatherOptions.BrightSkyAddress(builder.Configuration["Weather:BrightSkyBaseUrl"]);
if (weatherProvider != WeatherProviderKind.OpenMeteo)
    WeatherOptions.OpenStreetMapAddress(builder.Configuration["Weather:OpenStreetMapBaseUrl"]);
var weatherProxyUrl = builder.Configuration["Weather:ProxyUrl"];
_ = WeatherOptions.ProxyAddress(weatherProxyUrl);

builder.Services.AddHttpClient<OpenMeteoClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
}).ConfigurePrimaryHttpMessageHandler(() => WeatherOptions.CreateHandler(weatherProxyUrl));
builder.Services.AddHttpClient<BrightSkyClient>(client =>
{
    client.BaseAddress = WeatherOptions.BrightSkyAddress(builder.Configuration["Weather:BrightSkyBaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ShortP2P.Botticelli.Sample.Meteo");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).ConfigurePrimaryHttpMessageHandler(() => WeatherOptions.CreateHandler(weatherProxyUrl));
builder.Services.AddHttpClient<OpenStreetMapGeocoder>(client =>
{
    client.BaseAddress = WeatherOptions.OpenStreetMapAddress(builder.Configuration["Weather:OpenStreetMapBaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ShortP2P.Botticelli.Sample.Meteo");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
}).ConfigurePrimaryHttpMessageHandler(() => WeatherOptions.CreateHandler(weatherProxyUrl));
builder.Services.AddSingleton<ICurrentWeatherSource>(sp =>
    weatherProvider == WeatherProviderKind.BrightSky
        ? sp.GetRequiredService<BrightSkyClient>()
        : sp.GetRequiredService<OpenMeteoClient>());
builder.Services.AddSingleton<IPlaceGeocoder>(sp =>
    weatherProvider == WeatherProviderKind.OpenMeteo
        ? sp.GetRequiredService<OpenMeteoClient>()
        : sp.GetRequiredService<OpenStreetMapGeocoder>());
builder.Services.AddSingleton<WeatherService>();
builder.Services.AddSingleton<MeteoCommandHandler>();
builder.Services.AddSingleton<MeteoReply>();
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
builder.Services.AddBotCommand<StartCommand>()
    .AddProcessor<StartCommandProcessor>()
    .AddValidator<PassValidator<StartCommand>>();
builder.Services.AddBotCommand<StopCommand>()
    .AddProcessor<StopCommandProcessor>()
    .AddValidator<PassValidator<StopCommand>>();
builder.Services.AddBotCommand<LangCommand>()
    .AddProcessor<LangCommandProcessor>()
    .AddValidator<PassValidator<LangCommand>>();
builder.Services.AddBotCommand<WeatherCommand>()
    .AddProcessor<WeatherCommandProcessor>()
    .AddValidator<PassValidator<WeatherCommand>>();
builder.Services.AddSingleton<MeteoTextProcessor>();
ProcessorFactoryBuilder.AddProcessor<MeteoTextProcessor>(builder.Services);

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
