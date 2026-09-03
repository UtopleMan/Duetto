using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Duetto.Core.FileSystem;
using Duetto.Core.State;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class ViewerWindow : Window
{
    public ViewerViewModel Vm { get; }

    private WindowPlacementStore? _placement;
    private Func<IReadOnlyList<ScreenBounds>>? _screensProvider;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public ViewerWindow()
        : this(new ViewerViewModel(new FileSystemRegistry()))
    {
    }

    public ViewerWindow(ViewerViewModel vm)
    {
        Vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.ScrollToLineRequested += line => LineList.ScrollIntoView(line);
    }

    internal void WirePlacement(WindowPlacementStore placement, Func<IReadOnlyList<ScreenBounds>> screens)
    {
        _placement = placement;
        _screensProvider = screens;
        PositionChanged += (_, _) => RecordNormalBounds();
        SizeChanged += (_, _) => RecordNormalBounds();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestorePlacement();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_placement is not null)
        {
            RecordNormalBounds();
            _placement.Save(new WindowPlacement(
                _normalPosition.X, _normalPosition.Y,
                _normalSize.Width, _normalSize.Height,
                Maximized: WindowState == WindowState.Maximized));
        }

        Vm.Cancel();
        base.OnClosing(e);
    }

    private void RecordNormalBounds()
    {
        if (WindowState == WindowState.Normal)
        {
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }
    }

    private void RestorePlacement()
    {
        if (_placement is null || _screensProvider is null)
            return;

        var saved = _placement.Load();
        if (saved is not null && saved.IsVisibleOn(_screensProvider()))
        {
            _normalPosition = new PixelPoint(saved.X, saved.Y);
            _normalSize = new Size(saved.Width, saved.Height);
            Position = _normalPosition;
            Width = saved.Width;
            Height = saved.Height;
            WindowState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
        }
        else
        {
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control)
        {
            OpenFind();
            e.Handled = true;
            return;
        }

        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Escape when Vm.IsFindVisible:
                Vm.CloseFind();
                break;
            case Key.Escape or Key.F3:
                Close();
                break;
            case Key.N when e.KeyModifiers == KeyModifiers.None:
                Vm.FindNext();
                break;
            case Key.N when e.KeyModifiers == KeyModifiers.Shift:
                Vm.FindPrevious();
                break;
            case Key.W when e.KeyModifiers == KeyModifiers.None:
                Vm.ToggleWrap();
                break;
            case Key.PageDown or Key.Down or Key.Right when Vm.IsPdfMode:
                Vm.NextPage();
                break;
            case Key.PageUp or Key.Up or Key.Left when Vm.IsPdfMode:
                Vm.PreviousPage();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.Shift:
                Vm.FindPrevious();
                break;
            case Key.Enter:
                Vm.FindNext();
                break;
            case Key.Escape:
                Vm.CloseFind();
                LineList.Focus();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OpenFind()
    {
        Vm.OpenFind();
        if (!Vm.IsFindVisible)
            return;

        FindBox.Focus();
        FindBox.SelectAll();
    }
}
