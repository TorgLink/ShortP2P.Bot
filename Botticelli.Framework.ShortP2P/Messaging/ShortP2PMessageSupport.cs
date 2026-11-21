using System.Diagnostics.CodeAnalysis;
using Botticelli.Framework.ShortP2P.Commands;
using Botticelli.Shared.Constants;
using Botticelli.Shared.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Botticelli.Framework.ShortP2P.Messaging;

/// <summary>
/// Decides whether a Botticelli message can be sent on ShortP2P.
/// Text, pictures, and sound can. Anything else fails with <see cref="NotImplementedException"/> after an error log.
/// Layouts are ignored by the caller and are not a reason to fail when text or a supported file is present.
/// </summary>
public static class ShortP2PMessageSupport
{
    public static void EnsureCanGenerate(Message message, bool isUpdate, ILogger logger)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        if (isUpdate)
            Fail(logger, "message edit");

        if (message.Type == Message.MessageType.Extended)
            Fail(logger, "extended message");

        if (message.Poll != null)
            Fail(logger, "poll");
        if (message.Contact != null)
            Fail(logger, "contact");
        if (message.Location != null)
            Fail(logger, "location");

        var attachments = message.Attachments ?? [];
        var hasBinary = false;
        
        foreach (var binaryAttachment in attachments.Select(attachment => attachment as BinaryBaseAttachment))
        {
            if (binaryAttachment is null)
                Fail(logger, "attachment");

            switch (binaryAttachment.MediaType)
            {
                case MediaType.Image:
                case MediaType.Audio:
                case MediaType.Voice:
                    if (binaryAttachment.Data is not { Length: > 0 })
                        Fail(logger, binaryAttachment.MediaType.ToString());
                    hasBinary = true;
                    break;
                default:
                    Fail(logger, binaryAttachment.MediaType.ToString());
                    break;
            }
        }

        if (message.Type == Message.MessageType.Command && !BotticelliCommandText.IsCommand(message.Body))
            Fail(logger, "command format");

        var hasText = !string.IsNullOrWhiteSpace(message.Body) || !string.IsNullOrWhiteSpace(message.Subject);
        if (!hasText && !hasBinary)
            Fail(logger, string.IsNullOrEmpty(message.CallbackData) ? "empty message" : "callback");
    }

    public static string? TextToSend(Message message)
    {
        if (!string.IsNullOrWhiteSpace(message.Body))
            return message.Body;
        
        return !string.IsNullOrWhiteSpace(message.Subject) ? message.Subject : null;
    }

    public static IReadOnlyList<BinaryBaseAttachment> FilesToSend(Message message)
    {
        if (message.Attachments is null || message.Attachments.Count == 0)
            return [];

        var files = new List<BinaryBaseAttachment>();
        foreach (var attachment in message.Attachments)
        {
            if (attachment is BinaryBaseAttachment binary
                && binary.Data is { Length: > 0 }
                && binary.MediaType is MediaType.Image or MediaType.Audio or MediaType.Voice)
            {
                files.Add(binary);
            }
        }

        return files;
    }

    [DoesNotReturn]
    private static void Fail(ILogger logger, string kind)
    {
        var error = new NotImplementedException(
            $"Cannot generate a ShortP2P message: {kind} is not supported.");
        logger.LogError(
            error,
            "Cannot generate a ShortP2P message: {Kind} is not supported.",
            kind);
        throw error;
    }
}
