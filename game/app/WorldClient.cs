using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EtherBound.Host;
using EtherBound.Game.Spike;
using EtherBound.Sim.Core;
using Godot;

namespace EtherBound.Game.App;

public partial class WorldClient : Node
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

    private readonly Dictionary<(int Cx, int Cy, int Z), MeshInstance3D> _chunkMeshes = new();
    private readonly Dictionary<(int Cx, int Cy), HostChunk> _renderedChunks = new();
    private readonly Dictionary<string, MeshInstance3D> _actors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _actorTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _args = new(StringComparer.Ordinal);
    private SimulationHost? _host;
    private PixelView _view = null!;
    private Node3D _root = null!;
    private WorldDump? _world;
    private WorldFrame? _frame;
    private ShaderMaterial _terrainMat = null!, _structureMat = null!, _glassMat = null!;
    private Godot.Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private Label _hud = null!;
    private Button _pauseButton = null!;
    private Dictionary<int, int> _topLayers = new();
    private Dictionary<int, int> _sideLayers = new();
    private long _lastSequence;
    private double _moveAccumulator;
    private bool _cutaway = true;
    private bool _scripted;
    private bool _shotsStarted;
    private int _texMode;

    private static string RepoRoot => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));

    public override void _Ready()
    {
        var raw = OS.GetCmdlineUserArgs();
        for (var i = 0; i < raw.Length; i++)
            if (raw[i].StartsWith("--")) _args[raw[i][2..]] = i + 1 < raw.Length && !raw[i + 1].StartsWith("--") ? raw[++i] : "";

        _view = new PixelView();
        AddChild(_view);
        _root = new Node3D { Scale = new Vector3(1, PixelView.VerticalScale, 1) };
        _view.Viewport.AddChild(_root);
        BuildEnvironment();
        BuildHud();

        var seed = _args.TryGetValue("seed", out var rawSeed) && long.TryParse(rawSeed, out var parsedSeed) ? parsedSeed : 7;
        var generator = _args.GetValueOrDefault("generator", "test");
        var databasePath = _args.GetValueOrDefault("database", Path.Combine(OS.GetUserDataDir(), "etherbound.db"));
        _host = new SimulationHost(databasePath, seed, generator);
        GD.Print($"world client started: seed {seed}, generator {generator}");
    }

    private void BuildEnvironment()
    {
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
        var light = new Vector3(0.174f, 0.870f, 0.461f).Normalized();
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
        _sun.LookAt(-light, Mathf.Abs(light.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    private void BuildHud()
    {
        _hud = new Label { Text = "Starting simulation...", Position = new Vector2(8, 8) };
        _hud.AddThemeColorOverride("font_color", Colors.White);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 4);

        var controls = new HBoxContainer { Position = new Vector2(8, 36) };
        foreach (var speed in new[] { 1, 3, 10 })
        {
            var button = new Button { Text = $"x{speed}" };
            button.Pressed += () => _host?.TrySetClock(speed: speed);
            controls.AddChild(button);
        }
        _pauseButton = new Button { Text = "Pause" };
        _pauseButton.Pressed += () =>
        {
            if (_frame is { } frame) _host?.TrySetClock(paused: !frame.Paused);
        };
        controls.AddChild(_pauseButton);

        var layer = new CanvasLayer { Layer = 2 };
        layer.AddChild(_hud);
        layer.AddChild(controls);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        if (_host?.LatestFrame is { } latest && latest.Sequence != _lastSequence) ApplyFrame(latest);
        if (_host?.Fault is { } fault) _hud.Text = $"Simulation stopped: {fault.Message}";

        foreach (var (id, actor) in _actors)
            if (_actorTargets.TryGetValue(id, out var target)) actor.Position = actor.Position.MoveToward(target, (float)delta * 12f);
        FollowPlayer();
        SendMovement(delta);
        UpdateHud();
    }

    private void ApplyFrame(WorldFrame frame)
    {
        _frame = frame;
        UpdateActors(frame);
        var current = frame.Chunks.ToDictionary(chunk => (chunk.Cx, chunk.Cy));
        var dirty = current.Count != _renderedChunks.Count || current.Any(pair =>
            !_renderedChunks.TryGetValue(pair.Key, out var old) || !ReferenceEquals(old, pair.Value));
        if (dirty)
        {
            _world = WorldDump.FromFrame(frame);
            if (_terrainMat is null) BuildTerrainMaterials();
            RebuildChunks(frame, current);
        }
        _lastSequence = frame.Sequence;

        if (!_shotsStarted && _args.TryGetValue("shots", out var directory))
        {
            _shotsStarted = true;
            _scripted = true;
            _ = RunShots(directory);
        }
    }

    private void BuildTerrainMaterials()
    {
        var shader = GD.Load<Shader>("res://spike/Terrain.gdshader");
        var tops = new Godot.Collections.Array<Image>();
        var sides = new Godot.Collections.Array<Image>();
        _topLayers = new Dictionary<int, int>();
        _sideLayers = new Dictionary<int, int>();
        var sprites = Path.Combine(RepoRoot, "src", "sprites");
        foreach (var (key, top, side) in Sheets)
        {
            var material = _world!.Materials.Values.FirstOrDefault(item => item.Key == key);
            if (material is null) continue;
            var topImage = Image.LoadFromFile(Path.Combine(sprites, top));
            var sideImage = Image.LoadFromFile(Path.Combine(sprites, side));
            topImage.Convert(Image.Format.Rgba8);
            sideImage.Convert(Image.Format.Rgba8);
            _topLayers[material.Id] = tops.Count;
            _sideLayers[material.Id] = sides.Count;
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
    }

    private void RebuildChunks(WorldFrame frame, Dictionary<(int Cx, int Cy), HostChunk> current)
    {
        foreach (var node in _chunkMeshes.Values) node.QueueFree();
        _chunkMeshes.Clear();
        var mesher = new ChunkMesher(_world!, _topLayers, _sideLayers, _terrainMat, _structureMat, _glassMat);
        foreach (var chunk in frame.Chunks)
        foreach (var (z, result) in mesher.Build(chunk.Cx, chunk.Cy))
        {
            if (result.Mesh is null) continue;
            var node = new MeshInstance3D
            {
                Name = $"chunk_{chunk.Cx}_{chunk.Cy}_{z}",
                Mesh = result.Mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
            };
            _root.AddChild(node);
            _chunkMeshes[(chunk.Cx, chunk.Cy, z)] = node;
        }
        _renderedChunks.Clear();
        foreach (var pair in current) _renderedChunks[pair.Key] = pair.Value;
    }

    private void UpdateActors(WorldFrame frame)
    {
        var current = frame.Actors.Select(actor => actor.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _actors.Keys.Where(id => !current.Contains(id)).ToArray())
        {
            _actors[stale].QueueFree();
            _actors.Remove(stale);
            _actorTargets.Remove(stale);
        }
        foreach (var actor in frame.Actors)
        {
            var feet = new Vector3((float)actor.X + 0.5f, actor.H * 0.5f, (float)actor.Y + 0.5f);
            var target = feet + new Vector3(0, 0.85f, 0);
            if (!_actors.TryGetValue(actor.Id, out var node))
            {
                var isPlayer = actor.Id == Ids.Player;
                var silhouette = new ShaderMaterial
                {
                    Shader = new Shader
                    {
                        Code = "shader_type spatial; render_mode unshaded, depth_test_inverted, depth_draw_never, cull_back; void fragment() { ALBEDO = vec3(0.55, 0.75, 1.0); ALPHA = 0.7; }",
                    },
                    RenderPriority = 10,
                };
                var material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(isPlayer ? "#2a7fff" : "#d48b55"),
                    NextPass = silhouette,
                };
                node = new MeshInstance3D
                {
                    Name = actor.Id,
                    Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.7f, Material = material },
                };
                _root.AddChild(node);
                node.Position = target;
                _actors[actor.Id] = node;
            }
            _actorTargets[actor.Id] = target;
        }
    }

    private void SendMovement(double delta)
    {
        if (_scripted || _frame is null || _host is null) return;
        var kx = (Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        var ky = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
        var direction = new Vector2(kx + ky, ky - kx);
        if (direction == Vector2.Zero)
        {
            _moveAccumulator = 0;
            return;
        }
        direction = direction.Normalized();
        _moveAccumulator += delta;
        const double moveInterval = 1.0 / 20;
        while (_moveAccumulator >= moveInterval)
        {
            if (!_host.TryMove(direction.X, direction.Y, moveInterval))
            {
                _moveAccumulator = Math.Min(_moveAccumulator, moveInterval);
                break;
            }
            _moveAccumulator -= moveInterval;
        }
    }

    private void FollowPlayer()
    {
        if (!_actors.TryGetValue(Ids.Player, out var player)) return;
        var feet = player.Position - new Vector3(0, 0.85f, 0);
        _view.Follow(feet * new Vector3(1, PixelView.VerticalScale, 1) + new Vector3(0, 0.6f, 0));
        if (_world is null || _frame is null) return;
        var clip = _cutaway ? Cutaway.ClipH(_world, feet.X, feet.Z, _frame.Actors.First(a => a.Id == Ids.Player).H) : float.PositiveInfinity;
        var clipH = float.IsInfinity(clip) ? 100000f : clip;
        _structureMat.SetShaderParameter("clip_h", clipH);
        _glassMat.SetShaderParameter("clip_h", clipH);
    }

    private void UpdateHud()
    {
        if (_frame is not { } frame) return;
        var player = frame.Actors.First(actor => actor.Id == Ids.Player);
        _hud.Text = $"{frame.Generator}  ({player.X:0.0}, {player.Y:0.0}, h={player.H})  " +
            $"{frame.GameMinute / 60:00}:{frame.GameMinute % 60:00}  x{frame.Speed}  " +
            $"{(frame.Paused ? "paused" : "running")}  {Engine.GetFramesPerSecond()} fps";
        _pauseButton.Text = frame.Paused ? "Resume" : "Pause";
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
        if (_terrainMat is null) return;
        _terrainMat.SetShaderParameter("tex_mode", mode);
        _structureMat.SetShaderParameter("tex_mode", mode);
    }

    private async Task RunShots(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var scale in new[] { 1, 2, 4 })
        {
            _view.SetScale(scale);
            await Frames(8);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, $"spawn-x{scale}.png"));
        }
        GetTree().Quit();
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    public override void _ExitTree()
    {
        _host?.Dispose();
        base._ExitTree();
    }
}
