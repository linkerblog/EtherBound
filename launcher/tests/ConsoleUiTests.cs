namespace EtherBound.Launcher.Tests;

public sealed class ConsoleUiTests
{
    [Fact]
    public void Header_frames_the_version_and_keeps_every_row_aligned()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            new ConsoleUi().Header("v0.4.2");
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("RELEASE  v0.4.2", output.ToString());
        Assert.Contains("SERVER  http://127.0.0.1:8000", output.ToString());
        Assert.Contains("WEB  http://127.0.0.1:5173", output.ToString());
        Assert.All(lines, line => Assert.Equal(68, line.Length));
    }
}
