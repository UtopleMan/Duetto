using System.Diagnostics;
using System.Text.RegularExpressions;
using Duetto.Core.Operations;
using Xunit;

namespace Duetto.Tests.Core;

public class MacTrashTests
{
    [Fact]
    public void Trash_folder_on_other_volume_succeeds()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var dmg = Path.Combine(Path.GetTempPath(), "duetto-trash-" + Guid.NewGuid().ToString("N")[..8] + ".dmg");
        if (!Run("hdiutil", $"create -size 10m -fs APFS -volname DuettoTrashTest -ov \"{dmg}\"", out _))
            return;

        string? mount = null;
        try
        {
            if (!Run("hdiutil", $"attach \"{dmg}\"", out var attachOut))
                return;
            mount = Regex.Match(attachOut, "/Volumes/\\S+").Value;
            if (string.IsNullOrEmpty(mount) || !Directory.Exists(mount))
                return;

            var folder = Path.Combine(mount, "folder");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "f.txt"), "bye");

            TrashService.Trash(folder);

            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (mount is not null)
                Run("hdiutil", $"detach \"{mount}\"", out _);
            try { File.Delete(dmg); } catch (IOException) { }
        }
    }

    private static bool Run(string file, string args, out string stdout)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }
}
