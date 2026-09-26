using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Tests;

public sealed class PickRules
{
    [Fact]
    public void Pick_reads_actor_geometry_without_committing_state()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        var state = engine.GetState();
        var player = state.Actors.Single(actor => actor.Id == Ids.Player);
        var seq = engine.ReadEvents().Count;
        var ray = new WorldRay(player.X, player.Y, player.H * 0.5 + 3, 0, 0, -1);

        var hit = engine.Pick(ray);

        Assert.NotNull(hit);
        Assert.Equal(new ActorTarget(Ids.Player), hit.Target);
        Assert.Equal(seq, engine.ReadEvents().Count);
        var after = engine.GetState().Actors.Single(actor => actor.Id == Ids.Player);
        Assert.Equal((player.X, player.Y), (after.X, after.Y));
    }

    [Fact]
    public void Pick_returns_the_visible_surface_tile()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        var state = engine.GetState();
        var actors = state.Actors.Select(actor => (PyMath.Floor(actor.X), PyMath.Floor(actor.Y))).ToHashSet();
        var tile = (X: 0, Y: 0, GroundH: 0);
        var found = false;
        for (var radius = 1; radius <= 8 && !found; radius++)
        for (var dy = -radius; dy <= radius && !found; dy++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            var x = PyMath.Floor(state.Actors.Single(actor => actor.Id == Ids.Player).X) + dx;
            var y = PyMath.Floor(state.Actors.Single(actor => actor.Id == Ids.Player).Y) + dy;
            var ground = engine.Grid.GroundAt(x, y);
            if (actors.Contains((x, y)) || engine.Grid.ObjectsAt(x, y).Count != 0 || ground is null ||
                engine.Grid.IsVoid(x, y, ground.Value.GroundH)) continue;
            tile = (x, y, ground.Value.GroundH);
            found = true;
            break;
        }
        Assert.True(found, "the test world should have a nearby visible empty tile");
        var ray = new WorldRay(tile.X + 0.5, tile.Y + 0.5, tile.GroundH * 0.5 + 4, 0, 0, -1);

        var hit = engine.Pick(ray);

        Assert.NotNull(hit);
        Assert.IsType<TileTarget>(hit.Target);
        Assert.Equal((tile.X, tile.Y), (hit.TileX, hit.TileY));
    }

    [Fact]
    public void Pick_returns_a_wall_edge_from_its_visible_face()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "lab");
        var wall = (Level: (ChunkLevel?)null, Index: 0);
        foreach (var level in engine.Grid.Levels.Values)
        {
            for (var index = 0; index < level.WallN.Length; index++)
            {
                var flags = level.EdgeFlags[index];
                if (level.WallN[index] == 0 || (flags & ChunkConst.EdgeNDoorway) != 0 ||
                    level.FloorH[index] == ChunkConst.NoFloor) continue;
                wall = (level, index);
                break;
            }
            if (wall.Level is not null) break;
        }
        Assert.NotNull(wall.Level);
        var x = wall.Level!.Cx * ChunkConst.Size + wall.Index % ChunkConst.Size;
        var y = wall.Level.Cy * ChunkConst.Size + wall.Index / ChunkConst.Size;
        var height = wall.Level.FloorH[wall.Index] + 1;
        var ray = new WorldRay(x + 0.5, y - 0.75, height * 0.5, 0, 1, 0);

        var hit = engine.Pick(ray);

        Assert.NotNull(hit);
        Assert.Equal(new EdgeTarget(x, y, wall.Level.Z, "north"), hit.Target);
    }

    [Fact]
    public void Pick_returns_a_solid_object_volume()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "lab");
        var occupied = engine.GetState().Actors.Select(actor => (PyMath.Floor(actor.X), PyMath.Floor(actor.Y))).ToHashSet();
        var candidate = engine.Grid.Chunks.Keys.SelectMany(key => engine.Grid.ObjectsAtChunk(key.Item1, key.Item2))
            .Select(item => (Item: item, Kind: engine.Grid.KindOf(item)))
            .First(pair => pair.Kind is { Solid: true, Height: > 0 } && !occupied.Contains((pair.Item.X, pair.Item.Y)));
        var objectTop = candidate.Item.H + candidate.Kind!.Height;
        var ray = new WorldRay(candidate.Item.X + 0.5, candidate.Item.Y + 0.5, objectTop * 0.5 + 2, 0, 0, -1);

        var hit = engine.Pick(ray);

        Assert.NotNull(hit);
        Assert.Equal(new ObjectTarget(candidate.Item.Id), hit.Target);
    }
}
