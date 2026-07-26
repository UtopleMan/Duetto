using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Duet.ViewModels;

namespace Duet.Views;

public partial class MainWindow : Window
{
    public MainViewModel Vm { get; }

    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel vm)
    {
        Vm = vm;
        DataContext = vm;
        InitializeComponent();

        LeftPane.Interacted += _ => vm.Activate(vm.Left);
        RightPane.Interacted += _ => vm.Activate(vm.Right);

        // Tab switches panes app-wide; tunnel so focus traversal never sees it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LeftPane.List.Focus();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsTextInputFocused())
            return;

        var pane = Vm.ActivePane;
        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                Vm.SwitchPane();
                ActivePaneView().List.Focus();
                e.Handled = true;
                return;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                pane.OpenCursor();
                e.Handled = true;
                return;
            case Key.Back:
                pane.Up();
                e.Handled = true;
                return;
            case Key.F2:
                pane.StartRename();
                e.Handled = true;
                return;
            case Key.F7:
                pane.NewFolder();
                e.Handled = true;
                return;
        }

        if (!string.IsNullOrEmpty(e.KeySymbol) && !char.IsControl(e.KeySymbol[0]) &&
            e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            ActivePaneView().TypeAhead(e.KeySymbol);
            e.Handled = true;
        }
    }

    public PaneView ActivePaneView() => Vm.ActivePane == Vm.Left ? LeftPane : RightPane;

    private bool IsTextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox;
}
