using Botticelli.Framework.ShortP2P.Commands;
using Botticelli.Framework.ShortP2P.Messaging;
using ShortP2P.Bot.Wire;
using Botticelli.Shared.Constants;
using Botticelli.Shared.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Botticelli.Framework.ShortP2P.Tests;

public sealed class BotticelliCommandTextTests
{
    [Theory]
    [InlineData("/start")]
    [InlineData("/start Moscow")]
    [InlineData("/weather Moscow 3")]
    public void AcceptsBotticelliCommandShape(string body)
    {
        Assert.True(BotticelliCommandText.IsCommand(body));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("start")]
    [InlineData("/get_text")]
    [InlineData("")]
    public void RejectsOtherText(string body)
    {
        Assert.False(BotticelliCommandText.IsCommand(body));
    }
}

public sealed class ShortP2PMessageSupportTests
{
    [Fact]
    public void CommandBodyIsAcceptedUnchanged()
    {
        var message = new Message
        {
            Body = "/weather Moscow",
            Type = Message.MessageType.Command,
            ChatIds = ["client"]
        };

        ShortP2PMessageSupport.EnsureCanGenerate(message, false, new ListLogger());

        Assert.Equal("/weather Moscow", ShortP2PMessageSupport.TextToSend(message));
    }

    [Fact]
    public void CallbackBesideTextIsIgnored()
    {
        var message = new Message
        {
            Body = "hello",
            CallbackData = "button",
            ChatIds = ["client"]
        };

        ShortP2PMessageSupport.EnsureCanGenerate(message, false, new ListLogger());
    }

    [Theory]
    [InlineData(MediaType.Image, "pic.png")]
    [InlineData(MediaType.Audio, "clip.mp3")]
    [InlineData(MediaType.Voice, "clip.ogg")]
    public void PictureAndSoundAreAccepted(MediaType mediaType, string name)
    {
        var message = new Message
        {
            ChatIds = ["client"],
            Attachments =
            [
                new BinaryBaseAttachment(Guid.NewGuid().ToString("N"), name, mediaType, "", [1, 2, 3])
            ]
        };

        ShortP2PMessageSupport.EnsureCanGenerate(message, false, new ListLogger());
    }

    [Fact]
    public void UnsupportedKindThrowsAndIsLogged()
    {
        var logger = new ListLogger();
        var message = new Message
        {
            Body = "hello",
            ChatIds = ["client"],
            Attachments =
            [
                new BinaryBaseAttachment(Guid.NewGuid().ToString("N"), "clip.mp4", MediaType.Video, "", [1])
            ]
        };

        var error = Assert.Throws<NotImplementedException>(() =>
            ShortP2PMessageSupport.EnsureCanGenerate(message, false, logger));

        Assert.Contains("Video", error.Message, StringComparison.Ordinal);
        Assert.Contains("Video", logger.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EditThrowsAndIsLogged()
    {
        var logger = new ListLogger();
        var message = new Message { Body = "hello", ChatIds = ["client"] };

        var error = Assert.Throws<NotImplementedException>(() =>
            ShortP2PMessageSupport.EnsureCanGenerate(message, true, logger));

        Assert.Contains("message edit", error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(logger.Errors);
    }

    [Fact]
    public void CommandWithoutBotticelliShapeThrows()
    {
        var logger = new ListLogger();
        var message = new Message
        {
            Body = "weather Moscow",
            Type = Message.MessageType.Command,
            ChatIds = ["client"]
        };

        var error = Assert.Throws<NotImplementedException>(() =>
            ShortP2PMessageSupport.EnsureCanGenerate(message, false, logger));

        Assert.Contains("command format", error.Message, StringComparison.Ordinal);
        Assert.NotEmpty(logger.Errors);
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}

public sealed class ChatWireCodecTests
{
    [Fact]
    public void CommandTextRoundTripsUnchanged()
    {
        const string command = "/weather Moscow";
        var (publicJson, privateJson) = ChatWireCodec.CreateRsaKeyPairJson();
        var envelope = ChatWireCodec.EncryptTextToEnvelopeBase64(command, publicJson);
        var plain = ChatWireCodec.DecryptEnvelope(Convert.FromBase64String(envelope), privateJson);

        Assert.True(ChatWireCodec.TryDecode(plain, out var frame));
        Assert.Equal(command, frame.Text);
        Assert.True(BotticelliCommandText.IsCommand(frame.Text));
    }
}
