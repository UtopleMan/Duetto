using Avalonia.Controls;
using Duet.ViewModels;

namespace Duet.Views;

public partial class ProgressStrip : UserControl
{
    public ProgressStrip()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TransferViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(TransferViewModel.DonePercent)
                        or nameof(TransferViewModel.InflightPercent))
                        UpdateBar(vm);
                };
                UpdateBar(vm);
            }
        };
    }

    private void UpdateBar(TransferViewModel vm)
    {
        var done = Math.Clamp(vm.DonePercent, 0, 100);
        var inflight = Math.Clamp(vm.InflightPercent, 0, 100 - done);
        BarGrid.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
        BarGrid.ColumnDefinitions[1].Width = new GridLength(inflight, GridUnitType.Star);
        BarGrid.ColumnDefinitions[2].Width = new GridLength(Math.Max(0, 100 - done - inflight), GridUnitType.Star);
    }
}
