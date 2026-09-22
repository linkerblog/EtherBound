using EtherBound.Launcher.Win32;

namespace EtherBound.Launcher.Tests;

public class CommandLineTests
{
    [Fact]
    public void Plain_arguments_stay_bare() =>
        Assert.Equal(@"C:\tools\uv.exe run uvicorn --port 8000", CommandLine.Build(@"C:\tools\uv.exe", ["run", "uvicorn", "--port", "8000"]));

    [Fact]
    public void Arguments_with_spaces_are_quoted() =>
        Assert.Equal(
            @"""C:\Program Files\nodejs\node.exe"" ""C:\My Work\web\vite.js"" --strictPort",
            CommandLine.Build(@"C:\Program Files\nodejs\node.exe", [@"C:\My Work\web\vite.js", "--strictPort"]));

    [Fact]
    public void Empty_arguments_survive() => Assert.Equal(@"app.exe """"", CommandLine.Build("app.exe", [""]));

    [Fact]
    public void Quotes_and_the_backslashes_before_them_are_escaped() =>
        Assert.Equal(@"app.exe ""say \""hi\"""" ""C:\dir with space\\""", CommandLine.Build("app.exe", ["say \"hi\"", @"C:\dir with space\"]));

    [Fact]
    public void Backslashes_before_other_characters_stay_single() =>
        Assert.Equal(@"app.exe ""C:\a b\c""", CommandLine.Build("app.exe", [@"C:\a b\c"]));

    [Fact]
    public void The_environment_block_applies_overrides_and_ends_with_two_nulls()
    {
        var block = ChildProcess.EnvironmentBlock(new Dictionary<string, string> { ["NO_COLOR"] = "1" });

        Assert.Contains("\0NO_COLOR=1\0", "\0" + block);
        Assert.EndsWith("\0\0", block);
    }
}
