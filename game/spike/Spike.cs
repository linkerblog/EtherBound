using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace EtherBound.Game.Spike;

/// <summary>
/// Dev-025 stage 0 look spike. Loads a world dump and draws it with the stage-0 camera, pixel,
/// texture and light decisions. Keys: WASD walk, 1/2/4 zoom, T texturing, C cutaway, O SSAO,
/// H shadows. User args (after <c>--</c>): <c>--world test-7</c>, <c>--shots DIR</c>,
/// <c>--bench DIR</c>, <c>--shimmer DIR</c>.
/// </summary>
public partial class Spike : Node
{
    private static readonly (string Key, string Top, string Side)[] Sheets =
    {
        ("grass", "grass/grass_x4.png", "grass/grass_side_x4.png"),
        ("wood_floor", "floor/planks_x4.png", "floor/planks_side_x4.png"),
        ("concrete", "floor/concrete_x4.png", "floor/concrete_side_x4.png"),
        ("asphalt", "floor/asphalt_x4.png", "floor/asphalt_side_x4.png"),
        ("roofing", "floor/roofing_x4.png", "floor/roofing_side_x4.png"),
        ("brick", "wall/brick_x4.png", "wall/brick_side_x4.png"),
    };

    private readonly Dictionary<string, string> _args = new();
    private WorldDump _world = null!;
    private PixelView _view = null!;
    private Node3D _root = null!;
    private ShaderMaterial _terrainMat = null!, _structureMat = null!, _glassMat = null!;
    private Godot.Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _niko = null!;
    private Label _hud = null!;
    private Vector3 _pos; // x, y in metres; z = standing h in half-metres
    private bool _cutaway = true;
    private int _texMode;
    private bool _scripted;
    private string? _only;

    public static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    public override void _Ready()
    {
        var raw = OS.GetCmdlineUserArgs();
        for (var i = 0; i < raw.Length; i++)
            if (raw[i].StartsWith("--")) _args[raw[i][2..]] = i + 1 < raw.Length && !raw[i + 1].StartsWith("--") ? raw[++i] : "";
        var worldName = _args.GetValueOrDefault("world", "test-7");
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();
        _world = WorldDump.Load(Path.Combine(RepoRoot, "game", "spike", "dumps", $"{worldName}.json"));
        _pos = new Vector3(_world.Spawn.X, _world.Spawn.Y, _world.Spawn.H);

        _view = new PixelView();
        AddChild(_view);
        _root = new Node3D { Scale = new Vector3(1, PixelView.VerticalScale, 1) };
        _view.Viewport.AddChild(_root);
        BuildEnvironment();
        BuildWorld();
        BuildNiko();
        GD.Print($"spike: {worldName} loaded and meshed in {loadTimer.ElapsedMilliseconds} ms, {_world.Chunks.Count} chunks");

        _hud = new Label { Position = new Vector2(8, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 4);
        var hudLayer = new CanvasLayer { Layer = 2 };
        hudLayer.AddChild(_hud);
        AddChild(hudLayer);

        if (_args.GetValueOrDefault("ssao") == "0") _env.SsaoEnabled = false;
        if (_args.GetValueOrDefault("shadows") == "0") _sun.ShadowEnabled = false;
        if (_args.TryGetValue("only", out var only)) _only = only;
        if (_args.TryGetValue("shots", out var shots)) _ = RunShots(shots);
        else if (_args.TryGetValue("bench", out var bench)) _ = RunBench(bench);
        else if (_args.TryGetValue("shimmer", out var shimmer)) _ = RunShimmer(shimmer);
    }

    private void BuildEnvironment()
    {
        // Linear light: ambient + sun on a top = 1.0, so tops keep the sheet colours; a south face
        // reads 0.82 and an east face 0.66 in sRGB, like the web client's baked side light.
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("#1d2128"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.25f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 2.5f,
            SsaoDetail = 0.5f,
            SsaoLightAffect = 0.2f,
        };
        _view.Viewport.AddChild(new WorldEnvironment { Environment = _env });
        var l = new Vector3(0.174f, 0.870f, 0.461f).Normalized(); // towards the sun, (x, up, y)
        _sun = new DirectionalLight3D
        {
            LightEnergy = 0.8617f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 60f,
            ShadowBias = 0.08f,
            ShadowNormalBias = 1.5f,
        };
        _view.Viewport.AddChild(_sun);
        _sun.LookAt(-l, Mathf.Abs(l.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    private void BuildWorld()
    {
        var shader = GD.Load<Shader>("res://spike/Terrain.gdshader");
        var tops = new Godot.Collections.Array<Image>();
        var sides = new Godot.Collections.Array<Image>();
        var topLayers = new Dictionary<int, int>();
        var sideLayers = new Dictionary<int, int>();
        var sprites = Path.Combine(RepoRoot, "src", "sprites");
        foreach (var (key, top, side) in Sheets)
        {
            var material = _world.Materials.Values.FirstOrDefault(m => m.Key == key);
            if (material is null) continue;
            var topImage = Image.LoadFromFile(Path.Combine(sprites, top));
            var sideImage = Image.LoadFromFile(Path.Combine(sprites, side));
            topImage.Convert(Image.Format.Rgba8);
            sideImage.Convert(Image.Format.Rgba8);
            topLayers[material.Id] = tops.Count;
            sideLayers[material.Id] = sides.Count;
            tops.Add(topImage);
            sides.Add(sideImage);
        }
        var topArray = new Texture2DArray();
        topArray.CreateFromImages(tops);
        var sideArray = new Texture2DArray();
        sideArray.CreateFromImages(sides);
        _terrainMat = new ShaderMaterial { Shader = shader };
        _terrainMat.SetShaderParameter("tops", topArray);
        _terrainMat.SetShaderParameter("sides", sideArray);
        _structureMat = (ShaderMaterial)_terrainMat.Duplicate();
        _structureMat.SetShaderParameter("clip_enabled", true);
        _glassMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode cull_disabled, specular_disabled, depth_draw_never;
                    uniform float clip_h = 100000.0;
                    varying float hh;
                    void vertex() { hh = VERTEX.y * 2.0; }
                    void fragment() {
                        if (hh > clip_h + 0.001) discard;
                        ALBEDO = vec3(0.686, 0.791, 0.871);
                        ALPHA = 0.45;
                    }
                    """,
            },
        };

        var mesher = new ChunkMesher(_world, topLayers, sideLayers);
        foreach (var (cx, cy) in _world.Chunks.Keys)
        {
            var meshes = mesher.Build(cx, cy);
            Add(meshes.Terrain, _terrainMat);
            Add(meshes.Structure, _structureMat);
            Add(meshes.Glass, _glassMat);
        }
    }

    private void Add(ArrayMesh? mesh, Material material)
    {
        if (mesh is null) return;
        _root.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
        });
    }

    private void BuildNiko()
    {
        // The silhouette is a second pass drawn only where Niko's depth test fails.
        var silhouette = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, depth_test_inverted, depth_draw_never, cull_back;
                    void fragment() { ALBEDO = vec3(0.55, 0.75, 1.0); ALPHA = 0.7; }
                    """,
            },
            RenderPriority = 10,
        };
        var body = new StandardMaterial3D { AlbedoColor = new Color("#2a7fff"), NextPass = silhouette };
        _niko = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.7f, Material = body },
        };
        _root.AddChild(_niko);
    }

    public override void _Process(double delta)
    {
        if (!_scripted)
        {
            var kx = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
            var ky = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
            var dir = new Vector2(kx + ky, ky - kx);
            if (dir != Vector2.Zero) Walk(dir.Normalized() * 4f * (float)delta);
        }
        Place();
        _hud.Text = $"{_world.Generator}  ({_pos.X:0.0}, {_pos.Y:0.0}, h={_pos.Z})  x{_view.Scale}  " +
            $"tex={(_texMode == 0 ? "iso" : "world")}  cut={_cutaway}  ssao={_env.SsaoEnabled}  " +
            $"shadows={_sun.ShadowEnabled}  {Engine.GetFramesPerSecond()} fps";
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Key1: _view.SetScale(1); break;
            case Key.Key2: _view.SetScale(2); break;
            case Key.Key4: _view.SetScale(4); break;
            case Key.T: SetTexMode(1 - _texMode); break;
            case Key.C: _cutaway = !_cutaway; break;
            case Key.O: _env.SsaoEnabled = !_env.SsaoEnabled; break;
            case Key.H: _sun.ShadowEnabled = !_sun.ShadowEnabled; break;
        }
    }

    private void SetTexMode(int mode)
    {
        _texMode = mode;
        _terrainMat.SetShaderParameter("tex_mode", mode);
        _structureMat.SetShaderParameter("tex_mode", mode);
    }

    private void Walk(Vector2 step)
    {
        var next = new Vector2(_pos.X + step.X, _pos.Y + step.Y);
        var h = Cutaway.StandingH(_world, (int)Mathf.Floor(next.X), (int)Mathf.Floor(next.Y), (int)_pos.Z);
        if (h is { } standing) _pos = new Vector3(next.X, next.Y, standing);
    }

    private void Place()
    {
        var feet = new Vector3(_pos.X, _pos.Z * 0.5f, _pos.Y);
        _niko.Position = feet + new Vector3(0, 0.85f, 0);
        var clip = _cutaway ? Cutaway.ClipH(_world, _pos.X, _pos.Y, (int)_pos.Z) : float.PositiveInfinity;
        _structureMat.SetShaderParameter("clip_h", float.IsInfinity(clip) ? 100000f : clip);
        _glassMat.SetShaderParameter("clip_h", float.IsInfinity(clip) ? 100000f : clip);
        _view.Follow(feet * new Vector3(1, PixelView.VerticalScale, 1) + new Vector3(0, 0.6f, 0));
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private async Task RunShots(string dir)
    {
        _scripted = true;
        Directory.CreateDirectory(dir);
        var name = _args.GetValueOrDefault("world", "test-7");
        var places = name.StartsWith("test")
            ? new (string, Vector3, int, int)[]
            {
                ("spawn-x1", new(121.5f, 128.5f, 2), 1, 0), ("spawn-x2", new(121.5f, 128.5f, 2), 2, 0),
                ("spawn-x4", new(121.5f, 128.5f, 2), 4, 0), ("spawn-x2-worldtex", new(121.5f, 128.5f, 2), 2, 1),
                ("spawn-x4-worldtex", new(121.5f, 128.5f, 2), 4, 1),
                ("building-x1", new(128.5f, 146.5f, 7), 1, 0), ("building-x2", new(131.5f, 146.5f, 10), 2, 0),
                ("inside-x1", new(146.5f, 157.5f, 12), 1, 0), ("inside-x2", new(146.5f, 157.5f, 12), 2, 0),
                ("inside-x4", new(146.5f, 157.5f, 12), 4, 0), ("behind-x2", new(148.5f, 130.5f, 20), 2, 0),
                ("behind-x1-nocut", new(148.5f, 130.5f, 20), 1, 2), ("terrace-x2", new(111.5f, 97.5f, 20), 2, 0),
            }
            : new (string, Vector3, int, int)[]
            {
                ("lab-x1", new(_world.Spawn.X, _world.Spawn.Y, _world.Spawn.H), 1, 0),
                ("lab-x2", new(_world.Spawn.X, _world.Spawn.Y, _world.Spawn.H), 2, 0),
            };
        foreach (var (shot, at, scale, tex) in places)
        {
            if (_only is not null && shot != _only) continue;
            _pos = at;
            if (Cutaway.StandingH(_world, (int)Mathf.Floor(at.X), (int)Mathf.Floor(at.Y), (int)at.Z) is { } h)
                _pos.Z = h;
            else if (_world.GroundH((int)Mathf.Floor(at.X), (int)Mathf.Floor(at.Y)) is { } g)
                _pos.Z = g;
            _view.SetScale(scale);
            SetTexMode(tex == 1 ? 1 : 0);
            _cutaway = tex != 2;
            await Frames(8);
            var suffix = (_env.SsaoEnabled ? "" : "-nossao") + (_sun.ShadowEnabled ? "" : "-noshadow");
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, $"{shot}{suffix}.png"));
            GD.Print($"shot {shot} at h={_pos.Z}");
        }
        GetTree().Quit();
    }

    private async Task RunBench(string dir)
    {
        _scripted = true;
        Directory.CreateDirectory(dir);
        var rid = _view.Viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        var lines = new List<string>();
        foreach (var scale in new[] { 1, 2, 4 })
        {
            _view.SetScale(scale);
            await Frames(30);
            var cpu = new List<double>();
            var gpu = new List<double>();
            var start = new Vector2(_world.Spawn.X, _world.Spawn.Y);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var last = 0.0;
            for (var i = 0; i < 600; i++)
            {
                // Scroll diagonally across the map at walking speed x3 so every frame moves sub-pixels.
                Walk(new Vector2(0.02f, 0.013f));
                await Frames(1);
                var now = clock.Elapsed.TotalMilliseconds;
                cpu.Add(now - last);
                last = now;
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid));
            }
            _pos = new Vector3(start.X, start.Y, _world.Spawn.H);
            string Stat(List<double> v) => $"avg {v.Average():0.00} ms, p95 {v.OrderBy(x => x).ElementAt(v.Count * 95 / 100):0.00} ms";
            lines.Add($"x{scale}: frame {Stat(cpu.Skip(10).ToList())}; world gpu {Stat(gpu.Skip(10).ToList())}");
            GD.Print(lines[^1]);
        }
        lines.Insert(0, $"{_world.Generator}: {_world.Chunks.Count} chunks, {RenderingServer.GetVideoAdapterName()}, " +
            $"window {GetViewport().GetVisibleRect().Size}");
        File.WriteAllLines(Path.Combine(dir, $"bench-{_world.Generator}.txt"), lines);
        GetTree().Quit();
    }

    /// <summary>
    /// Scrolls by whole and fractional art pixels and compares the low-res buffer with the first
    /// frame shifted by the same whole-pixel amount. Zero mismatches means no texel shimmer.
    /// </summary>
    private async Task RunShimmer(string dir)
    {
        _scripted = true;
        Directory.CreateDirectory(dir);
        _niko.Visible = false;
        SetProcess(false);
        _view.SetScale(1);
        var lines = new List<string>();
        foreach (var ssao in new[] { false, true })
        {
            _env.SsaoEnabled = ssao;
            var baseTarget = new Vector3(_world.Spawn.X, _world.Spawn.H * 0.5f * PixelView.VerticalScale, _world.Spawn.Y);
            _view.Follow(baseTarget);
            await Frames(10);
            var reference = _view.CaptureLowRes();
            foreach (var shift in new[] { 0.4f, 1f, 3.37f, 7f })
            {
                var right = new Vector3(Mathf.Sqrt(0.5f), 0, -Mathf.Sqrt(0.5f));
                _view.Follow(baseTarget + right * (shift / PixelView.PixelsPerUnit));
                await Frames(10);
                var frame = _view.CaptureLowRes();
                var whole = (int)Mathf.Round(shift);
                var (bad, total) = Compare(reference, frame, whole);
                lines.Add($"ssao={ssao} shift {shift} px (camera {whole} px): {bad}/{total} pixels differ ({100.0 * bad / total:0.000} %)");
                GD.Print(lines[^1]);
            }
        }
        File.WriteAllLines(Path.Combine(dir, $"shimmer-{_world.Generator}.txt"), lines);
        GetTree().Quit();
    }

    private static (int Bad, int Total) Compare(Image a, Image b, int shift)
    {
        int bad = 0, total = 0;
        for (var y = 8; y < a.GetHeight() - 8; y++)
        for (var x = 8; x < a.GetWidth() - 16; x++)
        {
            var p = a.GetPixel(x + shift, y);
            var q = b.GetPixel(x, y);
            total++;
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 3f / 255f) bad++;
        }
        return (bad, total);
    }
}
