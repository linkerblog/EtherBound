namespace EtherBound.Launcher.Tests;

public class OptionsTests
{
    [Fact]
    public void No_arguments_start_the_services_with_hot_reload()
    {
        var options = Options.Parse([]);

        Assert.Equal(Mode.Run, options.Mode);
        Assert.True(options.Reload);
        Assert.False(options.Open);
    }

    [Fact]
    public void Run_flags_combine()
    {
        var options = Options.Parse(["--no-reload", "--open"]);

        Assert.Equal(Mode.Run, options.Mode);
        Assert.False(options.Reload);
        Assert.True(options.Open);
    }

    [Theory]
    [InlineData("--cleanup", "Cleanup")]
    [InlineData("--help", "Help")]
    [InlineData("-h", "Help")]
    [InlineData("/?", "Help")]
    public void A_mode_flag_selects_its_mode(string flag, string mode) =>
        Assert.Equal(Enum.Parse<Mode>(mode), Options.Parse([flag]).Mode);

    [Fact]
    public void Ctrl_c_takes_a_process_id()
    {
        var options = Options.Parse(["--ctrl-c", "4242"]);

        Assert.Equal(Mode.CtrlC, options.Mode);
        Assert.Equal(4242, options.ProcessId);
    }

    [Theory]
    [InlineData("--ctrl-c")]
    [InlineData("--ctrl-c", "abc")]
    [InlineData("--ctrl-c", "0")]
    [InlineData("--frobnicate")]
    [InlineData("--cleanup", "--help")]
    [InlineData("--cleanup", "--open")]
    [InlineData("--help", "--no-reload")]
    public void Bad_arguments_are_usage_errors(params string[] args) =>
        Assert.Throws<UsageException>(() => Options.Parse(args));
}
