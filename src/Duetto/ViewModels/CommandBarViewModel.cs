using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Duetto.Core.Shell;

namespace Duetto.ViewModels;

public sealed record OutputLine(string Text, string Color);

public partial class CommandBarViewModel : ObservableObject
{
    private readonly ShellRunner _runner = new();
    private readonly Func<string> _cwd;
    private int _historyIndex = -1;

    [ObservableProperty]
    private string _input = "";

    [ObservableProperty]
    private bool _isDrawerOpen;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _ranCommand = "";

    [ObservableProperty]
    private string _exitText = "";

    [ObservableProperty]
    private bool _exitOk = true;

    [ObservableProperty]
    private bool _hasExited;

    public ObservableCollection<OutputLine> Output { get; } = [];

    /// <summary>Raised after a command exits so panes can refresh.</summary>
    public event Action? CommandFinished;

    public CommandBarViewModel(Func<string> cwdProvider) => _cwd = cwdProvider;

    public async Task RunAsync()
    {
        var command = Input.Trim();
        if (command.Length == 0 || IsRunning)
            return;

        RanCommand = command;
        Input = "";
        _historyIndex = -1;
        Output.Clear();
        IsDrawerOpen = true;
        IsRunning = true;
        HasExited = false;
        ExitText = "";

        try
        {
            var result = await _runner.RunAsync(command, _cwd(), line =>
                Dispatcher.UIThread.Post(() => Output.Add(new OutputLine(line.Text, ColorFor(line.Stream)))));
            ExitOk = result.ExitCode == 0;
            ExitText = $"exit {result.ExitCode} · {result.Duration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s";
        }
        catch (Exception e)
        {
            Output.Add(new OutputLine(e.Message, "#d97a6a"));
            ExitOk = false;
            ExitText = "failed to start";
        }
        finally
        {
            IsRunning = false;
            HasExited = true;
            CommandFinished?.Invoke();
        }
    }

    [RelayCommand]
    public void CloseDrawer() => IsDrawerOpen = false;

    /// <summary>Esc: first close the drawer, then clear the input.</summary>
    public void Escape()
    {
        if (IsDrawerOpen)
            IsDrawerOpen = false;
        else
            Input = "";
    }

    public void HistoryUp()
    {
        if (_runner.History.Count == 0)
            return;
        _historyIndex = _historyIndex < 0
            ? _runner.History.Count - 1
            : Math.Max(0, _historyIndex - 1);
        Input = _runner.History[_historyIndex];
    }

    public void HistoryDown()
    {
        if (_runner.History.Count == 0 || _historyIndex < 0)
            return;
        _historyIndex++;
        if (_historyIndex >= _runner.History.Count)
        {
            _historyIndex = -1;
            Input = "";
        }
        else
        {
            Input = _runner.History[_historyIndex];
        }
    }

    public string AllOutputText() => string.Join(Environment.NewLine, Output.Select(l => l.Text));

    private static string ColorFor(ShellStream stream) =>
        stream == ShellStream.Error ? "#d9b45c" : "#d8d5cc";
}
