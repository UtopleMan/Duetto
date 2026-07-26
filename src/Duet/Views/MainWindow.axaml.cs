using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
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
        ApplyChrome(vm.Chrome);

        LeftPane.Interacted += _ => vm.Activate(vm.Left);
        RightPane.Interacted += _ => vm.Activate(vm.Right);

        // Tab switches panes app-wide; tunnel so focus traversal never sees it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void ApplyChrome(ChromeKind chrome)
    {
        switch (chrome)
        {
            case ChromeKind.Win:
            case ChromeKind.Gnome:
                // Custom title bar replaces the native one.
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
                ExtendClientAreaTitleBarHeightHint = -1;
                break;

            case ChromeKind.Mac:
                // Native title bar; panes float as cards on a recessed desk (design 1b).
                Desk.Background = Brush.Parse("#e8e6e1");
                Desk.Padding = new Thickness(14, 12);
                PanesGrid.ColumnDefinitions[1].Width = new GridLength(12);
                PaneDivider.Background = Brushes.Transparent;
                foreach (var card in new[] { LeftCard, RightCard })
                {
                    card.CornerRadius = new CornerRadius(9);
                    card.BorderBrush = Brush.Parse("#dad7d0");
                    card.BorderThickness = new Thickness(1);
                    card.BoxShadow = BoxShadows.Parse("0 1 3 0 #0d000000");
                }

                UpdateMacTitle();
                Vm.Left.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PaneViewModel.DirName))
                        UpdateMacTitle();
                };
                Vm.Right.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PaneViewModel.DirName))
                        UpdateMacTitle();
                };
                break;
        }
    }

    private void UpdateMacTitle() => Title = $"{Vm.Left.DirName} · {Vm.Right.DirName}";

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnGnomeSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnPlaceClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is Place place)
            Vm.NavigatePlace(place);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LeftPane.List.Focus();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd/Ctrl+F focuses the search field from anywhere.
        if (e.Key == Key.F && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (IsTextInputFocused())
            return;

        var pane = Vm.ActivePane;
        switch (e.Key)
        {
            case Key.Escape when Vm.Search.IsActive:
                Vm.Search.Clear();
                e.Handled = true;
                return;
            case Key.Escape when Vm.CommandBar.IsDrawerOpen:
                Vm.CommandBar.CloseDrawer();
                e.Handled = true;
                return;
            case Key.Enter when Vm.Search.IsActive && e.KeyModifiers == KeyModifiers.None:
                Vm.Search.RevealSelected();
                e.Handled = true;
                return;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                Vm.SwitchPane();
                ActivePaneView().List.Focus();
                e.Handled = true;
                return;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                pane.OpenCursor();
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.Back:
                pane.Up();
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.F2:
                pane.StartRename();
                e.Handled = true;
                return;
            case Key.F5:
                Vm.CopySelected();
                e.Handled = true;
                return;
            case Key.F6:
                Vm.MoveSelected();
                e.Handled = true;
                return;
            case Key.F7:
                pane.NewFolder();
                e.Handled = true;
                return;
            case Key.F8 or Key.Delete:
                Vm.DeleteSelected();
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

    /// <summary>
    /// Navigation reloads replace the row containers; the focused item detaches
    /// asynchronously and would drop focus, so refocus after that cleanup runs.
    /// </summary>
    private void RefocusActiveList() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var list = ActivePaneView().List;
            var container = list.SelectedIndex >= 0 ? list.ContainerFromIndex(list.SelectedIndex) : null;
            if (container is null || !container.Focus())
                list.Focus();
        });

    private bool IsTextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox;

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Vm.Search.Clear();
                ActivePaneView().List.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                if (Vm.TryNavigatePath(SearchBox.Text ?? ""))
                {
                    RefocusActiveList();
                }
                else if (Vm.Search.Results.Count > 0)
                {
                    if (Vm.Search.Selection.SelectedItem is null)
                        Vm.Search.Selection.Select(0);
                    Vm.Search.RevealSelected();
                }

                e.Handled = true;
                break;
        }
    }

    private void OnSizeAny(object? sender, RoutedEventArgs e) => Vm.Search.SetSizeFilter(SizeFilter.Any);
    private void OnSize1(object? sender, RoutedEventArgs e) => Vm.Search.SetSizeFilter(SizeFilter.Over1MB);
    private void OnSize10(object? sender, RoutedEventArgs e) => Vm.Search.SetSizeFilter(SizeFilter.Over10MB);
    private void OnSize100(object? sender, RoutedEventArgs e) => Vm.Search.SetSizeFilter(SizeFilter.Over100MB);
    private void OnDateAny(object? sender, RoutedEventArgs e) => Vm.Search.SetDateFilter(DateFilter.Any);
    private void OnDateToday(object? sender, RoutedEventArgs e) => Vm.Search.SetDateFilter(DateFilter.Today);
    private void OnDateWeek(object? sender, RoutedEventArgs e) => Vm.Search.SetDateFilter(DateFilter.ThisWeek);
    private void OnDateMonth(object? sender, RoutedEventArgs e) => Vm.Search.SetDateFilter(DateFilter.ThisMonth);
}
