using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Duet.Core.FileSystem;
using Duet.ViewModels;

namespace Duet.Views;

public partial class PaneView : UserControl
{
    private string _typeAhead = "";
    private DateTime _typeAheadAt = DateTime.MinValue;

    private PaneViewModel? _subscribedVm;

    public PaneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is { } old)
                old.Reloaded -= OnVmReloaded;
            _subscribedVm = Vm;
            if (_subscribedVm is { } vm)
                vm.Reloaded += OnVmReloaded;
        };
    }

    /// <summary>
    /// Reload replaces every row container; if the focused one died with it,
    /// keyboard focus becomes null. Restore it to this pane when it is active.
    /// </summary>
    private void OnVmReloaded() => Dispatcher.UIThread.Post(() =>
    {
        if (Vm is { IsActive: true } &&
            TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            FocusList();
    });

    /// <summary>Focuses the selected row container, falling back to the list.</summary>
    public void FocusList()
    {
        var container = RowList.SelectedIndex >= 0 ? RowList.ContainerFromIndex(RowList.SelectedIndex) : null;
        if (container is null || !container.Focus())
            RowList.Focus();
    }

    private PaneViewModel? Vm => DataContext as PaneViewModel;

    /// <summary>Raised when the user interacts with this pane; MainWindow marks it active.</summary>
    public event Action<PaneView>? Interacted;

    public ListBox List => RowList;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Interacted?.Invoke(this);
    }

    private void OnListFocused(object? sender, GotFocusEventArgs e) => Interacted?.Invoke(this);

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm && (e.Source as Control)?.DataContext is FileRowViewModel row)
        {
            vm.Open(row);
            Dispatcher.UIThread.Post(() => RowList.Focus());
        }
    }

    private void OnSortName(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Name);
    private void OnSortSize(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Size);
    private void OnSortType(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Type);
    private void OnSortModified(object? sender, RoutedEventArgs e) => Vm?.SortBy(SortColumn.Modified);

    /// <summary>Type-ahead: printable characters jump the cursor to the first name match.</summary>
    public void TypeAhead(string symbol)
    {
        if (Vm is not { } vm)
            return;
        var now = DateTime.UtcNow;
        _typeAhead = (now - _typeAheadAt).TotalMilliseconds > 900 ? symbol : _typeAhead + symbol;
        _typeAheadAt = now;
        var hit = vm.Rows.FirstOrDefault(r => r.Name.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            vm.SelectByName(hit.Name);
            RowList.ScrollIntoView(vm.Rows.IndexOf(hit));
        }
    }

    private void OnEditBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not FileRowViewModel row || Vm is not { } vm)
            return;
        if (e.Key == Key.Enter)
        {
            vm.CommitRename(row);
            RowList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRename(row);
            RowList.Focus();
            e.Handled = true;
        }
    }

    private void OnEditBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FileRowViewModel { IsEditing: true } row } && Vm is { } vm)
            vm.CommitRename(row);
    }
}
