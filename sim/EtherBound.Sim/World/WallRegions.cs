namespace EtherBound.Sim.World;

/// <summary>Connected walkable parts of a tile split by its interior H/V wall slots.</summary>
public static class WallRegions
{
    public const byte NorthWest = 1;
    public const byte NorthEast = 2;
    public const byte SouthWest = 4;
    public const byte SouthEast = 8;
    public const byte AllQuadrants = NorthWest | NorthEast | SouthWest | SouthEast;

    public const byte NorthEdge = NorthWest | NorthEast;
    public const byte SouthEdge = SouthWest | SouthEast;
    public const byte WestEdge = NorthWest | SouthWest;
    public const byte EastEdge = NorthEast | SouthEast;
    public const byte FirstPort = 1;
    public const byte SecondPort = 2;

    // 0.3 m body radius + 0.125 m half-wall + 0.001 m tolerance.
    public const double BodyClearance = 0.426;

    private static readonly Region[][] Layouts =
    {
        new[] { new Region(0, AllQuadrants) },
        new[] { new Region(1, NorthEdge), new Region(2, SouthEdge) },
        new[] { new Region(1, WestEdge), new Region(2, EastEdge) },
        new[]
        {
            new Region(1, NorthWest), new Region(2, NorthEast),
            new Region(3, SouthWest), new Region(4, SouthEast),
        },
    };

    public readonly record struct Region(byte Id, byte Quadrants);

    public static IReadOnlyList<Region> For(byte slotMask) => Layouts[LayoutIndex(slotMask)];

    public static byte At(byte slotMask, double localX, double localY)
    {
        var quadrant = localX < 0.5
            ? (byte)(localY < 0.5 ? NorthWest : SouthWest)
            : (byte)(localY < 0.5 ? NorthEast : SouthEast);
        foreach (var region in For(slotMask))
            if ((region.Quadrants & quadrant) != 0) return region.Id;
        return 0;
    }

    public static byte Quadrants(byte slotMask, byte region)
    {
        foreach (var candidate in For(slotMask))
            if (candidate.Id == region) return candidate.Quadrants;
        return 0;
    }

    public static bool Touches(byte slotMask, byte region, byte edge) =>
        (region == 0 && LayoutIndex(slotMask) != 0) || (Quadrants(slotMask, region) & edge) != 0;

    /// <summary>Aligned half-edge ports shared by a source and destination region.</summary>
    public static byte SharedPorts(byte sourceMask, byte sourceRegion, byte targetMask, byte targetRegion, int dx, int dy)
    {
        var source = sourceRegion == 0 ? AllQuadrants : Quadrants(sourceMask, sourceRegion);
        var target = targetRegion == 0 ? AllQuadrants : Quadrants(targetMask, targetRegion);
        var (sourceFirst, sourceSecond, targetFirst, targetSecond) = (dx, dy) switch
        {
            (1, 0) => (NorthEast, SouthEast, NorthWest, SouthWest),
            (-1, 0) => (NorthWest, SouthWest, NorthEast, SouthEast),
            (0, 1) => (SouthWest, SouthEast, NorthWest, NorthEast),
            (0, -1) => (NorthWest, NorthEast, SouthWest, SouthEast),
            _ => throw new ArgumentException("region ports require an orthogonal direction"),
        };
        byte ports = 0;
        if ((source & sourceFirst) != 0 && (target & targetFirst) != 0) ports |= FirstPort;
        if ((source & sourceSecond) != 0 && (target & targetSecond) != 0) ports |= SecondPort;
        return ports;
    }

    public static IEnumerable<byte> AtEdge(byte slotMask, byte edge)
    {
        foreach (var region in For(slotMask))
            if ((region.Quadrants & edge) != 0) yield return region.Id;
    }

    public static (double X, double Y) Waypoint(byte slotMask, byte region)
    {
        var quadrants = Quadrants(slotMask, region);
        if (quadrants == 0 || LayoutIndex(slotMask) == 0) return (0.5, 0.5);
        var x = (slotMask & ChunkConst.SlotHalfV) == 0
            ? 0.5
            : (quadrants & WestEdge) != 0 ? 0.5 - BodyClearance : 0.5 + BodyClearance;
        var y = (slotMask & ChunkConst.SlotHalfH) == 0
            ? 0.5
            : (quadrants & NorthEdge) != 0 ? 0.5 - BodyClearance : 0.5 + BodyClearance;
        return (x, y);
    }

    private static int LayoutIndex(byte slotMask) =>
        ((slotMask & ChunkConst.SlotHalfH) != 0 ? 1 : 0) |
        ((slotMask & ChunkConst.SlotHalfV) != 0 ? 2 : 0);
}
