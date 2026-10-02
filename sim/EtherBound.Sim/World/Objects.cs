using System.Globalization;
using Tomlyn.Model;

namespace EtherBound.Sim.World;

public sealed record Container(double Capacity);

public sealed record Wearable(string Slot);

public sealed record Tool(double? Dig = null, double? StrikeSpeedMS = null, double? Build = null);

/// <summary>Eating one unit restores this much hunger (<c>docs/Dev-011.md</c> [Sec. 3], D4).</summary>
public sealed record Edible(double Satiety);

/// <summary>Drinking one unit restores this much thirst.</summary>
public sealed record Drinkable(double Hydration);

/// <summary>A data-defined object kind from <c>objects.toml</c>.</summary>
public sealed record ObjectKind(
    string Key, string Name, string Material, double Mass, double Bulk, int Height, bool Solid, bool Surface,
    bool Fixed, bool Stackable, Container? Container = null, bool Openable = false, Wearable? Wearable = null,
    Tool? Tool = null, Edible? Edible = null, Drinkable? Drinkable = null)
{
    /// <summary>Litres of <paramref name="quantity"/> of a kind; contents do not add bulk.</summary>
    public double TotalBulk(int quantity) => Bulk * quantity;

    /// <summary>More than 25 l takes both hands.</summary>
    public bool TwoHanded => Bulk > 25.0;

    /// <summary>Kilograms of <paramref name="quantity"/> plus the mass of the contents.</summary>
    public double TotalMass(int quantity, double contents = 0.0) => Mass * quantity + contents;
}

/// <summary>Static object kinds, loaded and validated like <c>world/objects.py</c>.</summary>
public sealed class ObjectCatalog
{
    public static readonly IReadOnlySet<string> WearableSlots = new HashSet<string> { "back" };

    private static readonly HashSet<string> KindKeys = new()
    {
        "key", "name", "material", "mass", "bulk", "height", "solid", "surface", "fixed", "stackable",
        "container", "openable", "wearable", "tool", "edible", "drinkable",
    };

    private static readonly Dictionary<string, HashSet<string>> ComponentKeys = new()
    {
        ["container"] = new() { "capacity" },
        ["openable"] = new(),
        ["wearable"] = new() { "slot" },
        ["tool"] = new() { "dig", "strike_speed_m_s", "build" },
        ["edible"] = new() { "satiety" },
        ["drinkable"] = new() { "hydration" },
    };

    private readonly Dictionary<string, ObjectKind> _byKey;

    public ObjectCatalog(IEnumerable<ObjectKind> kinds)
    {
        Kinds = kinds.ToList();
        if (Kinds.Count == 0) throw new ArgumentException("at least one object kind is required");
        if (Kinds.Select(k => k.Key).Distinct().Count() != Kinds.Count) throw new ArgumentException("object kind keys must be unique");
        _byKey = Kinds.ToDictionary(k => k.Key);
    }

    public IReadOnlyList<ObjectKind> Kinds { get; }

    public ObjectKind this[string key] => _byKey[key];

    public ObjectKind? Get(string key) => _byKey.GetValueOrDefault(key);

    public IReadOnlyList<string> Keys => Kinds.Select(k => k.Key).ToList();

    public static ObjectCatalog Load(MaterialRegistry? registry = null) => FromText(DataFiles.ReadText("objects.toml"), registry);

    public static ObjectCatalog FromText(string toml, MaterialRegistry? registry = null)
    {
        var document = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml)!;
        if (document["kinds"] is not TomlTableArray definitions)
            throw new InvalidDataException("objects.toml must contain a kinds array");
        registry ??= MaterialRegistry.Load();
        return new ObjectCatalog(definitions.Select(raw => ParseKind(raw, registry)).ToList());
    }

    private static TomlTable? Table(TomlTable raw, string name, string key)
    {
        if (!raw.TryGetValue(name, out var value)) return null;
        if (value is not TomlTable table) throw new InvalidDataException($"object kind {key}: {name} must be a table");
        var unknown = table.Keys.Where(k => !ComponentKeys[name].Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0) throw new InvalidDataException($"object kind {key}: unknown {name} keys {string.Join(", ", unknown)}");
        return table;
    }

    private static ObjectKind ParseKind(TomlTable raw, MaterialRegistry registry)
    {
        var key = raw.TryGetValue("key", out var k) ? (string)k : "";
        var unknown = raw.Keys.Where(x => !KindKeys.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0) throw new InvalidDataException($"object kind {key}: unknown keys {string.Join(", ", unknown)}");
        if (key.Length == 0) throw new InvalidDataException("object kind key must not be empty");
        var material = (string)raw["material"];
        if (registry.Get(material) is null) throw new InvalidDataException($"object kind {key}: unregistered material {material}");
        bool Flag(string name) => raw.TryGetValue(name, out var v) && (bool)v;
        var solid = Flag("solid");
        var surface = Flag("surface");
        var stackable = Flag("stackable");
        var height = raw.TryGetValue("height", out var hv) ? Convert.ToInt32(hv, CultureInfo.InvariantCulture) : 0;
        if (solid != (height > 0)) throw new InvalidDataException($"object kind {key}: height must be positive exactly when solid");
        if (surface && !solid) throw new InvalidDataException($"object kind {key}: surface requires solid");
        if (surface && registry.Get(material) is not { Walkable: true })
            throw new InvalidDataException($"object kind {key}: a surface material must be walkable");
        if (stackable && (solid || raw.ContainsKey("container") || raw.ContainsKey("openable")))
            throw new InvalidDataException($"object kind {key}: stackable excludes solid, container and openable");

        var containerTable = Table(raw, "container", key);
        var container = containerTable is null ? null : new Container(Positive(containerTable, "capacity", key, "container"));
        var openable = Table(raw, "openable", key) is not null;
        if (openable && container is null) throw new InvalidDataException($"object kind {key}: openable requires container");
        Wearable? wearable = null;
        if (Table(raw, "wearable", key) is { } wearableTable)
        {
            var slot = wearableTable.TryGetValue("slot", out var s) ? (string)s : "";
            if (!WearableSlots.Contains(slot)) throw new InvalidDataException($"object kind {key}: wearable slot must be back");
            wearable = new Wearable(slot);
        }
        Tool? tool = null;
        if (Table(raw, "tool", key) is { } toolTable)
        {
            var dig = OptionalPositive(toolTable, "dig", key, "tool");
            var strike = OptionalPositive(toolTable, "strike_speed_m_s", key, "tool");
            var build = OptionalPositive(toolTable, "build", key, "tool");
            if (dig is null && strike is null && build is null) throw new InvalidDataException($"object kind {key}: tool requires a capability");
            tool = new Tool(dig, strike, build);
        }
        Edible? edible = Table(raw, "edible", key) is { } edibleTable
            ? new Edible(RequiredFraction(edibleTable, "satiety", key, "edible")) : null;
        Drinkable? drinkable = Table(raw, "drinkable", key) is { } drinkableTable
            ? new Drinkable(RequiredFraction(drinkableTable, "hydration", key, "drinkable")) : null;
        return new ObjectKind(key, (string)raw["name"], material, DataFiles.Number(raw["mass"]), DataFiles.Number(raw["bulk"]),
            height, solid, surface, Flag("fixed"), stackable, container, openable, wearable, tool, edible, drinkable);
    }

    private static double Positive(TomlTable table, string field, string key, string component)
    {
        var value = DataFiles.Number(table[field]);
        if (value <= 0) throw new InvalidDataException($"object kind {key}: {component}.{field} must be positive");
        return value;
    }

    private static double RequiredFraction(TomlTable table, string field, string key, string component)
    {
        var value = table.TryGetValue(field, out var raw) ? DataFiles.Number(raw)
            : throw new InvalidDataException($"object kind {key}: {component}.{field} is required");
        if (value is <= 0 or > 1) throw new InvalidDataException($"object kind {key}: {component}.{field} must be in (0, 1]");
        return value;
    }

    private static double? OptionalPositive(TomlTable table, string field, string key, string component) =>
        table.ContainsKey(field) ? Positive(table, field, key, component) : null;
}
