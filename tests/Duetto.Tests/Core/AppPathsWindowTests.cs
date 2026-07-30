using Duetto.Core.Remote;
using Xunit;

namespace Duetto.Tests.Core;

public class AppPathsWindowTests
{
    [Fact]
    public void WindowJsonPath_is_window_json_in_config_dir()
    {
        Assert.Equal(Path.Combine(AppPaths.ConfigDir, "window.json"), AppPaths.WindowJsonPath);
    }
}
