namespace EtherBound.Launcher.Tests;

public class SourceWatcherTests
{
    [Theory]
    [InlineData(@"C:\Work\EtherBound\server\src\etherbound\app.py", true)]
    [InlineData(@"C:\Work\EtherBound\server\src\etherbound\world\materials.toml", true)]
    [InlineData(@"C:\Work\EtherBound\server\alembic\versions\0002_world.PY", true)]
    [InlineData(@"C:\Work\EtherBound\server\src\etherbound\__pycache__\app.cpython-314.pyc", false)]
    [InlineData(@"C:\Work\EtherBound\server\src\etherbound\__pycache__\app.py", false)]
    [InlineData(@"C:\Work\EtherBound\server\src\etherbound\app.py~", false)]
    [InlineData(@"C:\Work\EtherBound\server\schema.json", false)]
    public void Only_python_sources_and_toml_data_trigger_a_restart(string path, bool matters) =>
        Assert.Equal(matters, SourceWatcher.Matters(path));
}
