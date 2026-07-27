using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Duetto.ViewModels;

/// <summary>
/// An indeterminate long-running operation (delete, rename, slow listing): a
/// title, a spinner, and a Cancel button. Cancelling trips the shared
/// <see cref="CancellationTokenSource"/> the worker observes.
/// </summary>
public partial class SimpleOperationViewModel : ObservableObject, IStripOperation
{
    private readonly CancellationTokenSource _cts;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string _cancelLabel = "Cancel";

    /// <summary>Marks the strip template selection; always true for this VM.</summary>
    public bool IsIndeterminate => true;

    public event Action? Dismissed;

    public SimpleOperationViewModel(string title, CancellationTokenSource cts)
    {
        _title = title;
        _cts = cts;
    }

    [RelayCommand]
    public void CancelOrDismiss()
    {
        if (!IsFinished)
            _cts.Cancel();
        Dismiss();
    }

    /// <summary>Worker calls this on completion: flips to a done state that auto-hides.</summary>
    public void Finish(string? finalTitle = null)
    {
        if (finalTitle is not null)
            Title = finalTitle;
        IsFinished = true;
        CancelLabel = "Dismiss";
        DispatcherTimer.RunOnce(Dismiss, TimeSpan.FromSeconds(1.0));
    }

    public void Dismiss() => Dismissed?.Invoke();

    public void Dispose() => _cts.Dispose();
}
