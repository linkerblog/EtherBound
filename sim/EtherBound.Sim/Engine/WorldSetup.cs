using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Engine;

/// <summary>Creating, regenerating and settling a world (<c>engine/world_setup.py</c>).</summary>
public static class WorldSetup
{
    /// <summary>The save's generator, its validated options and whether it fell back.</summary>
    public static (GeneratorSpec Spec, IGeneratorOptions Options, bool Fallback) ResolveGenerator(WorldMetaRow meta)
    {
        var fallback = false;
        var spec = Generators.Get(string.IsNullOrEmpty(meta.Generator) ? Generators.Default : meta.Generator);
        if (spec is null)
        {
            spec = Generators.All[Generators.Default];
            meta.Generator = spec.Key;
            meta.GenOptions = new JsonObject();
            fallback = true;
        }
        IGeneratorOptions options;
        try
        {
            options = spec.Parse(meta.GenOptions);
        }
        catch (ArgumentException)
        {
            options = spec.Defaults;
            fallback = true;
        }
        catch (InvalidOperationException)
        {
            options = spec.Defaults;
            fallback = true;
        }
        return (spec, options, fallback);
    }

    /// <summary>Keep held and worn objects and everything inside them; drop the rest.</summary>
    public static List<ObjectRow> CarriedObjects(Session session)
    {
        var all = session.Objects();
        var keep = all.Where(o => o.Loc is "held" or "worn").Select(o => o.Id).ToHashSet();
        var frontier = new HashSet<int>(keep);
        while (frontier.Count > 0)
        {
            var children = all.Where(o => o.Loc == "in" && o.ContainerId is { } c && frontier.Contains(c)).Select(o => o.Id).ToHashSet();
            frontier = children.Except(keep).ToHashSet();
            keep.UnionWith(frontier);
        }
        return all.Where(o => keep.Contains(o.Id)).ToList();
    }

    /// <summary>Writes a generated world's terrain; the grid is rebuilt from it in load order.</summary>
    public static WorldGrid CommitWorld(Session session, GeneratedWorld world, MaterialRegistry registry, ObjectCatalog catalog)
    {
        var chunks = world.Chunks.Values.OrderBy(c => c.Cx).ThenBy(c => c.Cy).ToList();
        var levels = world.Levels.Values.OrderBy(l => l.Cx).ThenBy(l => l.Cy).ThenBy(l => l.Z).ToList();
        var grid = new WorldGrid(chunks, levels, registry, catalog: catalog);
        foreach (var chunk in chunks) session.MarkChunk(chunk.Cx, chunk.Cy);
        foreach (var level in levels) session.MarkLevel(level.Cx, level.Cy, level.Z);
        return grid;
    }

    public static void CommitObjects(Session session, IReadOnlyList<GeneratedObject> objects)
    {
        var ids = new List<int>();
        foreach (var obj in objects)
        {
            var row = new ObjectRow
            {
                Kind = obj.Kind, Loc = obj.Parent is null ? "tile" : "in", Quantity = obj.Quantity,
                State = obj.Open ? Json.Obj(("open", true)) : new JsonObject(),
            };
            if (obj.Parent is { } parent) row.ContainerId = ids[parent];
            else ObjectHelpers.SetTile(row, obj.X, obj.Y, obj.H);
            session.AddObject(row);
            ids.Add(row.Id);
        }
    }

    public static SimEvent SpawnEvent(ActorRow actor, string reason) =>
        SimEvent.ActorSpawned(actor.Id, actor.Kind, new TilePos(actor.TileX, actor.TileY, actor.H), reason, actor.Name);

    /// <summary>Snap a loaded actor to its surface, relocate it to spawn if it has none.</summary>
    public static SimEvent? SettleActor(WorldGrid grid, ActorRow actor, (double X, double Y, int H) spawn)
    {
        var surface = Movement.NearestSurface(grid, actor.X, actor.Y, actor.H);
        if (surface is null)
        {
            (actor.X, actor.Y, actor.H, actor.Z) = (spawn.X, spawn.Y, spawn.H, PyMath.FloorDiv(spawn.H, 6));
            return SpawnEvent(actor, "relocated");
        }
        // A data repair, not a world fact: no event. The standing rule needs the exact h, and the
        // next actor.moved carries the corrected h in from_tile.
        if (surface.Value.H != actor.H) (actor.H, actor.Z) = (surface.Value.H, PyMath.FloorDiv(surface.Value.H, 6));
        return null;
    }

    public static List<SimEvent> CreateExtras(Session session, WorldGrid grid, MaterialRegistry registry, long seed, (double X, double Y, int H) spawn)
    {
        var events = new List<SimEvent>();
        foreach (var generated in Population.Populate(grid, registry, spawn, seed))
        {
            session.AddActor(new ActorRow
            {
                Id = generated.Id, Kind = "extra", Name = generated.Name, X = generated.X, Y = generated.Y, H = generated.H,
                Z = PyMath.FloorDiv(generated.H, 6),
                Mind = new Mind(new TilePos(generated.AnchorX, generated.AnchorY, generated.AnchorH)).ToJson(),
            });
            events.Add(SimEvent.ActorSpawned(generated.Id, "extra", new TilePos(generated.AnchorX, generated.AnchorY, generated.AnchorH), "created", generated.Name));
        }
        return events;
    }

    /// <summary>Create or settle Niko and the Extras, emitting one actor.spawned per change.</summary>
    public static List<SimEvent> EnsureActors(Session session, WorldGrid grid, MaterialRegistry registry, GeneratorSpec spec,
        IGeneratorOptions options, long seed)
    {
        var spawn = Spawning.SpawnPoint(grid, registry, spec, options, seed);
        var events = new List<SimEvent>();
        var niko = session.GetActor(Ids.Player);
        if (niko is null)
        {
            niko = new ActorRow { Id = Ids.Player, Kind = "player", X = spawn.X, Y = spawn.Y, H = spawn.H, Z = PyMath.FloorDiv(spawn.H, 6), MassKg = 80.0 };
            session.AddActor(niko);
            events.Add(SpawnEvent(niko, "created"));
        }
        else if (SettleActor(grid, niko, spawn) is { } settled)
        {
            events.Add(settled);
        }
        var extras = session.Actors().Where(a => a.Kind == "extra").ToList();
        if (extras.Count > 0)
        {
            foreach (var extra in extras)
                if (SettleActor(grid, extra, spawn) is { } settled) events.Add(settled);
        }
        else
        {
            events.AddRange(CreateExtras(session, grid, registry, seed, spawn));
        }
        return events;
    }
}
