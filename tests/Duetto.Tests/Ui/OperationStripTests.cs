using System.Threading;
using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests.Ui;

public class OperationStripTests
{
    [Fact]
    public void CancelOrDismiss_beforeFinish_cancelsTokenAndRaisesDismissed()
    {
        var cts = new CancellationTokenSource();
        var op = new SimpleOperationViewModel("Deleting 3 items", cts);
        var dismissed = false;
        op.Dismissed += () => dismissed = true;

        op.CancelOrDismissCommand.Execute(null);

        Assert.True(cts.IsCancellationRequested);
        Assert.True(dismissed);
    }
}
