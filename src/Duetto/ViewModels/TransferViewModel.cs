using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.Operations;

namespace Duetto.ViewModels;

public partial class TransferViewModel : ObservableObject, IStripOperation
{
    private readonly PaneViewModel? _sourcePane;
    private readonly DispatcherTimer _timer;
    private bool _completionHandled;

    public TransferSession Session { get; }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _currentFileLine = "";

    [ObservableProperty]
    private string _filesLine = "";

    [ObservableProperty]
    private string _skippedLine = "";

    [ObservableProperty]
    private bool _hasSkipped;

    [ObservableProperty]
    private double _donePercent;

    [ObservableProperty]
    private double _inflightPercent;

    [ObservableProperty]
    private string _pauseLabel = "Pause";

    [ObservableProperty]
    private bool _isFinished;

    [ObservableProperty]
    private string _cancelLabel = "Cancel";

    public ObservableCollection<string> SkippedItems { get; } = [];

    /// <summary>Raised when the strip should go away (auto-hide or user dismiss).</summary>
    public event Action? Dismissed;

    public TransferViewModel(TransferSession session, PaneViewModel? sourcePane)
    {
        Session = session;
        _sourcePane = sourcePane;
        var verb = session.Mode == TransferMode.Copy ? "Copying" : "Moving";
        _title = $"{verb} to {session.DestinationDir}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdateNow();
        _timer.Start();
    }

    [RelayCommand]
    public void TogglePause()
    {
        if (Session.IsPaused)
            Session.Resume();
        else
            Session.Pause();
        PauseLabel = Session.IsPaused ? "Resume" : "Pause";
    }

    [RelayCommand]
    public void CancelOrDismiss()
    {
        if (IsFinished)
        {
            Dismiss();
            return;
        }

        Session.Cancel();
        UpdateNow();
        Dismiss();
    }

    public void Dismiss()
    {
        _timer.Stop();
        foreach (var row in _sourcePane?.Rows ?? [])
            row.TransferStatus = "";
        Dismissed?.Invoke();
    }

    /// <summary>Pulls a snapshot into the bindable properties. Called by the timer; public for tests.</summary>
    public void UpdateNow()
    {
        var snap = Session.Snapshot();

        DonePercent = snap.TotalBytes > 0 ? 100.0 * snap.BytesDone / snap.TotalBytes : (snap.IsComplete ? 100 : 0);
        InflightPercent = snap.TotalBytes > 0 && snap.CurrentFileSize > 0 && !snap.IsComplete
            ? 100.0 * (snap.CurrentFileSize - snap.CurrentFileBytesDone) / snap.TotalBytes
            : 0;

        var verb = snap.Mode == TransferMode.Copy ? "Copying" : "Moving";
        Title = snap.FaultMessage is not null
            ? snap.FaultMessage
            : snap.IsComplete
                ? (snap.IsCancelled ? $"{verb} cancelled" : $"{(snap.Mode == TransferMode.Copy ? "Copied" : "Moved")} to {snap.DestinationDir}")
                : $"{verb} to {snap.DestinationDir}";

        CurrentFileLine = snap.CurrentFileName is { } name && !snap.IsComplete
            ? $"{name} — {FormatUtil.HumanSize(snap.CurrentFileBytesDone)} of {FormatUtil.HumanSize(snap.CurrentFileSize)}" +
              (snap.BytesPerSecond > 1 ? $" · {FormatUtil.HumanSize((long)snap.BytesPerSecond)}/s" : "")
            : "";

        FilesLine = $"{snap.FilesDone} of {snap.TotalFiles} files done";
        HasSkipped = snap.FilesSkipped > 0;
        SkippedLine = HasSkipped
            ? $"{snap.FilesSkipped} skipped — {TransferEngine.SkipReasonNewer}"
            : "";

        if (SkippedItems.Count != snap.Skipped.Count)
        {
            SkippedItems.Clear();
            foreach (var s in snap.Skipped)
                SkippedItems.Add(Path.GetFileName(s.SourcePath));
        }

        foreach (var row in _sourcePane?.Rows ?? [])
        {
            if (Session.StateOf(row.Entry.FullPath) is not { } state)
                continue;
            (row.TransferStatus, row.TransferStatusColor) = state.Status switch
            {
                TransferFileStatus.Done => ("done", "#2f8f5b"),
                TransferFileStatus.InProgress => ($"{(int)state.Percent}%", "#2f6fd0"),
                TransferFileStatus.Skipped => ("skipped", "#b08020"),
                _ => ("queued", "#a8a69c"),
            };
        }

        if (snap.IsComplete && !_completionHandled)
        {
            _completionHandled = true;
            IsFinished = true;
            CancelLabel = "Dismiss";
            _timer.Stop();
            if (!HasSkipped)
                DispatcherTimer.RunOnce(Dismiss, TimeSpan.FromSeconds(1.5));
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        Session.Dispose();
    }
}
