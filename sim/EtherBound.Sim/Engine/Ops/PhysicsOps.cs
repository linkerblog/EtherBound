using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using static EtherBound.Sim.Engine.ObjectHelpers;

namespace EtherBound.Sim.Engine.Ops;

/// <summary>Wall-edge helpers (<c>ops/edges.py</c>).</summary>
internal static class Edges
{
    public static int Sign(int value) => (value > 0 ? 1 : 0) - (value < 0 ? 1 : 0);

    public static bool Cardinal(int dx, int dy) => Math.Abs(dx) + Math.Abs(dy) == 1;

    public static EdgeTarget Canonical(EdgeTarget t) => t.Direction switch
    {
        "south" => t with { Y = t.Y + 1, Direction = "north" },
        "east" => t with { X = t.X + 1, Direction = "west" },
        _ => t,
    };

    public static (ChunkLevel Level, int Index, int MaterialId)? Cell(ActionContext ctx, EdgeTarget target)
    {
        var edge = Canonical(target);
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(edge.X, edge.Y);
        if (ctx.Grid.Level(cx, cy, edge.Z) is not { } level) return null;
        var index = Chunk.Index(lx, ly);
        int material = edge.Direction switch
        {
            "north" => level.WallN[index],
            "west" => level.WallW[index],
            WallSlots.HalfH or WallSlots.HalfV => (level.SlotMask[index] & WallSlots.Bit(edge.Direction)) != 0
                ? level.SlotMat[index]
                : 0,
            _ => 0,
        };
        return material == 0 ? null : (level, index, material);
    }

    public static int WallBottom(ActionContext ctx, ChunkLevel level, int index)
    {
        int bottom = level.FloorH[index];
        if (bottom != ChunkConst.NoFloor) return bottom;
        if (ctx.Grid.Chunk(level.Cx, level.Cy) is not { } chunk) return level.Z * 6;
        var limit = (level.Z + 1) * 6;
        var supports = new List<int> { chunk.GroundH[index] };
        supports.AddRange(ctx.Grid.Levels.Values
            .Where(l => l.Cx == level.Cx && l.Cy == level.Cy && l.Z < level.Z && l.FloorH[index] != ChunkConst.NoFloor)
            .Select(l => (int)l.FloorH[index]));
        var below = supports.Where(s => s < limit).ToList();
        return below.Count > 0 ? below.Max() : level.Z * 6;
    }

    public static bool Reachable(ActionContext ctx, EdgeTarget target)
    {
        var edge = Canonical(target);
        if (Cell(ctx, edge) is not { } record) return false;
        var bottom = WallBottom(ctx, record.Level, record.Index);
        if (edge.Direction is WallSlots.HalfH or WallSlots.HalfV)
            return Reach.InCloseReach(ctx, edge.X, edge.Y) && bottom <= ctx.Actor.H + Reach.UpH && bottom + 6 >= ctx.Actor.H - Reach.DownH;
        var (ax, ay) = ctx.ActorTile;
        var sides = edge.Direction == "north"
            ? new[] { (edge.X, edge.Y), (edge.X, edge.Y - 1) }
            : new[] { (edge.X, edge.Y), (edge.X - 1, edge.Y) };
        return sides.Contains((ax, ay)) && bottom <= ctx.Actor.H + 3 && bottom + 6 >= ctx.Actor.H - 2;
    }

    public static EdgeTarget ForStep(int x, int y, int z, int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => new EdgeTarget(x, y, z, "north"),
        (0, 1) => new EdgeTarget(x, y + 1, z, "north"),
        (-1, 0) => new EdgeTarget(x, y, z, "west"),
        _ => new EdgeTarget(x + 1, y, z, "west"),
    };

    public static EdgeTarget? BlockingStep(ActionContext ctx, int x, int y, int h, int dx, int dy)
    {
        if (!ctx.Grid.WallBetween(x, y, x + dx, y + dy, h)) return null;
        var edge = Canonical(ForStep(x, y, PyMath.FloorDiv(h, 6), dx, dy));
        var (cx, cy, lx, ly) = WorldGrid.ChunkCoords(edge.X, edge.Y);
        if (ctx.Grid.Chunk(cx, cy) is null) return edge;
        var index = ly * ChunkConst.Size + lx;
        foreach (var level in ctx.Grid.Levels.Values.Where(l => (l.Cx, l.Cy) == (cx, cy)).OrderBy(l => l.Z))
        {
            int material = edge.Direction == "north" ? level.WallN[index] : level.WallW[index];
            if (material == 0) continue;
            var doorway = edge.Direction == "north" ? ChunkConst.EdgeNDoorway : ChunkConst.EdgeWDoorway;
            if ((level.EdgeFlags[index] & doorway) != 0) continue;
            var bottom = WallBottom(ctx, level, index);
            if (bottom < h + 4 && bottom + 6 > h) return edge with { Z = level.Z };
        }
        return edge;
    }

    public static int? SurfaceH(WorldGrid grid, int x, int y, int currentH, bool body)
    {
        var surfaces = body ? grid.StandingSurfaces(x, y) : grid.RestingSurfaces(x, y);
        var candidates = surfaces.Where(s => s.H <= currentH + 1).Select(s => s.H).ToList();
        return candidates.Count > 0 ? candidates.Max() : null;
    }
}

/// <summary>Mutable accumulators of one physics resolution.</summary>
internal sealed class PhysicsLog
{
    public List<SimEvent> Events { get; } = new();
    public JsonArray Damage { get; } = new();
    public JsonArray Broken { get; } = new();
    public HashSet<(int, int)> ChangedChunks { get; } = new();
}

/// <summary>Material damage to objects and wall edges (<c>ops/damage.py</c>).</summary>
internal static class Damage
{
    private static (int, int) ChunkOf(int x, int y) => (PyMath.FloorDiv(x, ChunkConst.Size), PyMath.FloorDiv(y, ChunkConst.Size));

    public static double Object(ActionContext ctx, string op, ObjectRow row, double energy, PhysicsLog log, bool emitImpact = true)
    {
        var kind = ctx.Grid.Catalog[row.Kind];
        var material = ctx.Grid.Registry[kind.Material];
        var capacity = Physics.IntegrityCapacity(material.Resistance, kind.Height) * row.Quantity;
        var remaining = row.Integrity ?? capacity;
        var (absorbed, left) = Physics.Absorb(energy, remaining);
        if (emitImpact) log.Events.Add(SimEvent.Impact(ctx.Actor.Id, Json.Obj(("kind", "object"), ("id", row.Id)), energy));
        var record = Json.Obj(("target", Json.Obj(("kind", "object"), ("id", row.Id))), ("absorbed_j", absorbed),
            ("remaining_j", Math.Max(0.0, remaining - absorbed)));
        if (left > 0 || energy >= remaining)
        {
            int x = row.X!.Value, y = row.Y!.Value, h = row.H!.Value;
            int? rubbleId;
            if (row.Kind == "rubble")
            {
                ctx.Session.DeleteObject(row);
                rubbleId = null;
                log.Events.Add(SimEvent.ObjectChanged(ctx.Actor.Id, row.Id, row.Kind, op, Json.Obj(("removed", true))));
            }
            else
            {
                foreach (var child in Children(ctx.Session, row.Id))
                {
                    var old = LocationOf(child);
                    SetTile(child, x, y, h);
                    log.Events.Add(SimEvent.ObjectMoved(ctx.Actor.Id, child.Id, child.Kind, child.Quantity, op, old, LocationOf(child)));
                }
                row.Kind = "rubble";
                row.State = new JsonObject();
                row.Integrity = null;
                rubbleId = row.Id;
                log.Events.Add(SimEvent.ObjectChanged(ctx.Actor.Id, row.Id, "rubble", op, Json.Obj(("kind", "rubble"), ("integrity", null))));
            }
            log.Broken.Add(Json.Obj(("kind", "object"), ("id", row.Id), ("rubble_id", rubbleId)));
            record["remaining_j"] = 0.0;
            log.ChangedChunks.Add(ChunkOf(x, y));
            var (cx, cy) = ChunkOf(x, y);
            RefreshChunkObjects(ctx, cx, cy);
        }
        else
        {
            row.Integrity = remaining - absorbed;
            log.Events.Add(SimEvent.ObjectChanged(ctx.Actor.Id, row.Id, row.Kind, op, Json.Obj(("integrity", row.Integrity))));
            if (row.X is not null && row.Y is not null) log.ChangedChunks.Add(ChunkOf(row.X.Value, row.Y.Value));
        }
        log.Damage.Add(record);
        return left;
    }

    public static double WallWith(ActionContext ctx, string op, EdgeTarget target, double energy, PhysicsLog log)
    {
        var edge = Edges.Canonical(target);
        if (Edges.Cell(ctx, edge) is not { } record) return 0.0;
        var (level, index, materialId) = record;
        if (ctx.Grid.Registry.Get(materialId) is not { } material) return 0.0;
        var key = new WallKey(level.Cx, level.Cy, level.Z, index, edge.Direction);
        var stored = ctx.Session.GetWall(key);
        var capacity = Physics.IntegrityCapacity(material.Resistance, 6);
        var remaining = stored ?? capacity;
        var (absorbed, left) = Physics.Absorb(energy, remaining);
        log.Events.Add(SimEvent.Impact(ctx.Actor.Id, edge.ToJson(), energy));
        var data = Json.Obj(("target", edge.ToJson()), ("absorbed_j", absorbed), ("remaining_j", Math.Max(0.0, remaining - absorbed)));
        if (energy >= remaining)
        {
            if (stored is not null) ctx.Session.DeleteWall(key);
            var mask = (byte[])level.SlotMask.Clone();
            mask[index] &= (byte)~WallSlots.Bit(edge.Direction);
            ChunkLevel updated;
            if (edge.Direction is WallSlots.HalfH or WallSlots.HalfV)
            {
                var materials = (ushort[])level.SlotMat.Clone();
                if ((mask[index] & (ChunkConst.SlotHalfH | ChunkConst.SlotHalfV | ChunkConst.SlotDiag1 | ChunkConst.SlotDiag2)) == 0)
                    materials[index] = 0;
                updated = level.With(slotMask: mask, slotMat: materials);
            }
            else
            {
                var walls = (ushort[])(edge.Direction == "north" ? level.WallN : level.WallW).Clone();
                walls[index] = 0;
                updated = edge.Direction == "north"
                    ? level.With(wallN: walls, slotMask: mask)
                    : level.With(wallW: walls, slotMask: mask);
            }
            ctx.Grid.AddLevel(updated);
            ctx.Session.MarkLevel(level.Cx, level.Cy, level.Z);
            var broken = Json.Obj(("kind", "wall"));
            foreach (var (k, v) in edge.ToJson()) broken[k] = v?.DeepClone();
            log.Broken.Add(broken);
            data["remaining_j"] = 0.0;
            log.ChangedChunks.Add((level.Cx, level.Cy));
        }
        else
        {
            ctx.Session.SetWall(key, remaining - absorbed);
        }
        log.Damage.Add(data);
        return left;
    }
}

/// <summary>Tile-by-tile travel of a pushed, thrown or struck body (<c>ops/travel.py</c>).</summary>
internal static class Travel
{
    public static List<PhysicsPosition> Run(ActionContext ctx, string op, object mover, int dx, int dy, double energy, double mass, PhysicsLog log)
    {
        var isBody = mover is ActorRow;
        int x, y, h;
        string entityKind;
        PhysicsPosition Point(int px, int py, int ph) => mover is ActorRow a
            ? PhysicsPosition.Actor(a.Id, px, py, ph)
            : PhysicsPosition.Object(((ObjectRow)mover).Id, px, py, ph);
        JsonNode EntityId() => mover is ActorRow a ? JsonValue.Create(a.Id) : JsonValue.Create(((ObjectRow)mover).Id);
        if (mover is ActorRow actor)
        {
            (x, y, h) = (actor.TileX, actor.TileY, actor.H);
            entityKind = "actor";
        }
        else
        {
            var obj = (ObjectRow)mover;
            (x, y, h) = (obj.X!.Value, obj.Y!.Value, obj.H!.Value);
            entityKind = "object";
        }
        var path = new List<PhysicsPosition> { Point(x, y, h) };
        for (var step = 0; step < Physics.MaxTileSteps; step++)
        {
            var cost = Physics.TravelCost(mass);
            if (energy < cost) break;
            int nx = x + dx, ny = y + dy;
            if (Edges.BlockingStep(ctx, x, y, h, dx, dy) is { } wall)
            {
                energy = Damage.WallWith(ctx, op, wall, energy, log);
                if (energy <= 0) break;
                if (Edges.BlockingStep(ctx, x, y, h, dx, dy) is not null) break;
            }
            if (SolidObjectAt(ctx, nx, ny, h, mover) is { } obstacle)
            {
                energy = Damage.Object(ctx, op, obstacle, energy, log);
                if (energy <= 0) break;
                if (SolidObjectAt(ctx, nx, ny, h, mover) is not null) break;
            }
            if (ActorAt(ctx, nx, ny, h, mover) is { } other)
            {
                var target = Json.Obj(("kind", "actor"), ("id", other.Id));
                log.Events.Add(SimEvent.Impact(ctx.Actor.Id, target, energy));
                log.Damage.Add(Json.Obj(("target", target.DeepClone()), ("impact_j", energy)));
                break;
            }
            var moverHeight = isBody ? 3 : ctx.Grid.Catalog[((ObjectRow)mover).Kind].Height;
            if (TerrainBlocks(ctx.Grid, nx, ny, h, moverHeight))
            {
                var target = Json.Obj(("kind", "terrain"), ("x", nx), ("y", ny), ("h", h));
                log.Events.Add(SimEvent.Impact(ctx.Actor.Id, target, energy));
                log.Damage.Add(Json.Obj(("target", target.DeepClone()), ("impact_j", energy)));
                break;
            }
            if (Edges.SurfaceH(ctx.Grid, nx, ny, h, isBody) is not { } nextH) break;
            var fall = Math.Max(0.0, (h - nextH) * 0.5);
            (x, y, h) = (nx, ny, nextH);
            path.Add(Point(x, y, h));
            energy -= cost;
            if (fall != 0)
            {
                var fallEnergy = Physics.Potential(mass, fall);
                if (!isBody || fall > 3.0)
                {
                    var target = Json.Obj(("kind", entityKind), ("id", EntityId()));
                    log.Events.Add(SimEvent.Impact(ctx.Actor.Id, target, fallEnergy));
                    log.Damage.Add(Json.Obj(("target", target.DeepClone()), ("fall_j", fallEnergy)));
                    if (!isBody)
                    {
                        var obj = (ObjectRow)mover;
                        SetTile(obj, x, y, h);
                        var cx = PyMath.FloorDiv(x, ChunkConst.Size);
                        var cy = PyMath.FloorDiv(y, ChunkConst.Size);
                        log.ChangedChunks.Add((cx, cy));
                        RefreshChunkObjects(ctx, cx, cy);
                        Damage.Object(ctx, op, obj, fallEnergy, log, emitImpact: false);
                    }
                }
            }
            if (energy <= 0) break;
        }
        return path;
    }

    private static bool TerrainBlocks(WorldGrid grid, int x, int y, int h, int height)
    {
        for (var offset = 1; offset <= height; offset++)
            if (grid.TerrainSolidAt(x, y, h + offset)) return true;
        return false;
    }

    private static ObjectRow? SolidObjectAt(ActionContext ctx, int x, int y, int h, object mover)
    {
        foreach (var row in ctx.Session.Objects().Where(o => o.Loc == "tile" && o.X == x && o.Y == y))
        {
            if (mover is ObjectRow m && row.Id == m.Id) continue;
            var kind = ctx.Grid.Catalog[row.Kind];
            var moverHeight = mover is ActorRow ? 3 : Math.Max(ctx.Grid.Catalog[((ObjectRow)mover).Kind].Height, 1);
            if (kind.Solid && row.H is not null && row.H < h + moverHeight && row.H + kind.Height > h) return row;
        }
        return null;
    }

    private static ActorRow? ActorAt(ActionContext ctx, int x, int y, int h, object mover)
    {
        foreach (var actor in ctx.Session.ActorsOn(x, y))
        {
            if (mover is ActorRow m && actor.Id == m.Id) continue;
            if (actor.H == h) return actor;
        }
        return null;
    }
}

/// <summary>Force and material damage, resolved atomically in the action (<c>ops/physics.py</c>).</summary>
public sealed class PhysicsOp : OpHandler
{
    private static readonly (int, int)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };
    private static readonly Dictionary<(int, int), string> DirectionNames = new()
    {
        [(0, -1)] = "north", [(1, 0)] = "east", [(0, 1)] = "south", [(-1, 0)] = "west",
    };

    public PhysicsOp(string op) => Op = op;

    public override string Op { get; }
    public override bool ChangesLoad => Op == "throw";

    private bool Moves => Op is "push" or "pull" or "drag";

    private static ObjectRow? Obj(ActionContext ctx, ObjectTarget target) => ctx.Session.GetObject(target.Id);

    private static ActorRow? Actor(ActionContext ctx, ActorTarget target) =>
        ctx.Session.GetActor(target.Id) is { } row && row.Id != ctx.Actor.Id && row.MassKg > 0 ? row : null;

    private static (int X, int Y, int H)? TargetTile(ActionContext ctx, Target target) => target switch
    {
        ObjectTarget o when Obj(ctx, o) is { Loc: "tile", X: not null, Y: not null, H: not null } row => (row.X.Value, row.Y.Value, row.H.Value),
        ActorTarget a when Actor(ctx, a) is { } actor => (actor.TileX, actor.TileY, actor.H),
        _ => null,
    };

    public override bool Applies(ActionContext ctx, Target target)
    {
        if (Op == "throw") return target is ObjectTarget o && Obj(ctx, o) is not null;
        if (Moves)
            return target switch
            {
                ObjectTarget o => Obj(ctx, o) is { Loc: "tile" },
                ActorTarget a => Actor(ctx, a) is not null,
                _ => false,
            };
        return target switch
        {
            EdgeTarget e => Edges.Cell(ctx, e) is not null && Op is "hit" or "break",
            ObjectTarget o => Obj(ctx, o) is { Loc: "tile" } && Op is "hit" or "break",
            ActorTarget a => Actor(ctx, a) is not null && Op == "hit",
            _ => false,
        };
    }

    public override IReadOnlyList<GameAction> Builds(ActionContext ctx, Target target)
    {
        if (Op == "throw")
        {
            var row = Obj(ctx, (ObjectTarget)target);
            if (row is null || row.Loc != "held" || row.ActorId != ctx.Actor.Id) return Array.Empty<GameAction>();
            return Directions.Select(d => GameAction.Vector("throw", target, d.Item1, d.Item2)).ToList();
        }
        if (Moves)
        {
            if (target is not (ObjectTarget or ActorTarget) || TargetTile(ctx, target) is not { } position) return Array.Empty<GameAction>();
            var (ax, ay) = ctx.ActorTile;
            int dx = Edges.Sign(position.X - ax), dy = Edges.Sign(position.Y - ay);
            if (Math.Abs(position.X - ax) + Math.Abs(position.Y - ay) != 1) return Array.Empty<GameAction>();
            if (Op is "pull" or "drag") (dx, dy) = (-dx, -dy);
            return new[] { GameAction.Vector(Op, target, dx, dy) };
        }
        var tools = new List<ObjectTarget?> { null };
        tools.AddRange(ctx.Session.Objects()
            .Where(o => o.Loc == "held" && o.ActorId == ctx.Actor.Id && ctx.Grid.Catalog[o.Kind].Tool?.StrikeSpeedMS is not null)
            .Select(o => (ObjectTarget?)new ObjectTarget(o.Id)));
        if (Op == "hit" && target is ObjectTarget or ActorTarget or EdgeTarget) return tools.Select(t => GameAction.Strike("hit", target, t)).ToList();
        if (Op == "break" && target is ObjectTarget or EdgeTarget) return tools.Select(t => GameAction.Strike("break", target, t)).ToList();
        return Array.Empty<GameAction>();
    }

    public override string? Subject(ActionContext ctx, GameAction action)
    {
        string? label;
        switch (action.Target)
        {
            case ObjectTarget o:
                label = Obj(ctx, o) is { } row ? (row.Quantity > 1 ? $"{ctx.Grid.Catalog[row.Kind].Name} ×{row.Quantity}" : ctx.Grid.Catalog[row.Kind].Name) : null;
                break;
            case ActorTarget a:
                label = Actor(ctx, a) is { } actor ? Reach.ActorName(actor) : null;
                break;
            default:
                var record = Edges.Cell(ctx, (EdgeTarget)action.Target!);
                var material = record is { } r ? ctx.Grid.Registry.Get(r.MaterialId) : null;
                label = material is not null ? $"{material.Name} wall" : "wall";
                break;
        }
        if (Moves || Op == "throw") return label is not null ? $"{label} {DirectionNames[(action.IntDx, action.IntDy)]}" : null;
        if (action.Tool is { } tool && Obj(ctx, tool) is { } toolRow)
            return label is not null ? $"{label} with {ctx.Grid.Catalog[toolRow.Kind].Name}" : null;
        return label;
    }

    public override string? Validate(ActionContext ctx, GameAction action)
    {
        if (ctx.Actor.MassKg <= 0) return "invalid body mass";
        var target = action.Target!;
        if (Moves)
        {
            if (!Edges.Cardinal(action.IntDx, action.IntDy)) return "choose one direction";
            if (TargetTile(ctx, target) is not { } position) return "nothing there";
            var (ax, ay) = ctx.ActorTile;
            if (Math.Abs(position.X - ax) + Math.Abs(position.Y - ay) != 1) return "out of reach";
            var expected = (Edges.Sign(position.X - ax), Edges.Sign(position.Y - ay));
            if (Op is "pull" or "drag") expected = (-expected.Item1, -expected.Item2);
            if ((action.IntDx, action.IntDy) != expected) return "cannot move it that way";
            if (target is ObjectTarget o)
            {
                if (Obj(ctx, o) is not { Loc: "tile" } row) return "nothing there";
                if (ctx.Grid.Catalog[row.Kind].Fixed) return "fixed in place";
                return Reach.Object(ctx, row) ? null : "out of reach";
            }
            if (Actor(ctx, (ActorTarget)target) is not { } other) return "nobody there";
            if (!Reach.InCloseReach(ctx, other.TileX, other.TileY) || !Reach.WithinHeight(ctx, other.H)) return "out of reach";
            return null;
        }
        if (Op == "throw")
        {
            if (!Edges.Cardinal(action.IntDx, action.IntDy)) return "choose one direction";
            var row = Obj(ctx, (ObjectTarget)target);
            return row is null || row.Loc != "held" || row.ActorId != ctx.Actor.Id ? "not holding it" : null;
        }
        if (action.Tool is { } tool)
        {
            var toolRow = Obj(ctx, tool);
            if (toolRow is null || toolRow.Loc != "held" || toolRow.ActorId != ctx.Actor.Id) return "not holding the tool";
            if (ctx.Grid.Catalog[toolRow.Kind].Tool?.StrikeSpeedMS is null) return "not a striking tool";
        }
        switch (target)
        {
            case EdgeTarget e:
                if (Edges.Cell(ctx, e) is null) return "nothing there";
                return Edges.Reachable(ctx, e) ? null : "out of reach";
            case ObjectTarget o:
                if (Obj(ctx, o) is not { Loc: "tile" } row) return "nothing there";
                return Reach.Object(ctx, row) ? null : "out of reach";
        }
        if (Actor(ctx, (ActorTarget)target) is not { } victim) return "nobody there";
        var (px, py) = ctx.ActorTile;
        if (Math.Abs(victim.TileX - px) + Math.Abs(victim.TileY - py) != 1) return "out of reach";
        if (!Reach.InCloseReach(ctx, victim.TileX, victim.TileY) || !Reach.WithinHeight(ctx, victim.H)) return "out of reach";
        return null;
    }

    public override Resolution Resolve(ActionContext ctx, GameAction action)
    {
        var log = new PhysicsLog();
        var trajectory = new List<PhysicsPosition>();
        object? moved = null;
        Location? original = null;
        var target = action.Target!;

        if (Op == "throw")
        {
            var row = Obj(ctx, (ObjectTarget)target)!;
            original = LocationOf(row);
            var (x, y) = ctx.ActorTile;
            SetTile(row, x, y, ctx.Actor.H);
            moved = row;
            log.ChangedChunks.Add((PyMath.FloorDiv(x, ChunkConst.Size), PyMath.FloorDiv(y, ChunkConst.Size)));
            var mass = TotalMass(ctx.Session, ctx.Grid.Catalog, row);
            var energy = Physics.Kinetic(mass, Physics.ThrowSpeed(mass));
            trajectory = Travel.Run(ctx, Op, row, action.IntDx, action.IntDy, energy, mass, log);
        }
        else if (Moves)
        {
            double mass;
            if (target is ObjectTarget o)
            {
                var row = Obj(ctx, o)!;
                moved = row;
                original = LocationOf(row);
                mass = TotalMass(ctx.Session, ctx.Grid.Catalog, row);
            }
            else
            {
                var other = Actor(ctx, (ActorTarget)target)!;
                moved = other;
                mass = other.MassKg;
            }
            var impulse = Physics.ShoveImpulse(ctx.Actor.MassKg, mass);
            var energy = Physics.Kinetic(mass, impulse / mass);
            trajectory = Travel.Run(ctx, Op, moved, action.IntDx, action.IntDy, energy, mass, log);
        }
        else
        {
            var energy = Strike(ctx, action.Tool);
            switch (target)
            {
                case EdgeTarget e:
                    Damage.WallWith(ctx, Op, e, energy, log);
                    break;
                case ObjectTarget o:
                    Damage.Object(ctx, Op, Obj(ctx, o)!, energy, log);
                    break;
                default:
                    var other = Actor(ctx, (ActorTarget)target)!;
                    var (ax, ay) = ctx.ActorTile;
                    trajectory = Travel.Run(ctx, Op, other, Edges.Sign(other.TileX - ax), Edges.Sign(other.TileY - ay), energy, other.MassKg, log);
                    moved = other;
                    break;
            }
        }

        if (moved is not null && trajectory.Count > 0)
        {
            var last = trajectory[^1];
            int lx = (int)last.X, ly = (int)last.Y;
            if (moved is ObjectRow movedRow)
            {
                if (original is not TileLoc || (movedRow.X, movedRow.Y, movedRow.H) != (lx, ly, last.H))
                {
                    var from = original ?? LocationOf(movedRow);
                    SetTile(movedRow, lx, ly, last.H);
                    log.Events.Add(SimEvent.ObjectMoved(ctx.Actor.Id, movedRow.Id, movedRow.Kind, movedRow.Quantity, Op, from, LocationOf(movedRow)));
                    if (from is TileLoc tile) log.ChangedChunks.Add((PyMath.FloorDiv(tile.X, ChunkConst.Size), PyMath.FloorDiv(tile.Y, ChunkConst.Size)));
                    log.ChangedChunks.Add((PyMath.FloorDiv(lx, ChunkConst.Size), PyMath.FloorDiv(ly, ChunkConst.Size)));
                }
            }
            else if (moved is ActorRow movedActor && (movedActor.TileX, movedActor.TileY, movedActor.H) != (lx, ly, last.H))
            {
                var before = new TilePos(movedActor.TileX, movedActor.TileY, movedActor.H);
                (movedActor.X, movedActor.Y, movedActor.H) = (last.X + 0.5, last.Y + 0.5, last.H);
                movedActor.Z = PyMath.FloorDiv(last.H, 6);
                log.Events.Add(SimEvent.ActorMoved(movedActor.Id, before, new TilePos(lx, ly, last.H), "physics"));
            }
        }
        log.Events.Add(SimEvent.PhysicsResolved(ctx.Actor.Id, Op, trajectory, log.Damage, log.Broken));
        foreach (var (cx, cy) in log.ChangedChunks.OrderBy(c => c.Item1).ThenBy(c => c.Item2)) log.Events.Add(BumpChunk(ctx, cx, cy));
        return new Resolution(log.Events, null, trajectory);
    }

    private static double Strike(ActionContext ctx, ObjectTarget? tool)
    {
        if (tool is null) return Physics.Strike(null);
        var row = Obj(ctx, tool);
        var kind = row is not null ? ctx.Grid.Catalog[row.Kind].Tool : null;
        if (row is null || kind?.StrikeSpeedMS is null) return Physics.Strike(null);
        return Physics.Strike(TotalMass(ctx.Session, ctx.Grid.Catalog, row), kind.StrikeSpeedMS.Value);
    }
}
