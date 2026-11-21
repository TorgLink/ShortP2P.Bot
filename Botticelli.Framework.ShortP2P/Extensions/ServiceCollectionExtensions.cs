using Botticelli.Framework.ShortP2P;
using Botticelli.Interfaces;
using Botticelli.Shared.API.Admin.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Botticelli.Framework.ShortP2P.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBotticelliShortP2P(
        this IServiceCollection services,
        Action<ShortP2PBotOptions>? configure = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        var options = services.AddOptions<ShortP2PBotOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.AddSingleton<ShortP2PBot>();
        services.AddSingleton<IBot>(provider => provider.GetRequiredService<ShortP2PBot>());
        services.AddHostedService<ShortP2PBotHostedService>();
        return services;
    }

    private sealed class ShortP2PBotHostedService : IHostedService
    {
        private readonly ShortP2PBot _bot;

        public ShortP2PBotHostedService(ShortP2PBot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
        }

        public Task StartAsync(CancellationToken cancellationToken) =>
            _bot.StartBotAsync(StartBotRequest.GetInstance(), cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            _bot.StopBotAsync(StopBotRequest.GetInstance(), cancellationToken);
    }
}
