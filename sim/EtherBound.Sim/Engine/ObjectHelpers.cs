using EtherBound.Sim.Core;
using EtherBound.Sim.Engine.Ops;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>Object-row helpers shared by the handlers (<c>engine/objects.py</c>).</summary>
public static class ObjectHelpers
{
    public const int MaxContentDepth = 8;

    public static List<ObjectRow> Children(Session session, int containerId) =>
        session.Objects().Where(o => o.Loc == "in" && o.ContainerId == containerId).ToList();

    /// <summary>Kilograms of an object and everything inside it, recursively (depth cap 8).</summary>
    public static double TotalMass(Session session, ObjectCatalog catalog, ObjectRow obj, int depth = 0)
    {
        var kind = catalog[obj.Kind];
        var contents = 0.0;
        if (kind.Container is not null && depth < MaxContentDepth)
            foreach (var child in Children(session, obj.Id)) contents += TotalMass(session, catalog, child, depth + 1);
        return kind.Mass * obj.Quantity + contents;
    }

    public static double Bulk(ObjectCatalog catalog, ObjectRow obj) => catalog[obj.Kind].Bulk * obj.Quantity;

    public static double ContentsBulk(Session session, ObjectCatalog catalog, int containerId)
    {
        var total = 0.0;
        foreach (var child in Children(session, containerId)) total += Bulk(catalog, child);
        return total;
    }

    public static double ActorLoadKg(Session session, ObjectCatalog catalog, string actorId)
    {
        var total = 0.0;
        foreach (var row in session.Objects().Where(o => o.ActorId == actorId)) total += TotalMass(session, catalog, row);
        return total;
    }

    /// <summary>Contents reachable: an open container, or a container without a lid at all.</summary>
    public static bool IsAccessible(ObjectKind kind, ObjectRow obj) => kind.Container is not null && (!kind.Openable || obj.IsOpen);

    public static Location LocationOf(ObjectRow obj) => obj.Loc switch
    {
        "tile" => new TileLoc(obj.X!.Value, obj.Y!.Value, obj.H!.Value),
        "in" => new InLoc(obj.ContainerId!.Value),
        "held" => new HeldLoc(obj.ActorId!, obj.Slot!),
        _ => new WornLoc(obj.ActorId!, obj.Slot!),
    };

    public static void SetTile(ObjectRow obj, int x, int y, int h)
    {
        obj.Loc = "tile";
        (obj.X, obj.Y, obj.H) = (x, y, h);
        (obj.Cx, obj.Cy) = (PyMath.FloorDiv(x, ChunkConst.Size), PyMath.FloorDiv(y, ChunkConst.Size));
        obj.ContainerId = null;
        obj.ActorId = null;
        obj.Slot = null;
    }

    public static void SetIn(ObjectRow obj, int containerId)
    {
        obj.Loc = "in";
        obj.X = obj.Y = obj.H = obj.Cx = obj.Cy = null;
        obj.ContainerId = containerId;
        obj.ActorId = null;
        obj.Slot = null;
    }

    public static void SetHeld(ObjectRow obj, string actorId, string hand)
    {
        obj.Loc = "held";
        obj.X = obj.Y = obj.H = obj.Cx = obj.Cy = null;
        obj.ContainerId = null;
        obj.ActorId = actorId;
        obj.Slot = hand;
    }

    public static void SetWorn(ObjectRow obj, string actorId, string slot)
    {
        obj.Loc = "worn";
        obj.X = obj.Y = obj.H = obj.Cx = obj.Cy = null;
        obj.ContainerId = null;
        obj.ActorId = actorId;
        obj.Slot = slot;
    }

    public static List<ObjectRow> Held(Session session, string actorId) =>
        session.Objects().Where(o => o.Loc == "held" && o.ActorId == actorId).ToList();

    public static List<ObjectRow> Worn(Session session, string actorId) =>
        session.Objects().Where(o => o.Loc == "worn" && o.ActorId == actorId).ToList();

    /// <summary>Free hands, right first. A two-handed "both" is not counted, exactly as in Python.</summary>
    public static List<string> FreeHands(Session session, string actorId)
    {
        var used = Held(session, actorId).Select(o => o.Slot).ToHashSet();
        return new[] { "right", "left" }.Where(hand => !used.Contains(hand)).ToList();
    }

    public static List<TileObject> TileObjects(Session session, ObjectCatalog catalog, int cx, int cy) =>
        TileObjects(session.Objects(), catalog, cx, cy);

    public static List<TileObject> TileObjects(IEnumerable<ObjectRow> rows, ObjectCatalog catalog, int cx, int cy) =>
        rows.Where(o => o.Loc == "tile" && o.Cx == cx && o.Cy == cy)
            .Select(o => new TileObject(o.Id, o.Kind, o.X!.Value, o.Y!.Value, o.H!.Value, o.Quantity,
                catalog.Get(o.Kind) is { Openable: true } ? o.IsOpen : null))
            .ToList();

    public static void RefreshChunkObjects(ActionContext ctx, int cx, int cy) =>
        ctx.Grid.SetChunkObjects(cx, cy, TileObjects(ctx.Session, ctx.Grid.Catalog, cx, cy));

    public static SimEvent BumpChunk(ActionContext ctx, int cx, int cy)
    {
        var chunk = ctx.Grid.Chunk(cx, cy) ?? throw new KeyNotFoundException($"no chunk at {cx},{cy}");
        var bumped = chunk.With(revision: chunk.Revision + 1);
        ctx.Grid.AddChunk(bumped);
        ctx.Session.MarkChunk(cx, cy);
        RefreshChunkObjects(ctx, cx, cy);
        return SimEvent.ChunkChanged(cx, cy, bumped.Revision);
    }

    public static (int Cx, int Cy)? ChunkOfLocation(ActionContext ctx, Location loc)
    {
        if (loc is TileLoc tile) return (PyMath.FloorDiv(tile.X, ChunkConst.Size), PyMath.FloorDiv(tile.Y, ChunkConst.Size));
        if (loc is InLoc inside && ctx.Session.GetObject(inside.ObjectId) is { Loc: "tile" } container)
            return (container.Cx!.Value, container.Cy!.Value);
        return null;
    }

    /// <summary>Tile objects resting exactly at <paramref name="h"/> on a tile, in id order.</summary>
    public static List<ObjectRow> AtCell(ActionContext ctx, int x, int y, int h) =>
        ctx.Session.Objects().Where(o => o.Loc == "tile" && o.X == x && o.Y == y && o.H == h).ToList();
}
