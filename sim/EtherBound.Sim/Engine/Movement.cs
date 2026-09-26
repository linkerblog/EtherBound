using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>Walking with the standing rule, one sub-step at a time (<c>engine/movement.py</c>).</summary>
public static class Movement
{
    public const double SlopeUpMultiplier = 0.6;
    public const double SlopeDownMultiplier = 0.85;
    public const double SubstepMetres = 0.05;
    public const double BodyRadiusMetres = 0.3;
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

    private static double Multiplier(WorldGrid grid, int h, StandingSurface surface)
    {
        var cost = surface.H > h ? SlopeUpMultiplier : surface.H < h ? SlopeDownMultiplier : 1.0;
        if (grid.Registry.Get(surface.MaterialId) is { WalkCost: > 0 } material) cost /= material.WalkCost;
        return cost;
    }

    public static (double X, double Y, int H) MoveInWorld(double x, double y, int h, double dx, double dy, double distance,
        WorldGrid grid, double loadKg = 0.0)
    {
        var magnitude = PyMath.Hypot(dx, dy);
        if (magnitude == 0 || distance <= 0) return (x, y, h);
        distance *= LoadMultiplier(loadKg);
        double ux = dx / magnitude, uy = dy / magnitude;
        var remaining = distance;
        var blockedX = ux == 0;
        var blockedY = uy == 0;
        while (remaining > 0)
        {
            var step = Math.Min(SubstepMetres, remaining);
            var moved = false;
            var spent = 0.0;
            if (!blockedX)
            {
                var source = Tile(x, y);
                var peekX = x + ux * step;
                if (CanEnter(grid, x, y, h, peekX, y))
                {
                    if (NearestSurface(grid, peekX, y, h) is { } surface)
                    {
                        var multiplier = Multiplier(grid, h, surface);
                        x += ux * step;
                        if (Tile(x, y) != source) h = surface.H;
                        moved = true;
                        spent = Math.Max(spent, step / multiplier);
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
                if (CanEnter(grid, x, y, h, x, peekY))
                {
                    if (NearestSurface(grid, x, peekY, h) is { } surface)
                    {
                        var multiplier = Multiplier(grid, h, surface);
                        y += uy * step;
                        if (Tile(x, y) != source) h = surface.H;
                        moved = true;
                        spent = Math.Max(spent, step / multiplier);
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
