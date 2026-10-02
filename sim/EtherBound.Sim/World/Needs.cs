using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using Tomlyn.Model;

namespace EtherBound.Sim.World;

/// <summary>One need from <c>needs.toml</c>: how fast it falls and where an actor starts looking for a source.</summary>
public sealed record NeedSpec(string Key, double EmptyHours, double Threshold, double Urgent, bool NightOnly, double NightWeight)
{
    public double RatePerMinute => 1.0 / (EmptyHours * 60.0);
}

/// <summary>The data-defined needs (<c>docs/Dev-011.md</c> [Sec. 3], D1), validated on load.</summary>
public sealed class NeedCatalog
{
    public const string Hunger = "hunger";
    public const string Thirst = "thirst";
    public const string Rest = "rest";

    private static readonly string[] RequiredKeys = { Hunger, Thirst, Rest };
    private static readonly HashSet<string> Fields = new() { "key", "empty_hours", "threshold", "urgent", "night_only", "night_weight" };

    private static readonly Lazy<NeedCatalog> Shared = new(() => FromText(DataFiles.ReadText("needs.toml")));

    private readonly Dictionary<string, NeedSpec> _byKey;

    public NeedCatalog(IEnumerable<NeedSpec> specs)
    {
        Specs = specs.ToList();
        if (Specs.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != Specs.Count) throw new InvalidDataException("duplicate need key");
        _byKey = Specs.ToDictionary(s => s.Key, StringComparer.Ordinal);
        foreach (var key in RequiredKeys)
            if (!_byKey.ContainsKey(key)) throw new InvalidDataException($"needs.toml is missing the need {key}");
    }

    /// <summary>The embedded <c>needs.toml</c>, parsed once.</summary>
    public static NeedCatalog Default => Shared.Value;

    public IReadOnlyList<NeedSpec> Specs { get; }

    public NeedSpec this[string key] => _byKey[key];

    public static NeedCatalog FromText(string toml)
    {
        var document = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml)!;
        if (!document.TryGetValue("needs", out var node) || node is not TomlTableArray definitions)
            throw new InvalidDataException("needs.toml must contain a needs array");
        return new NeedCatalog(definitions.Select(Parse).ToList());
    }

    private static NeedSpec Parse(TomlTable raw)
    {
        var key = raw.TryGetValue("key", out var k) ? (string)k : "";
        if (key.Length == 0) throw new InvalidDataException("need key must not be empty");
        var unknown = raw.Keys.Where(x => !Fields.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0) throw new InvalidDataException($"need {key}: unknown fields {string.Join(", ", unknown)}");
        double Number(string field) => raw.TryGetValue(field, out var v)
            ? DataFiles.Number(v)
            : throw new InvalidDataException($"need {key}: {field} is required");
        var empty = Number("empty_hours");
        if (empty <= 0) throw new InvalidDataException($"need {key}: empty_hours must be positive");
        var threshold = Number("threshold");
        var urgent = Number("urgent");
        if (threshold is <= 0 or > 1) throw new InvalidDataException($"need {key}: threshold must be in (0, 1]");
        if (urgent is <= 0 || urgent > threshold) throw new InvalidDataException($"need {key}: urgent must be in (0, threshold]");
        var weight = Number("night_weight");
        if (weight <= 0) throw new InvalidDataException($"need {key}: night_weight must be positive");
        var nightOnly = raw.TryGetValue("night_only", out var n) && (bool)n;
        return new NeedSpec(key, empty, threshold, urgent, nightOnly, weight);
    }
}

/// <summary>A need as last written: its level at game minute <see cref="At"/>.</summary>
public readonly record struct NeedValue(double Level, int At);

/// <summary>
/// An actor's stored needs. Immutable: <see cref="With"/> returns the next value. A level is never
/// stored as "now"; <see cref="Level"/> derives it from the clock, so time passing writes nothing.
/// </summary>
public sealed class ActorNeeds
{
    private readonly IReadOnlyDictionary<string, NeedValue> _values;

    public ActorNeeds(IReadOnlyDictionary<string, NeedValue> values) => _values = values;

    public static ActorNeeds Full(int minute) => new(NeedCatalog.Default.Specs.ToDictionary(s => s.Key, _ => new NeedValue(1.0, minute)));

    /// <summary>Reads the stored document defensively: a missing or malformed need counts as satisfied now.</summary>
    public static ActorNeeds? Parse(JsonObject? json)
    {
        if (json is null) return null;
        var values = new Dictionary<string, NeedValue>(StringComparer.Ordinal);
        foreach (var spec in NeedCatalog.Default.Specs)
        {
            if (json[spec.Key] is JsonObject entry && entry["level"] is JsonValue level && entry["at"] is JsonValue at &&
                level.TryGetValue<double>(out var l) && at.TryGetValue<int>(out var a))
                values[spec.Key] = new NeedValue(Math.Clamp(l, 0.0, 1.0), a);
        }
        return new ActorNeeds(values);
    }

    public JsonObject ToJson()
    {
        var json = new JsonObject();
        foreach (var spec in NeedCatalog.Default.Specs)
            if (_values.TryGetValue(spec.Key, out var v)) json[spec.Key] = Json.Obj(("level", v.Level), ("at", v.At));
        return json;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The level at <paramref name="minute"/>; a need with no stored value is fully satisfied.</summary>
    public double Level(string key, int minute) => NeedModel.Level(NeedCatalog.Default[key], _values.GetValueOrDefault(key, new NeedValue(1.0, minute)), minute);

    public ActorNeeds With(string key, double level, int minute)
    {
        var next = new Dictionary<string, NeedValue>(_values, StringComparer.Ordinal)
        {
            [key] = new NeedValue(Math.Clamp(level, 0.0, 1.0), minute),
        };
        return new ActorNeeds(next);
    }
}

/// <summary>The pure arithmetic of needs: decay, urgency and the clock of the day.</summary>
public static class NeedModel
{
    public const int MinutesPerDay = 1440;
    public const int NightStartMinute = 22 * 60;
    public const int NightEndMinute = 6 * 60;

    /// <summary>What an actor with no needs row counts as when an op asks for a level (Niko, for now).</summary>
    public const double NeutralLevel = 0.5;

    public static double Level(NeedSpec spec, NeedValue value, int minute) =>
        Math.Max(0.0, value.Level - spec.RatePerMinute * Math.Max(0, minute - value.At));

    public static bool IsNight(int minute)
    {
        var time = ((minute % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        return time >= NightStartMinute || time < NightEndMinute;
    }

    /// <summary>
    /// How badly a need wants a source now, 0 when it is not sought. Rest is sought below its threshold
    /// only at night and below its urgent level at any hour, and weighs more at night.
    /// </summary>
    public static double Urgency(NeedSpec spec, double level, int minute)
    {
        var night = IsNight(minute);
        var limit = spec.NightOnly && !night ? spec.Urgent : spec.Threshold;
        if (level >= limit) return 0.0;
        var urgency = (limit - level) / limit;
        return night ? urgency * spec.NightWeight : urgency;
    }

    /// <summary>The level an Extra starts with, in [0.8, 1.0], so none is hungry in the first two game hours.</summary>
    public static double StartLevel(double roll) => 0.8 + 0.2 * Math.Clamp(roll, 0.0, 1.0);
}
