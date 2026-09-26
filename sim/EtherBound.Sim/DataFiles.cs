using Tomlyn;
using Tomlyn.Model;

namespace EtherBound.Sim;

/// <summary>The TOML data embedded from <c>Data/</c>: materials, object kinds, names and ops.</summary>
public static class DataFiles
{
    public static string ReadText(string name)
    {
        using var stream = typeof(DataFiles).Assembly.GetManifestResourceStream($"EtherBound.Sim.Data.{name}")
            ?? throw new FileNotFoundException($"embedded data file {name} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static TomlTable Read(string name) => TomlSerializer.Deserialize<TomlTable>(ReadText(name))!;

    public static double Number(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
}
