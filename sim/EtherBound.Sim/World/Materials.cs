using Tomlyn.Model;

namespace EtherBound.Sim.World;

/// <summary>A registered material. <c>Resistance</c> is joules absorbed per half-metre segment.</summary>
public sealed record Material(
    string Key, string Name, string Color, bool Walkable, double WalkCost, bool Solid, bool BlocksSight,
    bool Diggable, double DigCost, bool Flammable, double Density, double Resistance, bool Liquid,
    IReadOnlyList<string> Tags, int Id);

/// <summary>Static material definitions with save-compatible, append-only ids.</summary>
public sealed class MaterialRegistry
{
    private readonly Dictionary<string, Material> _byKey;
    private readonly Dictionary<int, Material> _byId;

    public MaterialRegistry(IEnumerable<Material> materials)
    {
        Materials = materials.ToList();
        if (Materials.Count == 0) throw new ArgumentException("at least one material is required");
        if (Materials.Select(m => m.Key).Distinct().Count() != Materials.Count) throw new ArgumentException("material keys must be unique");
        if (Materials.Any(m => m.Id <= 0) || Materials.Select(m => m.Id).Distinct().Count() != Materials.Count)
            throw new ArgumentException("material ids must be unique positive integers");
        _byKey = Materials.ToDictionary(m => m.Key);
        _byId = Materials.ToDictionary(m => m.Id);
    }

    public IReadOnlyList<Material> Materials { get; }

    /// <summary>Loads <c>materials.toml</c>; saved ids win, new keys get the next free id in file order.</summary>
    public static MaterialRegistry Load(IReadOnlyDictionary<string, int>? existingIds = null) =>
        FromText(DataFiles.ReadText("materials.toml"), existingIds);

    public static MaterialRegistry FromText(string toml, IReadOnlyDictionary<string, int>? existingIds = null)
    {
        var document = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml)!;
        if (document["materials"] is not TomlTableArray definitions)
            throw new InvalidDataException("materials.toml must contain a materials array");
        var used = new Dictionary<string, int>(existingIds ?? new Dictionary<string, int>());
        var nextId = (used.Count == 0 ? 0 : used.Values.Max()) + 1;
        var values = new List<Material>();
        foreach (var raw in definitions)
        {
            var key = (string)raw["key"];
            if (!used.TryGetValue(key, out var id)) id = nextId++;
            var tags = raw.TryGetValue("tags", out var t) ? ((TomlArray)t).Select(tag => (string)tag!).ToList() : new List<string>();
            values.Add(new Material(
                key, (string)raw["name"], (string)raw["color"], (bool)raw["walkable"],
                DataFiles.Number(raw["walk_cost"]), (bool)raw["solid"], (bool)raw["blocks_sight"],
                (bool)raw["diggable"], DataFiles.Number(raw["dig_cost"]), (bool)raw["flammable"],
                DataFiles.Number(raw["density"]), DataFiles.Number(raw["resistance"]), (bool)raw["liquid"],
                tags, id));
        }
        return new MaterialRegistry(values);
    }

    public int Count => Materials.Count;

    public Material this[string key] => _byKey[key];

    public Material this[int id] => _byId[id];

    public Material? Get(string key) => _byKey.GetValueOrDefault(key);

    public Material? Get(int id) => _byId.GetValueOrDefault(id);

    public Dictionary<string, int> Ids() => Materials.ToDictionary(m => m.Key, m => m.Id);
}
