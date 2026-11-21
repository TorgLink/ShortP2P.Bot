using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortP2P.Bot.Commands;
using ShortP2P.Bot.Contracts.Dtos;
using ShortP2P.Bot.Dal;
using ShortP2P.Bot.Messaging;
using ShortP2P.Bot.Security;

namespace ShortP2P.Bot.Hosting;

/// <summary>
/// Publishes the bot on the messenger server before polling starts.
/// The command list comes from <see cref="GetBotCommands"/> and <c>bot_commands</c>.
/// </summary>
public sealed class BotRegistrationService : IHostedService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly IBotMessenger _messenger;
    private readonly IMessengerServerRepository _servers;
    private readonly IBotCertificateProvider _certificates;
    private readonly IMessageKeyStore _messageKeys;
    private readonly IMessageKeyFactory _keyFactory;
    private readonly MessageKeyHolder _keys;
    private readonly GetBotCommands _botCommands;
    private readonly BotRegistrationOptions _options;
    private readonly ILogger<BotRegistrationService> _logger;
    private CancellationTokenSource? _stop;

    public BotRegistrationService(
        IBotMessenger messenger,
        IMessengerServerRepository servers,
        IBotCertificateProvider certificates,
        IMessageKeyStore messageKeys,
        IMessageKeyFactory keyFactory,
        MessageKeyHolder keys,
        GetBotCommands botCommands,
        IOptions<BotRegistrationOptions> options,
        ILogger<BotRegistrationService> logger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
        _messageKeys = messageKeys ?? throw new ArgumentNullException(nameof(messageKeys));
        _keyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _botCommands = botCommands ?? throw new ArgumentNullException(nameof(botCommands));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serverUri = RequireServerUri();
        RequireBotIdentity();

        var listed = _botCommands.Execute(new GetBotCommandsQuery(_options.NetworkId.Trim()));
        _logger.LogInformation(
            "Loaded {CommandCount} commands for {BotNetworkId}",
            listed.Commands.Count,
            listed.BotNetworkId);

        var pair = EnsureMessageKeys();
        _keys.PrivateKeyJson = pair.PrivateJson;

        await EnsureRegisteredAsync(serverUri, _certificates.FingerprintSha256, pair.PublicJson, _stop.Token)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stop?.Cancel();
        return Task.CompletedTask;
    }

    private (string PublicJson, string PrivateJson) EnsureMessageKeys()
    {
        var existing = _messageKeys.TryGet();
        if (existing is not null)
        {
            _logger.LogInformation("Using stored RSA message key pair");
            return existing.Value;
        }

        var created = _keyFactory.Create();
        _messageKeys.Save(created.PublicJson, created.PrivateJson);
        _logger.LogInformation("Generated new RSA message key pair for client to bot encryption");
        return created;
    }

    private async Task<bool> EnsureRegisteredAsync(
        Uri serverUri,
        string fingerprintSha256,
        string messagePublicKey,
        CancellationToken stoppingToken)
    {
        var baseUrl = serverUri.ToString().TrimEnd('/');

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var existing = await _servers.GetByBaseUrlAsync(baseUrl, stoppingToken).ConfigureAwait(false);
                if (existing is not null && !string.IsNullOrEmpty(existing.BotKey))
                {
                    _logger.LogInformation("Re-registering on {BaseUrl} to publish MessagePublicKey", baseUrl);
                    try
                    {
                        await _messenger.RemoveAsync(
                            serverUri,
                            new BotRemoveRequest
                            {
                                NetworkId = existing.NetworkId,
                                BotKey = existing.BotKey
                            },
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Remove before re-register on {BaseUrl} failed; trying register anyway", baseUrl);
                    }
                }

                await _messenger.RegisterAsync(serverUri, new BotRegisterRequest
                {
                    NetworkId = _options.NetworkId.Trim(),
                    BotName = _options.Name.Trim(),
                    BotReadableName = _options.ReadableName.Trim(),
                    BotDescription = _options.Description.Trim(),
                    FingerprintSha256 = fingerprintSha256,
                    MessagePublicKey = messagePublicKey,
                    Version = _options.Version
                }, stoppingToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Registered on {BaseUrl} with MessagePublicKey (len={KeyLen})",
                    baseUrl,
                    messagePublicKey.Length);
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration on {BaseUrl} failed", baseUrl);
                await DelayAsync(stoppingToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private Uri RequireServerUri()
    {
        var baseUrl = _options.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("MessengerServer:BaseUrl must be an absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("MessengerServer:BaseUrl must use https.");

        return uri;
    }

    private void RequireBotIdentity()
    {
        if (string.IsNullOrWhiteSpace(_options.NetworkId)
            || string.IsNullOrWhiteSpace(_options.Name)
            || string.IsNullOrWhiteSpace(_options.ReadableName)
            || string.IsNullOrWhiteSpace(_options.Description))
        {
            throw new InvalidOperationException(
                "Bot:NetworkId, Name, ReadableName and Description are required.");
        }
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
