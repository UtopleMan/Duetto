using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Duetto.Tests.Core;
using Duetto.ViewModels;
using Duetto.Views;

namespace Duetto.Tests.Ui;

public class ViewerFindTests
{
    private static ViewerViewModel Loaded(TempDir tmp, string name, string contents)
    {
        var path = tmp.File(name, contents);
        var vm = ViewerTests.Viewer();
        vm.Show(path, name);
        return vm;
    }

    [AvaloniaFact]
    public void Query_marks_every_matching_line()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "alpha\nbeta\nALPHA soup\ngamma\nalphabet\n");

        vm.FindQuery = "alpha";

        Assert.Equal(3, vm.MatchCount);
        Assert.Equal([true, false, true, false, true], vm.Lines.Select(l => l.IsMatch));
        Assert.Equal("1 of 3", vm.MatchPositionText);
    }

    [AvaloniaFact]
    public void Find_next_wraps_from_the_last_match_to_the_first()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\nmiss\nhit\n");

        vm.FindQuery = "hit";
        Assert.Equal(0, vm.CurrentMatchIndex);

        vm.FindNext();
        Assert.Equal(1, vm.CurrentMatchIndex);

        vm.FindNext();
        Assert.Equal(0, vm.CurrentMatchIndex);
        Assert.Equal("1 of 2", vm.MatchPositionText);
    }

    [AvaloniaFact]
    public void Find_previous_wraps_backwards()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\nmiss\nhit\n");

        vm.FindQuery = "hit";
        vm.FindPrevious();

        Assert.Equal(1, vm.CurrentMatchIndex);
        Assert.Equal("2 of 2", vm.MatchPositionText);
    }

    [AvaloniaFact]
    public void Query_without_matches_marks_nothing()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "alpha\nbeta\n");

        vm.FindQuery = "zebra";

        Assert.Equal(0, vm.MatchCount);
        Assert.Equal(-1, vm.CurrentMatchIndex);
        Assert.All(vm.Lines, line => Assert.False(line.IsMatch));
        Assert.Equal("no matches", vm.MatchPositionText);
    }

    [AvaloniaFact]
    public void Stepping_with_no_matches_does_nothing()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "alpha\n");

        vm.FindQuery = "zebra";
        vm.FindNext();
        vm.FindPrevious();

        Assert.Equal(-1, vm.CurrentMatchIndex);
    }

    [AvaloniaFact]
    public void Scroll_request_carries_the_matching_line_index()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "miss\nmiss\nhit\n");
        var scrolled = new List<int>();
        vm.ScrollToLineRequested += scrolled.Add;

        vm.FindQuery = "hit";

        Assert.Equal(2, Assert.Single(scrolled));
    }

    [AvaloniaFact]
    public void Changing_the_file_clears_find_state()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "first.txt", "hit\nhit\n");
        vm.FindQuery = "hit";
        Assert.Equal(2, vm.MatchCount);

        var second = tmp.File("second.txt", "nothing here\n");
        vm.Show(second, "second.txt");

        Assert.Equal("", vm.FindQuery);
        Assert.Equal(0, vm.MatchCount);
        Assert.Equal(-1, vm.CurrentMatchIndex);
        Assert.All(vm.Lines, line => Assert.False(line.IsMatch));
    }

    [AvaloniaFact]
    public void Wrap_toggle_flips_and_survives_a_find()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\n");

        vm.ToggleWrap();
        Assert.True(vm.IsWrapped);

        vm.FindQuery = "hit";
        vm.FindNext();

        Assert.True(vm.IsWrapped);
        vm.ToggleWrap();
        Assert.False(vm.IsWrapped);
    }

    [AvaloniaFact]
    public void Wrap_survives_showing_another_file()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "first.txt", "one\n");
        vm.ToggleWrap();

        var second = tmp.File("second.txt", "two\n");
        vm.Show(second, "second.txt");

        Assert.True(vm.IsWrapped);
    }

    [AvaloniaFact]
    public void Find_stays_hidden_in_image_mode()
    {
        using var tmp = new TempDir();
        var path = ViewerTests.WriteBytes(tmp, "pixel.png", ViewerTests.TwoByTwoPng);
        var vm = ViewerTests.Viewer();
        vm.Show(path, "pixel.png");

        vm.OpenFind();

        Assert.False(vm.IsFindOpen);
        Assert.False(vm.IsFindVisible);
    }

    [AvaloniaFact]
    public void Ctrl_f_opens_and_focuses_the_find_box()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\n");
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsFindVisible);
        Assert.IsType<TextBox>(window.FocusManager!.GetFocusedElement());
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_closes_find_first_then_the_window()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\n");
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var closed = false;
        window.Closed += (_, _) => closed = true;

        vm.OpenFind();
        window.FindControl<ListBox>("LineList")!.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsFindVisible);
        Assert.False(closed);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void N_steps_matches_when_the_list_has_focus()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\nmiss\nhit\n");
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        vm.FindQuery = "hit";
        window.FindControl<ListBox>("LineList")!.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, vm.CurrentMatchIndex);

        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.CurrentMatchIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void W_toggles_wrap_when_the_list_has_focus()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "hit\n");
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.FindControl<ListBox>("LineList")!.Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsWrapped);
        window.Close();
    }

    [AvaloniaFact]
    public void Typing_in_the_find_box_does_not_trigger_the_window_shortcuts()
    {
        using var tmp = new TempDir();
        var vm = Loaded(tmp, "log.txt", "win\nwin\n");
        var window = new ViewerWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput("w");
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsWrapped);
        Assert.Equal(0, vm.CurrentMatchIndex);
        window.Close();
    }
}
