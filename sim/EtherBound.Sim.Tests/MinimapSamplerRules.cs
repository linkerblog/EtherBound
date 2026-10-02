using EtherBound.Host;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;
using EtherBound.Sim.World.Gen;

namespace EtherBound.Sim.Tests;

/// <summary>The minimap is a pure colouring of the ground the sim already has (Dev-010).</summary>
public sealed class MinimapSamplerRules
{
    private static readonly MaterialRegistry Registry = MaterialRegistry.Load();
    private static readonly MapSampler Sampler = new(Registry);

    private static (byte R, byte G, byte B) Rgb(byte[] pixels, int cell) => (pixels[cell * 3], pixels[cell * 3 + 1], pixels[cell * 3 + 2]);

    [Fact]
    public void A_chunk_is_sixteen_by_sixteen_cells_in_the_material_colour()
    {
        var grass = Registry["grass"];
        var pixels = Sampler.Chunk((_, _) => new TerrainCell(4, grass.Id), 3, -2);

        Assert.Equal(MapSampler.CellsPerChunk * MapSampler.CellsPerChunk * MapSampler.BytesPerCell, pixels.Length);
        var hex = grass.Color.TrimStart('#');
        var expected = (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
        for (var cell = 0; cell < 256; cell++) Assert.Equal(expected, Rgb(pixels, cell));
    }

    [Fact]
    public void A_slope_is_shaded_and_ground_the_sample_does_not_know_is_dark()
    {
        var grass = Registry["grass"].Id;
        // Height rises towards the south-east, so every cell is higher than its north-west neighbour.
        var sloped = Sampler.Chunk((x, y) => new TerrainCell((x + y) / 2, grass), 0, 0);
        var flat = Sampler.Chunk((_, _) => new TerrainCell(0, grass), 0, 0);
        Assert.True(Rgb(sloped, 17).G > Rgb(flat, 17).G);

        var unknown = Sampler.Chunk((_, _) => null, 0, 0);
        Assert.All(Enumerable.Range(0, 256), cell => Assert.Equal(((byte)16, (byte)16, (byte)24), Rgb(unknown, cell)));
    }

    [Fact]
    public void The_sampler_depends_only_on_the_surface_it_is_given()
    {
        var field = new TerrainField(7, new EndlessOptions(), Registry);
        var first = Sampler.Chunk((x, y) => field.At(x, y), -1, 2);
        Assert.Equal(first, Sampler.Chunk((x, y) => field.At(x, y), -1, 2));
        Assert.NotEqual(first, Sampler.Chunk((x, y) => field.At(x, y), 0, 0));
    }

    [Fact]
    public void The_engine_shows_a_dug_tile_without_loading_pristine_chunks()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "infinite");
        var niko = engine.OpenSession().GetActor(Ids.Player)!;
        var (x, y) = (niko.TileX, niko.TileY);
        var loaded = engine.Grid.Chunks.Count;

        var far = engine.SurfaceAt(x + 900, y - 900);
        Assert.Equal(far, engine.Source!.Sample(x + 900, y - 900));
        Assert.Equal(0, engine.MapRevision(PyMath.FloorDiv(x + 900, 32), PyMath.FloorDiv(y - 900, 32)));
        Assert.Equal(loaded, engine.Grid.Chunks.Count);

        var before = engine.SurfaceAt(x, y)!.Value;
        var dig = engine.Submit(Ids.Player, GameAction.On("dig", new TileTarget(x, y, niko.H)));
        Assert.True(dig.Accepted, dig.Reason);
        for (var i = 0; i < dig.Activity!.EndsMinute - dig.Activity.StartedMinute; i++) engine.AdvanceTime();
        var after = engine.SurfaceAt(x, y)!.Value;
        Assert.Equal(before.Height - 1, after.Height);
        Assert.Equal(1, engine.MapRevision(PyMath.FloorDiv(x, 32), PyMath.FloorDiv(y, 32)));
    }

    [Fact]
    public void A_bounded_world_answers_inside_its_chunks_and_nothing_outside()
    {
        using var engine = new WorldEngine();
        engine.NewGame(7, "test");
        Assert.NotNull(engine.SurfaceAt(121, 128));
        Assert.Null(engine.SurfaceAt(5000, 5000));
        Assert.Null(engine.Source);
    }

    [Fact]
    public void The_host_publishes_a_minimap_window_around_niko_and_reuses_it_while_nothing_changes()
    {
        using var host = new SimulationHost(seed: 7, generator: "infinite");
        Assert.True(host.WaitUntilReady(TimeSpan.FromSeconds(60)));
        Assert.True(host.TrySetClock(paused: true));

        // The window colours a few chunks per publish; wait until all 81 of them have been done.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        HostMinimap map;
        do
        {
            host.WaitForFrameAfter(host.LatestFrame!.Sequence, TimeSpan.FromMilliseconds(200));
            map = host.LatestFrame!.Minimap!;
        }
        while (DateTime.UtcNow < deadline && !AllChunksColoured(map));

        Assert.True(AllChunksColoured(map));
        var player = host.LatestFrame!.Actors.Single(a => a.Id == Ids.Player);
        Assert.Equal((PyMath.FloorDiv((int)Math.Floor(player.X), 32) - SimulationHost.MinimapRadiusChunks,
            PyMath.FloorDiv((int)Math.Floor(player.Y), 32) - SimulationHost.MinimapRadiusChunks), (map.OriginCx, map.OriginCy));
        Assert.Equal(9, map.Chunks);
        Assert.Equal(144 * 144 * 3, map.Rgb.Length);

        var before = host.LatestFrame!.Sequence;
        Assert.True(host.TrySetClock(speed: 3));
        Assert.True(host.WaitForFrameAfter(before, TimeSpan.FromSeconds(5)));
        Assert.Same(map, host.LatestFrame!.Minimap);
    }

    private static bool AllChunksColoured(HostMinimap map)
    {
        var width = map.Cells;
        for (var cy = 0; cy < map.Chunks; cy++)
        for (var cx = 0; cx < map.Chunks; cx++)
        {
            var any = false;
            for (var row = 0; row < map.CellsPerChunk && !any; row++)
            {
                var at = ((cy * map.CellsPerChunk + row) * width + cx * map.CellsPerChunk) * 3;
                for (var k = 0; k < map.CellsPerChunk * 3; k++)
                    if (map.Rgb[at + k] != 0)
                    {
                        any = true;
                        break;
                    }
            }
            if (!any) return false;
        }
        return true;
    }
}
