namespace Duetto.ViewModels;

// The host (MainViewModel.ActiveOperation) owns a single slot; when the operation raises
// Dismissed the slot is cleared and disposed.
public interface IStripOperation : IDisposable
{
    bool IsFinished { get; }

    event Action? Dismissed;
}
