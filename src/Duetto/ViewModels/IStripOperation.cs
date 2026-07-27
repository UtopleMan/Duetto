namespace Duetto.ViewModels;

/// <summary>
/// A long-running operation shown in the unified bottom progress strip. The host
/// (<see cref="MainViewModel.ActiveOperation"/>) owns a single slot; when the
/// operation raises <see cref="Dismissed"/> the slot is cleared and disposed.
/// </summary>
public interface IStripOperation : IDisposable
{
    /// <summary>True once the operation has completed (or been cancelled).</summary>
    bool IsFinished { get; }

    /// <summary>Raised when the strip should go away (auto-hide or user dismiss).</summary>
    event Action? Dismissed;
}
