using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.Events;
using EtherBound.Sim.Rng;
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

    public const int KitMinApples = 2;
    public const int KitMaxApples = 4;

    /// <summary>
    /// An Extra's starting needs, seeded per actor so the same seed gives the same crowd (Dev-011 D1):
    /// each level in [0.8, 1.0], so nobody is hungry in the first two game hours.
    /// </summary>
    public static ActorNeeds StartingNeeds(long seed, string actorId, int minute)
    {
        var rng = new RngStreams(seed).Stream($"needs:{actorId}");
        var needs = ActorNeeds.Full(minute);
        foreach (var spec in NeedCatalog.Default.Specs)
            needs = needs.With(spec.Key, NeedModel.StartLevel(rng.Random()), minute);
        return needs;
    }

    /// <summary>
    /// A worn, open backpack with a few apples. A streaming world places no objects, so its Extras bring
    /// their own food (Dev-011 D6); the bounded worlds lay theirs out and their object lists are pinned.
    /// </summary>
    public static void GiveFoodKit(Session session, string actorId, int apples)
    {
        var pack = session.AddObject(new ObjectRow { Kind = "backpack", Loc = "worn", State = Json.Obj(("open", true)) });
        ObjectHelpers.SetWorn(pack, actorId, "back");
        var food = session.AddObject(new ObjectRow { Kind = "apple", Loc = "in", Quantity = apples });
        ObjectHelpers.SetIn(food, pack.Id);
    }

    public static List<SimEvent> CreateExtras(Session session, WorldGrid grid, MaterialRegistry registry, long seed, (double X, double Y, int H) spawn,
        bool foodKit = false)
    {
        var events = new List<SimEvent>();
        var minute = session.World.GameMinute;
        var kit = new RngStreams(seed).Stream("population:kit");
        foreach (var generated in Population.Populate(grid, registry, spawn, seed))
        {
            session.AddActor(new ActorRow
            {
                Id = generated.Id, Kind = "extra", Name = generated.Name, X = generated.X, Y = generated.Y, H = generated.H,
                Z = PyMath.FloorDiv(generated.H, 6),
                Mind = new Mind(new TilePos(generated.AnchorX, generated.AnchorY, generated.AnchorH)).ToJson(),
                Needs = StartingNeeds(seed, generated.Id, minute).ToJson(),
            });
            if (foodKit) GiveFoodKit(session, generated.Id, kit.RandInt(KitMinApples, KitMaxApples));
            events.Add(SimEvent.ActorSpawned(generated.Id, "extra", new TilePos(generated.AnchorX, generated.AnchorY, generated.AnchorH), "created", generated.Name));
        }
        return events;
    }

    /// <summary>
    /// An Extra saved before <c>0012_actor_needs</c> has no needs: give it the same seeded start as a new
    /// one, and its food kit in a streaming world. Nothing else about it changes, and no event is written.
    /// </summary>
    private static void BackfillNeeds(Session session, long seed, bool foodKit)
    {
        var minute = session.World.GameMinute;
        var kit = new RngStreams(seed).Stream("population:kit");
        foreach (var extra in session.Actors().Where(a => a.Kind == "extra" && a.Needs is null))
        {
            extra.Needs = StartingNeeds(seed, extra.Id, minute).ToJson();
            var carriesPack = session.Objects().Any(o => o.ActorId == extra.Id && o.Loc == "worn");
            if (foodKit && !carriesPack) GiveFoodKit(session, extra.Id, kit.RandInt(KitMinApples, KitMaxApples));
        }
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
            BackfillNeeds(session, seed, spec.Streaming);
        }
        else
        {
            events.AddRange(CreateExtras(session, grid, registry, seed, spawn, spec.Streaming));
        }
        return events;
    }
}
