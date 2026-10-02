using System.Text.Json.Nodes;

namespace EtherBound.Sim.World.Gen;

/// <summary>Generator options: validated from JSON, dumped back as Pydantic's <c>model_dump(mode="json")</c>.</summary>
public interface IGeneratorOptions
{
    JsonObject Dump();
}

public sealed record TestOptions : IGeneratorOptions
{
    public JsonObject Dump() => new();
}

public sealed record ReliefOptions(int Amplitude = 12, double Scale = 96.0, int Octaves = 3, double Gain = 0.5, int Edge = 4)
{
    public JsonObject Dump() => new()
    {
        ["amplitude"] = Amplitude,
        ["scale"] = Scale,
        ["octaves"] = Octaves,
        ["gain"] = Gain,
        ["edge"] = Edge,
    };
}

public sealed record LabOptions(string Feature = "none", string SpawnBay = "feature", ReliefOptions? ReliefValue = null) : IGeneratorOptions
{
    public ReliefOptions Relief => ReliefValue ?? new ReliefOptions();

    public JsonObject Dump() => new()
    {
        ["feature"] = Feature,
        ["spawn_bay"] = SpawnBay,
        ["relief"] = Relief.Dump(),
    };
}

/// <summary>One flat, form-ready description of a generator option.</summary>
public sealed record OptionField(string Path, string Label, string Kind, JsonNode? Default, double? Minimum = null,
    double? Maximum = null, double? Step = null, IReadOnlyList<string>? Choices = null, string? Group = null);

public sealed class GeneratorSpec
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required int Version { get; init; }
    public required Func<JsonObject?, IGeneratorOptions> Parse { get; init; }
    public required Func<long, IGeneratorOptions, MaterialRegistry, GeneratedWorld> Generate { get; init; }
    public required Func<long, IGeneratorOptions, (double X, double Y, int H)> Spawn { get; init; }
    public required IReadOnlyList<OptionField> Fields { get; init; }
    public IReadOnlyList<GeneratorBay> Bays { get; init; } = Array.Empty<GeneratorBay>();

    /// <summary>
    /// Set only by a streaming generator (Dev-010): chunks are a pure function of seed, options and
    /// coordinates, built on demand and persisted only once modified. <see cref="Generate"/> then
    /// returns just the spawn and its kit.
    /// </summary>
    public Func<long, IGeneratorOptions, MaterialRegistry, IChunkSource>? Stream { get; init; }

    public bool Streaming => Stream is not null;

    public IGeneratorOptions Defaults => Parse(null);
}

/// <summary>The generator registry (<c>gen/registry.py</c>): <c>test</c> and <c>lab</c>.</summary>
public static class Generators
{
    public const string Default = "test";

    // The spawn search only asks which materials are walkable, so a registry with the catalog's own ids serves.
    private static readonly Lazy<MaterialRegistry> SpawnRegistry = new(() => MaterialRegistry.Load());

    private static readonly string[] LabFeatures = { "none", "relief" };

    public static readonly IReadOnlyDictionary<string, GeneratorSpec> All = new Dictionary<string, GeneratorSpec>
    {
        ["test"] = new()
        {
            Key = "test",
            Name = "Test world",
            Version = TestWorld.GenVersion,
            Parse = _ => new TestOptions(),
            Generate = (seed, _, registry) => TestWorld.Generate(seed, registry),
            Spawn = (_, _) => (TestWorld.SpawnPoint.X, TestWorld.SpawnPoint.Y, 2),
            Fields = Array.Empty<OptionField>(),
        },
        ["infinite"] = new()
        {
            Key = "infinite",
            Name = "Endless world",
            Version = EndlessWorld.GenVersion,
            Parse = ParseEndless,
            Generate = (seed, options, registry) => EndlessWorld.Generate(seed, (EndlessOptions)options, registry),
            Spawn = (seed, options) => EndlessWorld.Spawn(new TerrainField(seed, (EndlessOptions)options, SpawnRegistry.Value)),
            Stream = (seed, options, registry) => new EndlessSource(seed, (EndlessOptions)options, registry),
            Fields = new[]
            {
                new OptionField("relief", "Relief", "int", 12, 0, 24),
                new OptionField("water", "Water", "float", 0.5, 0, 1, 0.05),
                new OptionField("mountains", "Mountains", "float", 0.5, 0, 1, 0.05),
            },
        },
        ["lab"] = new()
        {
            Key = "lab",
            Name = "Debug lab",
            Version = Lab.GenVersion,
            Parse = ParseLab,
            Generate = (seed, options, registry) => Lab.Generate(seed, (LabOptions)options, registry),
            Spawn = (seed, options) => Lab.Spawn(seed, (LabOptions)options),
            Fields = new[]
            {
                new OptionField("feature", "Feature", "choice", "none", Choices: LabFeatures),
                new OptionField("spawn_bay", "Spawn Bay", "choice", "feature", Choices: Lab.BayKeys),
                new OptionField("relief.amplitude", "Amplitude", "int", 12, 0, 24, Group: "relief"),
                new OptionField("relief.scale", "Scale", "float", 96.0, 8, 256, 8, Group: "relief"),
                new OptionField("relief.octaves", "Octaves", "int", 3, 1, 6, Group: "relief"),
                new OptionField("relief.gain", "Gain", "float", 0.5, 0.1, 0.9, 0.05, Group: "relief"),
                new OptionField("relief.edge", "Edge", "int", 4, 0, 12, Group: "relief"),
            },
            Bays = Lab.Bays,
        },
    };

    public static GeneratorSpec? Get(string key) => All.GetValueOrDefault(key);

    /// <summary>Validates like Pydantic: unknown keys are ignored, bad values raise.</summary>
    private static LabOptions ParseLab(JsonObject? raw)
    {
        raw ??= new JsonObject();
        var feature = Choice(raw, "feature", "none", LabFeatures);
        var spawnBay = Choice(raw, "spawn_bay", "feature", Lab.BayKeys);
        var relief = raw["relief"] as JsonObject ?? new JsonObject();
        if (raw["relief"] is not null and not JsonObject) throw new ArgumentException("relief must be an object");
        var options = new ReliefOptions(
            Int(relief, "amplitude", 12, 0, 24),
            Float(relief, "scale", 96.0, 8, 256, 8),
            Int(relief, "octaves", 3, 1, 6),
            Float(relief, "gain", 0.5, 0.1, 0.9, 0.05),
            Int(relief, "edge", 4, 0, 12));
        return new LabOptions(feature, spawnBay, options);
    }

    private static EndlessOptions ParseEndless(JsonObject? raw)
    {
        raw ??= new JsonObject();
        return new EndlessOptions(Int(raw, "relief", 12, 0, 24), Float(raw, "water", 0.5, 0, 1, 0.05),
            Float(raw, "mountains", 0.5, 0, 1, 0.05));
    }

    private static string Choice(JsonObject raw, string key, string fallback, IReadOnlyList<string> choices)
    {
        if (raw[key] is not { } node) return fallback;
        var value = node.GetValue<string>();
        if (!choices.Contains(value)) throw new ArgumentException($"{key}: must be one of {string.Join(", ", choices)}");
        return value;
    }

    private static int Int(JsonObject raw, string key, int fallback, int min, int max)
    {
        if (raw[key] is not { } node) return fallback;
        var value = Core.Json.ToDouble(node);
        if (value != Math.Floor(value)) throw new ArgumentException($"{key}: must be an integer");
        if (value < min || value > max) throw new ArgumentException($"{key}: must be between {min} and {max}");
        return (int)value;
    }

    private static double Float(JsonObject raw, string key, double fallback, double min, double max, double step)
    {
        if (raw[key] is not { } node) return fallback;
        var value = Core.Json.ToDouble(node);
        if (value < min || value > max) throw new ArgumentException($"{key}: must be between {min} and {max}");
        // Pydantic's multiple_of on floats tolerates rounding noise; so does this check.
        var ratio = value / step;
        if (Math.Abs(ratio - Math.Round(ratio)) > 1e-9) throw new ArgumentException($"{key}: must be a multiple of {step}");
        return value;
    }
}
