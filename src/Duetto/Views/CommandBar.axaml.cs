using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Duetto.ViewModels;

namespace Duetto.Views;

public partial class CommandBar : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public CommandBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
                vm.CommandBar.Output.CollectionChanged += OnOutputChanged;
        };
    }

    private void OnOutputChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OutputScroll.ScrollToEnd();

    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await vm.CommandBar.RunAsync();
                break;
            case Key.Up:
                vm.CommandBar.HistoryUp();
                InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
                e.Handled = true;
                break;
            case Key.Down:
                vm.CommandBar.HistoryDown();
                InputBox.CaretIndex = InputBox.Text?.Length ?? 0;
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CommandBar.Escape();
                e.Handled = true;
                break;
        }
    }

    private async void OnCopyOutput(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.CommandBar.AllOutputText());
    }
}
