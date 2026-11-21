using System.Collections.Concurrent;
using System.Reflection;
using Botticelli.Client.Analytics;
using Botticelli.Framework.Events;
using Botticelli.Framework.Extensions.Processors;
using Botticelli.Framework.Global;
using Botticelli.Framework.ShortP2P.Commands;
using Botticelli.Framework.ShortP2P.Messaging;
using ShortP2P.Bot.Wire;
using Botticelli.Interfaces;
using Botticelli.Shared.API;
using Botticelli.Shared.API.Admin.Requests;
using Botticelli.Shared.API.Admin.Responses;
using Botticelli.Shared.API.Client.Requests;
using Botticelli.Shared.API.Client.Responses;
using Botticelli.Shared.Constants;
using Botticelli.Shared.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortP2P.Bot.Commands;
using ShortP2P.Bot.Contracts;
using ShortP2P.Bot.Contracts.Dtos;
using ShortP2P.Bot.Dal;
using ShortP2P.Bot.Http;
using ShortP2P.Bot.Messaging;

namespace Botticelli.Framework.ShortP2P;

/// <summary>
/// Draft ShortP2P messenger for Botticelli. Sends and receives text, pictures, and sound.
/// Button layouts are ignored. Any other generated message fails with <see cref="NotImplementedException"/>.
/// </summary>
public sealed class ShortP2PBot : BaseBot<ShortP2PBot>
{
    private readonly IBotMessenger _messenger;
    private readonly IMessengerServerRepository _servers;
    private readonly IOptions<ShortP2PBotOptions> _options;
    private readonly IServiceProvider _services;
    private readonly GetBotCommands _commands;
    private readonly ShortP2PAttachmentSender _attachments;
    private readonly ConcurrentDictionary<string, string> _clientPublicKeys = new(StringComparer.Ordinal);
    private CancellationTokenSource? _run;
    private Task? _loop;

    public ShortP2PBot(
        ILogger<ShortP2PBot> logger,
        IBotMessenger messenger,
        IMessengerServerRepository servers,
        IMessengerServerApiClient api,
        IOptions<ShortP2PBotOptions> options,
        IServiceProvider services,
        GetBotCommands commands,
        MetricsProcessor? metrics = null)
        : base(logger, metrics)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _attachments = new ShortP2PAttachmentSender(api ?? throw new ArgumentNullException(nameof(api)));
    }

    /// <summary>
    /// <see cref="BotType"/> has no ShortP2P value in Botticelli 0.8.
    /// </summary>
    public override BotType Type => BotType.Unknown;

    public override Task SetBotContext(BotData.Entities.Bot.BotData? context, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(context?.BotId))
            BotUserId = context.BotId;

        return Task.CompletedTask;
    }

    protected override Task<StartBotResponse> InnerStartBotAsync(StartBotRequest request, CancellationToken token)
    {
        if (BotStatusKeeper.IsStarted && _loop is { IsCompleted: false })
            return Task.FromResult(StartBotResponse.GetInstance(request.Uid, string.Empty, AdminCommandStatus.Ok));

        BotStatusKeeper.IsStarted = true;
        _run = new CancellationTokenSource();
        _loop = Task.Run(() => ReceiveLoop(_run.Token), CancellationToken.None);
        Logger.LogInformation("ShortP2P bot started.");
        return Task.FromResult(StartBotResponse.GetInstance(request.Uid, string.Empty, AdminCommandStatus.Ok));
    }

    protected override async Task<StopBotResponse> InnerStopBotAsync(StopBotRequest request, CancellationToken token)
    {
        BotStatusKeeper.IsStarted = false;
        if (_run is not null)
            await _run.CancelAsync().ConfigureAwait(false);
        
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Logger.LogInformation("ShortP2P bot stopped.");
        return StopBotResponse.GetInstance(request.Uid, string.Empty, AdminCommandStatus.Ok);
    }

    protected override async Task<SendMessageResponse> InnerSendMessageAsync<TSendOptions>(
        SendMessageRequest request,
        ISendOptionsBuilder<TSendOptions>? optionsBuilder,
        bool isUpdate,
        CancellationToken token)
    {
        _ = optionsBuilder;

        if (!BotStatusKeeper.IsStarted)
        {
            return new SendMessageResponse(request.Uid, "Bot was not started.")
            {
                MessageSentStatus = MessageSentStatus.Nonstarted
            };
        }

        if (request.Message is null)
            throw new ArgumentException("Message is required.", nameof(request));

        ShortP2PMessageSupport.EnsureCanGenerate(request.Message, isUpdate, Logger);

        var text = ShortP2PMessageSupport.TextToSend(request.Message);
        var files = ShortP2PMessageSupport.FilesToSend(request.Message);
        var correlationId = CorrelationId(request.Message);
        var session = await OpenSessionAsync(token).ConfigureAwait(false);

        foreach (var chatId in request.Message.ChatIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            if (!_clientPublicKeys.TryGetValue(chatId, out var publicKey))
            {
                Logger.LogError("No client public key for chat {ChatId}.", chatId);
                throw new InvalidOperationException($"No client public key for chat {chatId}.");
            }

            if (!string.IsNullOrEmpty(text))
            {
                await _messenger.SendMessagesAsync(
                    session.BaseUri,
                    new BotSendMessagesRequest
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        BotNetworkId = session.NetworkId,
                        BotKey = session.BotKey,
                        Messages =
                        [
                            new BotClientMessageDto
                            {
                                NetworkId = chatId,
                                EncryptedMessageBase64 = ChatWireCodec.EncryptTextToEnvelopeBase64(text, publicKey),
                                CorrelationId = correlationId
                            }
                        ]
                    },
                    token).ConfigureAwait(false);
            }

            foreach (var file in files)
            {
                var uploaded = await _attachments.UploadAsync(
                    session.BaseUri,
                    session.Token,
                    session.Host,
                    chatId,
                    publicKey,
                    correlationId,
                    file,
                    token).ConfigureAwait(false);
                await _messenger.SendMessagesAsync(
                    session.BaseUri,
                    new BotSendMessagesRequest
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        BotNetworkId = session.NetworkId,
                        BotKey = session.BotKey,
                        Messages = [uploaded]
                    },
                    token).ConfigureAwait(false);
            }
        }

        var response = new SendMessageResponse(request.Uid, string.Empty)
        {
            MessageSentStatus = MessageSentStatus.Ok,
            Message = request.Message
        };
        Raise(nameof(MessageSent), new MessageSentBotEventArgs { Message = request.Message });
        return response;
    }

    protected override Task<RemoveMessageResponse> InnerDeleteMessageAsync(
        DeleteMessageRequest request,
        CancellationToken token)
    {
        var error = new NotImplementedException("Cannot generate a ShortP2P message: delete is not supported.");
        Logger.LogError(error, "Cannot generate a ShortP2P message: {Kind} is not supported.", "delete");
        throw error;
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && BotStatusKeeper.IsStarted)
        {
            try
            {
                var response = await _messenger.WaitForIncomeMessagesViaPoolAsync(
                    Guid.NewGuid().ToString("N"),
                    _options.Value.PollTimeoutSeconds,
                    cancellationToken).ConfigureAwait(false);

                foreach (var incoming in response.Messages ?? [])
                {
                    var mapped = await MapIncomingAsync(incoming, cancellationToken).ConfigureAwait(false);
                    if (mapped is null)
                        continue;

                    if (await RefuseUnknownCommandAsync(mapped, cancellationToken).ConfigureAwait(false))
                    {
                        Raise(nameof(MessageReceived), new MessageReceivedBotEventArgs { Message = mapped });
                        continue;
                    }

                    await ProcessInProcessors(mapped, cancellationToken).ConfigureAwait(false);
                    Raise(nameof(MessageReceived), new MessageReceivedBotEventArgs { Message = mapped });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "ShortP2P receive failed.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task<bool> RefuseUnknownCommandAsync(Message message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.Body))
            return false;

        var inspection = _commands.Inspect(message.Body);
        if (!inspection.IsCommand || inspection.IsAccepted)
            return false;

        var reply = inspection.RejectionMessage ?? BotCommandRejection.Code;
        Logger.LogWarning(
            "Rejected command from {ClientNetworkId} with {RejectionCode}: {Text}",
            message.ChatIds.FirstOrDefault(),
            inspection.RejectionCode,
            message.Body);
        await SendMessageAsync(new SendMessageRequest
        {
            Message = new Message
            {
                ChatIds = message.ChatIds,
                Body = reply
            }
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task ProcessInProcessors(Message request, CancellationToken token)
    {
        if (token is { CanBeCanceled: true, IsCancellationRequested: true })
            return;

        var processorFactory = ProcessorFactoryBuilder.Build(_services);
        var tasks = processorFactory.GetProcessors()
            .Select(processor => processor.ProcessAsync(request, token))
            .Concat(processorFactory.GetCommandChainProcessors().Select(processor => processor.ProcessAsync(request, token)))
            .ToArray();

        await Parallel.ForEachAsync(tasks, token, async (task, itemToken) => await task.WaitAsync(itemToken).ConfigureAwait(false))
            .ConfigureAwait(false);
    }

    private async Task<Message?> MapIncomingAsync(BotClientMessageDto incoming, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(incoming.ClientMessagePublicKey))
            _clientPublicKeys[incoming.NetworkId] = incoming.ClientMessagePublicKey;

        var privateKey = _options.Value.PrivateKeyJson;
        if (string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(incoming.EncryptedMessageBase64))
        {
            Logger.LogError("Incoming ShortP2P message for {ChatId} cannot be opened.", incoming.NetworkId);
            return null;
        }

        byte[] plain;
        try
        {
            plain = ChatWireCodec.DecryptEnvelope(Convert.FromBase64String(incoming.EncryptedMessageBase64), privateKey);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            Logger.LogError(ex, "Incoming ShortP2P message for {ChatId} cannot be opened.", incoming.NetworkId);
            return null;
        }

        if (!ChatWireCodec.TryDecode(plain, out var frame))
        {
            Logger.LogError("Incoming ShortP2P frame for {ChatId} is not supported.", incoming.NetworkId);
            return null;
        }

        if (frame.Kind == ChatWireCodec.KindTransferOffer && frame.Offer is not null)
        {
            var session = await OpenSessionAsync(cancellationToken).ConfigureAwait(false);
            var downloaded = await _attachments.DownloadAsync(
                session.BaseUri,
                session.Token,
                frame.Offer,
                privateKey,
                cancellationToken).ConfigureAwait(false);
            if (downloaded is null)
            {
                Logger.LogError("Incoming ShortP2P file for {ChatId} is not supported.", incoming.NetworkId);
                return null;
            }

            frame = downloaded.Value;
        }

        return frame.Kind switch
        {
            ChatWireCodec.KindText => TextMessage(incoming, frame.Text ?? ""),
            ChatWireCodec.KindImage => FileMessage(incoming, frame, MediaType.Image),
            ChatWireCodec.KindFile when ChatWireCodec.IsSound(frame.MimeType, frame.Offer?.PayloadKind) =>
                FileMessage(incoming, frame, MediaType.Audio),
            _ => UnsupportedIncoming(incoming.NetworkId)
        };
    }

    private Message? UnsupportedIncoming(string networkId)
    {
        Logger.LogError("Incoming ShortP2P frame for {ChatId} is not supported.", networkId);
        return null;
    }

    private static Message TextMessage(BotClientMessageDto incoming, string text)
    {
        return new Message(incoming.CorrelationId ?? Guid.NewGuid().ToString("N"))
        {
            ChatIds = [incoming.NetworkId],
            Body = text,
            Type = BotticelliCommandText.IsCommand(text)
                ? Message.MessageType.Command
                : Message.MessageType.Messaging,
            From = new User { Id = incoming.NetworkId }
        };
    }

    private static Message FileMessage(BotClientMessageDto incoming, ChatWireCodec.Frame frame, MediaType mediaType)
    {
        var name = string.IsNullOrWhiteSpace(frame.FileName)
            ? mediaType == MediaType.Image ? "image" : "audio"
            : frame.FileName;
        return new Message(incoming.CorrelationId ?? Guid.NewGuid().ToString("N"))
        {
            ChatIds = [incoming.NetworkId],
            Body = string.Empty,
            Type = Message.MessageType.Messaging,
            From = new User { Id = incoming.NetworkId },
            Attachments =
            [
                new BinaryBaseAttachment(
                    Guid.NewGuid().ToString("N"),
                    name,
                    mediaType,
                    string.Empty,
                    frame.Bytes ?? [])
            ]
        };
    }

    private async Task<ServerSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        var servers = await _servers.GetAvailableOrderedAsync(cancellationToken).ConfigureAwait(false);
        var server = servers.FirstOrDefault(item => !item.IsBlocked && !string.IsNullOrWhiteSpace(item.BotKey));
        if (server is null)
            throw new InvalidOperationException("No messenger server is available.");

        var baseUri = new Uri(server.BaseUrl, UriKind.Absolute);
        var login = await _messenger.LoginAsync(baseUri, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(login.Token))
            throw new InvalidOperationException("Messenger server login did not return a token.");

        if (string.IsNullOrWhiteSpace(BotUserId))
            BotUserId = server.NetworkId;

        return new ServerSession(baseUri, login.Token, server.BaseUrl.TrimEnd('/'), server.NetworkId, server.BotKey);
    }

    /// <summary>
    /// <see cref="BaseBot"/> keeps its events private to that class.
    /// A messenger in another assembly cannot call them directly.
    /// </summary>
    private void Raise(string eventName, EventArgs args)
    {
        var field = typeof(BaseBot).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(this) is not Delegate handler)
            return;

        handler.DynamicInvoke(this, args);
    }

    private static string? CorrelationId(Message message)
    {
        var uid = message.Uid;
        if (string.IsNullOrEmpty(uid) || uid.Length > BotLimits.MaxCorrelationIdLength)
            return null;
        return uid;
    }

    private sealed record ServerSession(Uri BaseUri, string Token, string Host, string NetworkId, string BotKey);
}
