using Botticelli.Shared.Constants;
using Botticelli.Shared.ValueObjects;
using Microsoft.Extensions.Options;
using ShortP2P.Bot.Sample.Meteo.Commands;
using ShortP2P.Bot.Sample.Meteo.Media;

namespace ShortP2P.Samples.Botticelli.Meteo;

/// <summary>
/// Builds the Botticelli message for a meteo reply. Picture and voice travel as attachments.
/// </summary>
internal sealed class MeteoReply(
    MeteoCommandHandler handler,
    IOptions<VoiceOptions> voice,
    ILogger<MeteoReply> logger)
{
    private readonly MeteoCommandHandler _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly VoiceOptions _voice = voice?.Value ?? throw new ArgumentNullException(nameof(voice));
    private readonly ILogger<MeteoReply> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Message> BuildAsync(Message incoming, CancellationToken cancellationToken)
    {
        var chatId = incoming.ChatIds.FirstOrDefault() ?? "";
        var result = await _handler.HandleAsync(chatId, incoming.Body ?? "", cancellationToken).ConfigureAwait(false);
        var outgoing = new Message
        {
            Uid = Guid.NewGuid().ToString(),
            ChatIds = incoming.ChatIds,
            Body = result.Text
        };

        if (result.Report is null)
            return outgoing;

        var attachments = await CreateMediaAsync(chatId, result, cancellationToken).ConfigureAwait(false);
        if (attachments.Count > 0)
            outgoing.Attachments = attachments;
        return outgoing;
    }

    private async Task<List<BaseAttachment>> CreateMediaAsync(
        string chatId,
        CommandResult reply,
        CancellationToken cancellationToken)
    {
        byte[]? png = null;
        VoiceClip? voice = null;
        var condition = MeteoTexts.ConditionKey(reply.Report!.WeatherCode);
        try
        {
            png = WeatherPicture.Render(condition);
            _logger.LogInformation("Weather picture {Condition} is {Bytes} bytes", condition, png.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Weather picture failed for {ClientNetworkId}", chatId);
        }

        try
        {
            var report = reply.Report!;
            var script = MeteoTexts.VoiceScript(
                reply.Language,
                report.DisplayName,
                report.TemperatureC,
                report.HumidityPercent,
                report.WeatherCode);
            voice = await WeatherVoice.SynthesizeAsync(
                reply.Language,
                script,
                _voice.SelectedFormat,
                _voice.BitRate,
                cancellationToken,
                _voice.SelectedEngine,
                _voice.PreferredVoice).ConfigureAwait(false);
            _logger.LogInformation(
                "Weather voice is {Bytes} bytes ({FileName} at {BitRate} bit/s)",
                voice.Value.Bytes.Length,
                voice.Value.FileName,
                voice.Value.BitRate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Weather voice failed for {ClientNetworkId}", chatId);
        }

        if (png is null && voice is null)
            return [];

        try
        {
            WeatherMediaArchive.Save(condition, reply.Language, png, voice, _logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to save weather media under {Folder}", WeatherMediaArchive.RootDirectory);
        }

        var attachments = new List<BaseAttachment>(2);
        if (png is { Length: > 0 })
        {
            attachments.Add(new BinaryBaseAttachment(
                Guid.NewGuid().ToString("N"),
                "weather.png",
                MediaType.Image,
                string.Empty,
                png));
        }

        if (voice is { Bytes.Length: > 0 } spoken)
        {
            attachments.Add(new BinaryBaseAttachment(
                Guid.NewGuid().ToString("N"),
                spoken.FileName,
                MediaType.Voice,
                string.Empty,
                spoken.Bytes));
        }

        return attachments;
    }
}
