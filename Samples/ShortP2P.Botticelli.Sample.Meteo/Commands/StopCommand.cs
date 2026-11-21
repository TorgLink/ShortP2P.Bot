using Botticelli.Framework.Commands;

namespace ShortP2P.Samples.Botticelli.Meteo.Commands;

public sealed class StopCommand : ICommand
{
    public Guid Id { get; }
}