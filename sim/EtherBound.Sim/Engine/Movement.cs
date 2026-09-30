using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>Walking with the standing rule, one sub-step at a time (<c>engine/movement.py</c>).</summary>
public static class Movement
{
    public const double SlopeUpMultiplier = 0.6;
    public const double SlopeDownMultiplier = 0.85;
    public const double SubstepMetres = 0.05;
    public const double BodyRadiusMetres = 0.3;
    private const int BodyHeightHalfCells = 4;
    private const double OverlapTolerance = 1e-12;
    public const double FreeLoadKg = 10.0;
    public const double LoadSlowdownKg = 60.0;
    public const double MinLoadMultiplier = 0.5;

    /// <summary>Carried mass slows walking: free up to 10 kg, then linearly down to half.</summary>
    public static double LoadMultiplier(double loadKg) =>
        loadKg <= FreeLoadKg ? 1.0 : Math.Max(MinLoadMultiplier, 1.0 - (loadKg - FreeLoadKg) / LoadSlowdownKg);

    private static (int, int) Tile(double x, double y) => (PyMath.Floor(x), PyMath.Floor(y));

    public static StandingSurface? NearestSurface(WorldGrid grid, double x, double y, int h)
    {
        var (tx, ty) = Tile(x, y);
        StandingSurface? best = null;
        foreach (var surface in grid.StandingSurfaces(tx, ty))
        {
            if (Math.Abs(surface.H - h) > 1) continue;
            if (best is null || Math.Abs(surface.H - h) < Math.Abs(best.Value.H - h)) best = surface;
        }
        return best;
    }

    private static bool CanEnter(WorldGrid grid, double x, double y, int h, double nx, double ny)
    {
        var current = Tile(x, y);
        var target = Tile(nx, ny);
        return current == target || grid.CanStep(current.Item1, current.Item2, target.Item1, target.Item2, h);
    }

    private static bool TryBlockEdge(WorldGrid grid, int tileX, int tileY, int h, bool vertical,
        double current, double next, out double snapped)
    {
        var direction = Math.Sign(next - current);
        var boundary = vertical
            ? direction > 0 ? tileY + 1 : tileY
            : direction > 0 ? tileX + 1 : tileX;
        var limit = boundary - direction * BodyRadiusMetres;
        if (direction == 0 || (direction > 0 ? next <= limit : next >= limit))
        {
            snapped = next;
            return false;
        }

        var nextX = tileX + (vertical ? 0 : direction);
        var nextY = tileY + (vertical ? direction : 0);
        if (grid.CanStep(tileX, tileY, nextX, nextY, h))
        {
            snapped = next;
            return false;
        }

        snapped = limit;
        return true;
    }

    private static double BodyOverlap(double x, double y, int h, ActorRow second)
    {
        var verticalOverlap = Math.Max(0.0,
            Math.Min(h + BodyHeightHalfCells, second.H + BodyHeightHalfCells) - Math.Max(h, second.H)) * 0.5;
        if (verticalOverlap == 0) return 0;

        var distance = PyMath.Hypot(x - second.X, y - second.Y);
        var diameter = BodyRadiusMetres * 2;
        if (distance >= diameter) return 0;
        var area = distance == 0
            ? Math.PI * BodyRadiusMetres * BodyRadiusMetres
            : 2 * BodyRadiusMetres * BodyRadiusMetres * Math.Acos(distance / diameter) -
              0.5 * distance * Math.Sqrt(diameter * diameter - distance * distance);
        return area * verticalOverlap;
    }

    private static bool CanMoveBody(double currentX, double currentY, int currentH,
        double nextX, double nextY, int nextH, IReadOnlyList<ActorRow> actors)
    {
        foreach (var actor in actors)
        {
            var before = BodyOverlap(currentX, currentY, currentH, actor);
            var after = BodyOverlap(nextX, nextY, nextH, actor);
            if (before == 0 ? after > 0 : after > before + OverlapTolerance) return false;
        }
        return true;
    }

    private static bool TryBlockInterior(WorldGrid grid, int tileX, int tileY, int h, bool vertical,
        double current, double next, out double snapped)
    {
        var bit = vertical ? ChunkConst.SlotHalfV : ChunkConst.SlotHalfH;
        if ((grid.InteriorWallMaskAt(tileX, tileY, h) & bit) == 0)
        {
            snapped = next;
            return false;
        }
        var line = (vertical ? tileX : tileY) + 0.5;
        var currentSide = current - line;
        var nextSide = next - line;
        if (Math.Abs(nextSide) >= WallRegions.BodyClearance && currentSide * nextSide > 0)
        {
            snapped = next;
            return false;
        }
        var side = currentSide < 0 || currentSide == 0 && nextSide < 0 ? -1 : 1;
        snapped = line + side * WallRegions.BodyClearance;
        return true;
    }

    private static void SeparateFromInteriorWalls(WorldGrid grid, ref double x, ref double y, int h)
    {
        var (tileX, tileY) = Tile(x, y);
        var walls = grid.InteriorWallMaskAt(tileX, tileY, h);
        if ((walls & ChunkConst.SlotHalfV) != 0)
        {
            var line = tileX + 0.5;
            if (Math.Abs(x - line) < WallRegions.BodyClearance)
                x = line + (x < line ? -1 : 1) * WallRegions.BodyClearance;
        }
        if ((walls & ChunkConst.SlotHalfH) != 0)
        {
            var line = tileY + 0.5;
            if (Math.Abs(y - line) < WallRegions.BodyClearance)
                y = line + (y < line ? -1 : 1) * WallRegions.BodyClearance;
        }
    }

    private static double Multiplier(WorldGrid grid, int h, StandingSurface surface)
    {
        var cost = surface.H > h ? SlopeUpMultiplier : surface.H < h ? SlopeDownMultiplier : 1.0;
        if (grid.Registry.Get(surface.MaterialId) is { WalkCost: > 0 } material) cost /= material.WalkCost;
        return cost;
    }

    public static (double X, double Y, int H) MoveInWorld(double x, double y, int h, double dx, double dy, double distance,
        WorldGrid grid, double loadKg = 0.0, IReadOnlyList<ActorRow>? otherActors = null)
    {
        var magnitude = PyMath.Hypot(dx, dy);
        if (magnitude == 0 || distance <= 0) return (x, y, h);
        distance *= LoadMultiplier(loadKg);
        var actors = otherActors?.OrderBy(actor => actor.Id, StringComparer.Ordinal).ToArray() ?? Array.Empty<ActorRow>();
        double ux = dx / magnitude, uy = dy / magnitude;
        var remaining = distance;
        var blockedX = ux == 0;
        var blockedY = uy == 0;
        while (remaining > 0)
        {
            SeparateFromInteriorWalls(grid, ref x, ref y, h);
            var step = Math.Min(SubstepMetres, remaining);
            var moved = false;
            var spent = 0.0;
            if (!blockedX)
            {
                var source = Tile(x, y);
                var peekX = x + ux * step;
                if (TryBlockInterior(grid, source.Item1, source.Item2, h, true, x, peekX, out var interiorX))
                {
                    x = interiorX;
                    blockedX = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
                else if (TryBlockEdge(grid, source.Item1, source.Item2, h, false, x, peekX, out var edgeX))
                {
                    x = edgeX;
                    blockedX = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
                else if (CanEnter(grid, x, y, h, peekX, y))
                {
                    if (NearestSurface(grid, peekX, y, h) is { } surface)
                    {
                        var multiplier = Multiplier(grid, h, surface);
                        var nextX = x + ux * step;
                        var nextH = Tile(nextX, y) != source ? surface.H : h;
                        if (CanMoveBody(x, y, h, nextX, y, nextH, actors))
                        {
                            x = nextX;
                            h = nextH;
                            SeparateFromInteriorWalls(grid, ref x, ref y, h);
                            moved = true;
                            spent = Math.Max(spent, step / multiplier);
                        }
                    }
                }
                else if (Tile(peekX, y) != Tile(x, y))
                {
                    var boundary = Math.Floor(x) + (ux > 0 ? 1 : 0);
                    x = ux > 0 ? boundary - BodyRadiusMetres : boundary + BodyRadiusMetres;
                    blockedX = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
            }
            if (!blockedY)
            {
                var source = Tile(x, y);
                var peekY = y + uy * step;
                if (TryBlockInterior(grid, source.Item1, source.Item2, h, false, y, peekY, out var interiorY))
                {
                    y = interiorY;
                    blockedY = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
                else if (TryBlockEdge(grid, source.Item1, source.Item2, h, true, y, peekY, out var edgeY))
                {
                    y = edgeY;
                    blockedY = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
                else if (CanEnter(grid, x, y, h, x, peekY))
                {
                    if (NearestSurface(grid, x, peekY, h) is { } surface)
                    {
                        var multiplier = Multiplier(grid, h, surface);
                        var nextY = y + uy * step;
                        var nextH = Tile(x, nextY) != source ? surface.H : h;
                        if (CanMoveBody(x, y, h, x, nextY, nextH, actors))
                        {
                            y = nextY;
                            h = nextH;
                            SeparateFromInteriorWalls(grid, ref x, ref y, h);
                            moved = true;
                            spent = Math.Max(spent, step / multiplier);
                        }
                    }
                }
                else if (Tile(x, peekY) != Tile(x, y))
                {
                    var boundary = Math.Floor(y) + (uy > 0 ? 1 : 0);
                    y = uy > 0 ? boundary - BodyRadiusMetres : boundary + BodyRadiusMetres;
                    blockedY = true;
                    moved = true;
                    spent = Math.Max(spent, step);
                }
            }
            if (!moved) break;
            remaining -= spent > 0 ? spent : step;
            if (blockedX && blockedY) break;
        }
        return (x, y, h);
    }
}

/// <summary>SI physics constants and formulas (<c>engine/physics.py</c>).</summary>
public static class Physics
{
    public const double Gravity = 9.81;
    public const double RollingFriction = 0.05;
    public const double ShoveSpeed = 2.0;
    public const double MaxThrowSpeed = 8.0;
    public const double MaxThrowEnergy = 100.0;
    public const double HandEffectiveMass = 2.0;
    public const double HandSpeed = 5.0;
    public const double ToolSpeed = 15.0;
    public const int MaxTileSteps = 256;

    // `speed ** 2` is C pow(); both runtimes call the same CRT on Windows.
    public static double Kinetic(double mass, double speed) => 0.5 * mass * Math.Pow(speed, 2);

    public static double Potential(double mass, double heightM) => mass * Gravity * heightM;

    public static double IntegrityCapacity(double resistance, int heightCells) => resistance * Math.Max(1, heightCells);

    public static (double Absorbed, double Left) Absorb(double energy, double integrity)
    {
        var absorbed = Math.Min(energy, integrity);
        return (absorbed, Math.Max(0.0, energy - absorbed));
    }

    public static double ShoveImpulse(double actorMass, double targetMass) =>
        actorMass * targetMass / (actorMass + targetMass) * ShoveSpeed;

    public static double ThrowSpeed(double mass) => Math.Min(MaxThrowSpeed, Math.Sqrt(2 * MaxThrowEnergy / mass));

    public static double Strike(double? toolMass, double strikeSpeed = ToolSpeed) =>
        toolMass is null ? Kinetic(HandEffectiveMass, HandSpeed) : Kinetic(toolMass.Value, strikeSpeed);

    public static double TravelCost(double mass) => RollingFriction * mass * Gravity;
}
