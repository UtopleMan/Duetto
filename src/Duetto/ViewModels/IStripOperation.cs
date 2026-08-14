namespace Duetto.ViewModels;

public interface IStripOperation : IDisposable
{
    bool IsFinished { get; }

    event Action? Dismissed;
}
