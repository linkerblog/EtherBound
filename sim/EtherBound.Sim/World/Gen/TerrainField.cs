using System.Text.Json.Nodes;

namespace EtherBound.Sim.World.Gen;

/// <summary>Options of the <c>infinite</c> generator; see <see cref="Generators"/> for their ranges.</summary>
public sealed record EndlessOptions(int Relief = 12, double Water = 0.5, double Mountains = 0.5) : IGeneratorOptions
{
    public JsonObject Dump() => new()
    {
        ["relief"] = Relief,
        ["water"] = Water,
        ["mountains"] = Mountains,
    };
}

/// <summary>One tile of the field: its ground height in half-metres and its surface material id.</summary>
public readonly record struct TerrainCell(int Height, int Material);

/// <summary>
/// The terrain of the endless world as a pure function of tile position: no grid, no chunk and no
/// database, so a chunk border can never show a seam and the minimap samples the very same ground.
/// Heights are half-metres. Water is a surface material at a lowered height, as in the test world's pond.
/// </summary>
public sealed class TerrainField
{
    private const int SaltContinent = 1, SaltHills = 2, SaltHillMask = 3, SaltMountain = 4, SaltRiver = 7,
        SaltDeep = 8, SaltMoisture = 9, SaltOutcrop = 10;

    private readonly HashNoise _noise;
    private readonly EndlessOptions _options;
    private readonly MaterialRegistry _registry;
    private readonly double _seaLevel;
    private readonly double _riverWidth;
    private readonly int _grass, _dirt, _topsoil, _clay, _sand, _rock, _shallow, _deep;

    public TerrainField(long seed, EndlessOptions options, MaterialRegistry registry)
    {
        _noise = new HashNoise(seed);
        _options = options;
        _registry = registry;
        _seaLevel = Math.Round(-2.0 + (options.Water - 0.5) * 16.0);
        _riverWidth = 0.004 + options.Water * 0.016;
        _grass = registry["grass"].Id;
        _dirt = registry["dirt"].Id;
        _topsoil = registry["topsoil"].Id;
        _clay = registry["clay"].Id;
        _sand = registry["sand"].Id;
        _rock = registry["rock"].Id;
        _shallow = registry["water_shallow"].Id;
        _deep = registry["water_deep"].Id;
    }

    public MaterialRegistry Registry => _registry;

    public bool IsGrass(TerrainCell cell) => cell.Material == _grass;

    /// <summary>The half-metre height of the water surface's top shore: land at or above it is dry.</summary>
    public int SeaLevel => (int)_seaLevel;

    private static double Step(double edge0, double edge1, double v)
    {
        var t = Math.Clamp((v - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    public TerrainCell At(int x, int y)
    {
        double fx = x, fy = y;
        var continent = _noise.Fbm(SaltContinent, fx / 700.0, fy / 700.0, 3);
        var hills = _noise.Fbm(SaltHills, fx / 90.0, fy / 90.0, 4);
        var hillMask = 0.5 + 0.5 * _noise.Sample(SaltHillMask, fx / 450.0, fy / 450.0);
        var mountain = _noise.Fbm(SaltMountain, fx / 600.0, fy / 600.0, 3);
        var peak = Math.Max(0.0, (mountain - 0.12) / 0.45);
        var natural = 3.0 + continent * 14.0 + hills * _options.Relief * (0.35 + 0.65 * hillMask) +
                      peak * peak * _options.Mountains * 48.0;

        // Rivers are shallow depressions that only exist in the lowlands: a carve no deeper than two
        // metres never turns a bank into a cliff, so a river can always be crossed.
        var lowland = 1.0 - Step(_seaLevel + 4.0, _seaLevel + 16.0, natural);
        var river = Math.Abs(_noise.Fbm(SaltRiver, fx / 320.0, fy / 320.0, 3));
        var carve = 4.0 * lowland * (1.0 - Step(0.0, 3.0 * _riverWidth, river));
        var height = (int)Math.Round(natural - carve);

        if (height < _seaLevel)
        {
            var depth = _seaLevel - height;
            // Deep water only in patches, so wading around a lake or across a river stays possible.
            var deep = depth >= 3.0 && _noise.Fbm(SaltDeep, fx / 26.0, fy / 26.0, 2) > -0.25;
            return deep ? new TerrainCell((int)_seaLevel - 3, _deep) : new TerrainCell((int)_seaLevel - 1, _shallow);
        }

        if (natural > 38.0 && _noise.Fbm(SaltOutcrop, fx / 13.0, fy / 13.0, 2) > 0.3)
            return new TerrainCell(height, _rock);

        if (height < _seaLevel + 3.0) return new TerrainCell(height, _sand);
        var moisture = _noise.Fbm(SaltMoisture, fx / 260.0, fy / 260.0, 3);
        if (natural > 30.0 || moisture < -0.25) return new TerrainCell(height, _dirt);
        if (moisture > 0.3 && height < _seaLevel + 10.0) return new TerrainCell(height, _clay);
        if (moisture > 0.15) return new TerrainCell(height, _topsoil);
        return new TerrainCell(height, _grass);
    }

    public bool Walkable(TerrainCell cell) => _registry.Get(cell.Material)?.Walkable ?? false;

    /// <summary>The standing height of a walkable tile, or null for water, rock and other blocked ground.</summary>
    public int? StandingHeight(int x, int y)
    {
        var cell = At(x, y);
        return Walkable(cell) ? cell.Height : null;
    }

    /// <summary>Strata of the chunk whose centre is at the given tile: beaches dig into sand first.</summary>
    public IReadOnlyList<Stratum> StrataAt(int x, int y) =>
        At(x, y).Material == _sand
            ? SandStrata
            : GenCanvas.DefaultStrata;

    private static readonly IReadOnlyList<Stratum> SandStrata = new[] { new Stratum(0, "sand"), new Stratum(6, "dirt"), new Stratum(24, "rock") };
}
