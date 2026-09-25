using System.Text.Json;

namespace EtherBound.Sim.Tests;

/// <summary>Reads the files exported by <c>server/scripts/export_goldens.py</c>.</summary>
public static class Goldens
{
    public static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Goldens");

    public static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Directory, name)));
}
