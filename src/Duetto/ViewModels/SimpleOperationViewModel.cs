using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Duetto.ViewModels;

public partial class SimpleOperationViewModel : ObservableObject, IStripOperation
{
    private readonly CancellationTokenSource _cts;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string _cancelLabel = "Cancel";

    public bool IsIndeterminate => true;

    public double DismissAfterSeconds { get; private set; } = 1.0;

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

    public void Finish(string? finalTitle = null, double? dismissAfterSeconds = null)
    {
        if (finalTitle is not null)
            Title = finalTitle;
        if (dismissAfterSeconds is { } s)
            DismissAfterSeconds = s;
        IsFinished = true;
        CancelLabel = "Dismiss";
        DispatcherTimer.RunOnce(Dismiss, TimeSpan.FromSeconds(DismissAfterSeconds));
    }

    public void Dismiss() => Dismissed?.Invoke();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
