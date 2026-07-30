using Duetto.ViewModels;
using Xunit;

namespace Duetto.Tests;

public class StartFolderTests
{
    [Fact]
    public void Provided_folder_wins_over_home()
    {
        Assert.Equal("/some/dir", MainViewModel.StartFolder("/some/dir"));
    }

    [Fact]
    public void Null_folder_falls_back_to_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, MainViewModel.StartFolder(null));
    }
}
