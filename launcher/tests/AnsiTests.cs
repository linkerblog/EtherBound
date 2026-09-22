namespace EtherBound.Launcher.Tests;

public class AnsiTests
{
    [Fact]
    public void Strips_the_colours_of_the_vite_banner()
    {
        const string banner =
            "\u001b[32m\u001b[1mVITE\u001b[22m v7.3.6\u001b[39m  \u001b[2mready in \u001b[0m\u001b[1m509\u001b[22m\u001b[2m\u001b[0m ms\u001b[22m";

        Assert.Equal("VITE v7.3.6  ready in 509 ms", Ansi.Strip(banner));
    }

    [Fact]
    public void Keeps_the_text_around_colours_inside_a_url()
    {
        const string line =
            "  \u001b[32m➜\u001b[39m  \u001b[1mLocal\u001b[22m:   \u001b[36mhttp://127.0.0.1:\u001b[1m5173\u001b[22m/\u001b[39m";

        Assert.Equal("  ➜  Local:   http://127.0.0.1:5173/", Ansi.Strip(line));
    }

    [Fact]
    public void Strips_title_sequences() => Assert.Equal("ready", Ansi.Strip("\u001b]0;vite\u0007ready"));

    [Fact]
    public void Leaves_plain_text_alone() =>
        Assert.Equal("INFO:     Application startup complete.", Ansi.Strip("INFO:     Application startup complete."));
}
