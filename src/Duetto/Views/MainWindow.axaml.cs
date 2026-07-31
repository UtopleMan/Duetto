using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Duetto.Core.Remote;
using Duetto.Core.State;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class MainWindow : Window
{
    public MainViewModel Vm { get; }

    // Window-placement persistence. Null when not wired (the plain vm ctor used by tests /
    // the XAML previewer, and headless smoke/screenshot runs) — the overrides then no-op.
    private WindowPlacementStore? _placement;
    private Func<IReadOnlyList<ScreenBounds>>? _screensProvider;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public MainWindow()
        : this(new MainViewModel())
    {
        // Production entry (App.axaml.cs). Persist placement to disk, except in headless
        // smoke/screenshot/CI runs which must never touch the user's window.json.
        if (!Program.Options.Headless)
            WirePlacement(
                new WindowPlacementStore(AppPaths.WindowJsonPath),
                () => Screens.All
                    .Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height))
                    .ToList());
    }

    /// <summary>Test seam: inject an in-memory placement store and a fake screen list.</summary>
    internal MainWindow(MainViewModel vm, WindowPlacementStore placement,
        Func<IReadOnlyList<ScreenBounds>> screens)
        : this(vm)
    {
        WirePlacement(placement, screens);
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

        // macOS clears keyboard focus at the moment the window activates, wiping
        // anything focused while it was still inactive — restore it here.
        Activated += (_, _) =>
        {
            if (FocusManager?.GetFocusedElement() is null)
                RefocusActiveList();
        };
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

    private void OnRemotePlaceClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not RemotePlace remotePlace)
            return;

        var pane = Vm.ActivePane;
        // Resolve the freshest stored record; fall back to the place's own Stored when absent.
        var stored = Vm.ConnectionStore.Load()
                         .FirstOrDefault(c => string.Equals(c.Id, remotePlace.Id, StringComparison.OrdinalIgnoreCase))
                     ?? remotePlace.Stored;

        // Wire the dialog seam so ConnectToShare can open this window's connect dialog.
        var capturedWindow = this;
        Vm.OpenConnectDialog = (forEdit, targetPane) =>
            OpenRemoteConnectDialog(forEdit, targetPane, capturedWindow);

        Vm.ConnectToShare(stored, pane);
    }

    /// <summary>
    /// Opens ConnectWindow (optionally pre-filled via ForEdit) for a remote place click;
    /// on success navigates <paramref name="pane"/> to the connection root.
    /// </summary>
    private void OpenRemoteConnectDialog(StoredConnection? stored, PaneViewModel pane, Window owner)
    {
        var dialogVm = new ConnectDialogViewModel(
            Vm.ConnectionManager,
            Vm.ConnectionStore,
            Vm.HostKeyStore,
            Vm.Codec);
        if (stored is not null)
            dialogVm.ForEdit(stored);

        dialogVm.Connected += info =>
        {
            var remotePath = $"sftp://{info.Id}{info.InitialRemotePath}";
            pane.NavigateTo(remotePath);
            Vm.RebuildRemotePlaces();
        };
        new ConnectWindow(dialogVm).ShowDialog(owner);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.Dispose();
        base.OnClosed(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestorePlacement();
        // Cursor starts on the first row; focus is deferred to Background priority
        // so it lands after Avalonia's own initial-focus pass instead of before it.
        if (Vm.ActivePane.Rows.Count > 0 && Vm.ActivePane.Selection.SelectedItem is null)
            Vm.ActivePane.Selection.Select(0);
        RefocusActiveList(Avalonia.Threading.DispatcherPriority.Background);
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

        base.OnClosing(e);
    }

    private void WirePlacement(WindowPlacementStore placement, Func<IReadOnlyList<ScreenBounds>> screens)
    {
        _placement = placement;
        _screensProvider = screens;
        // Track the last *normal* (non-maximized) bounds so a window closed while maximized
        // still restores to a sensible size when later un-maximized.
        PositionChanged += (_, _) => RecordNormalBounds();
        SizeChanged += (_, _) => RecordNormalBounds();
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
            // Seed the normal-bounds trackers from the saved values first, so restoring
            // straight into a maximized window still remembers a sane un-maximized size.
            _normalPosition = new PixelPoint(saved.X, saved.Y);
            _normalSize = new Size(saved.Width, saved.Height);
            Position = _normalPosition;
            Width = saved.Width;
            Height = saved.Height;
            WindowState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
        }
        else
        {
            // No usable saved placement — keep the XAML default size, seed trackers from it.
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }
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

        // Rename/new-entry control keys. Handled here (tunnel, before the text-input guard)
        // so Enter commits and Escape cancels regardless of whether the inline edit TextBox
        // holds keyboard focus — routing to that box is otherwise fragile (focus timing, or
        // the ListBoxItem/TextBox consuming the key first).
        if (e.Key is Key.Enter or Key.Escape && e.KeyModifiers == KeyModifiers.None
            && Vm.ActivePane.Rows.FirstOrDefault(r => r.IsEditing) is { } editing)
        {
            if (e.Key == Key.Enter)
                Vm.ActivePane.CommitRename(editing);
            else
                Vm.ActivePane.CancelRename(editing);
            RefocusActiveList();
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
                if (Vm.ActivePane.Rows.Count > 0 && Vm.ActivePane.Selection.SelectedItem is null)
                    Vm.ActivePane.Selection.Select(0);
                RefocusActiveList();
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
            case Key.F7 when e.KeyModifiers == KeyModifiers.Shift:
                pane.NewFile();
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
            case Key.Insert:
                pane.ToggleMarkAndAdvance();
                ActivePaneView().List.ScrollIntoView(pane.Selection.SelectedIndex);
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.Space:
                // Toggle the cursor row's mark in place (Insert advances; Space does not).
                if (pane.CursorRow is { } spaceRow)
                    pane.ToggleMarkAt(spaceRow);
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.Down when e.KeyModifiers == KeyModifiers.Shift:
                pane.MarkCursorAndMove(1);
                ActivePaneView().List.ScrollIntoView(pane.Selection.SelectedIndex);
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.Up when e.KeyModifiers == KeyModifiers.Shift:
                pane.MarkCursorAndMove(-1);
                ActivePaneView().List.ScrollIntoView(pane.Selection.SelectedIndex);
                RefocusActiveList();
                e.Handled = true;
                return;
            case Key.Escape when pane.HasMarks:
                pane.ClearMarks();
                e.Handled = true;
                return;
            case Key.PageUp:
                MoveCursorBy(-VisiblePageSize());
                e.Handled = true;
                return;
            case Key.PageDown:
                MoveCursorBy(VisiblePageSize());
                e.Handled = true;
                return;
            case Key.Home when e.KeyModifiers == KeyModifiers.None:
                MoveCursorBy(int.MinValue / 2);
                e.Handled = true;
                return;
            case Key.End when e.KeyModifiers == KeyModifiers.None:
                MoveCursorBy(int.MaxValue / 2);
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
    private void RefocusActiveList(Avalonia.Threading.DispatcherPriority? priority = null) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => ActivePaneView().FocusList(),
            priority ?? Avalonia.Threading.DispatcherPriority.Default);

    /// <summary>Moves the cursor (single selection) by a row delta, clamped to the list.</summary>
    private void MoveCursorBy(int delta)
    {
        var pane = Vm.ActivePane;
        if (pane.Rows.Count == 0)
            return;
        var list = ActivePaneView().List;
        var current = list.SelectedIndex >= 0 ? list.SelectedIndex : Math.Max(0, pane.Selection.AnchorIndex);
        var next = (int)Math.Clamp((long)current + delta, 0, pane.Rows.Count - 1);
        pane.Selection.Clear();
        pane.Selection.Select(next);
        list.ScrollIntoView(next);
        RefocusActiveList();
    }

    /// <summary>Rows visible in the active list at the 27px row height, minus one for context.</summary>
    private int VisiblePageSize() =>
        Math.Max(1, (int)(ActivePaneView().List.Bounds.Height / 27) - 1);


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
