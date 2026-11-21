using Microsoft.Extensions.DependencyInjection;

namespace ShortP2P.Bot.Hosting;

public static class BotRegistrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bot on the messenger server before other hosted services start polling.
    /// Call this before the messenger hosted service. Requires <see cref="IMessageKeyStore"/>.
    /// </summary>
    public static IServiceCollection AddBotRegistration(this IServiceCollection services)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

#if NET8_0_OR_GREATER
        services.AddSingleton<MessageKeyHolder>();
        services.AddSingleton<IMessageKeyFactory, ChatWireMessageKeyFactory>();
        services.AddHostedService<BotRegistrationService>();
        return services;
#else
        throw new PlatformNotSupportedException(
            "Message key registration is available on the .NET 8 build of ShortP2P.Bot.");
#endif
    }
}
