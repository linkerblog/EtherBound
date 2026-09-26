using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EtherBound.Host;
using EtherBound.Game.Spike;
using EtherBound.Game.Ui;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
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
    private TrajectoryAnimator _trajectoryAnimator = null!;
    private ShaderMaterial _terrainMat = null!, _structureMat = null!, _glassMat = null!;
    private Godot.Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private GameHud _gameHud = null!;
    private ActionMenuOverlay _actionMenu = null!;
    private Dictionary<int, int> _topLayers = new();
    private Dictionary<int, int> _sideLayers = new();
    private long _lastSequence;
    private double _moveAccumulator;
    private bool _cutaway = true;
    private bool _scripted;
    private bool _shotsStarted;
    private int _texMode;
    private int _requestId;
    private int _pendingContextRequest;
    private int _pendingRadialRequest;
    private int _pendingNewGameRequest;
    private Vector2 _contextPosition;
    private string? _lastFault;

    public override void _Ready()
    {
        var raw = OS.GetCmdlineUserArgs();
        for (var i = 0; i < raw.Length; i++)
            if (raw[i].StartsWith("--")) _args[raw[i][2..]] = i + 1 < raw.Length && !raw[i + 1].StartsWith("--") ? raw[++i] : "";

        _view = new PixelView();
        AddChild(_view);
        _root = new Node3D { Scale = new Vector3(1, PixelView.VerticalScale, 1) };
        _view.Viewport.AddChild(_root);
        _trajectoryAnimator = new TrajectoryAnimator();
        _root.AddChild(_trajectoryAnimator);
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
        _gameHud = new GameHud();
        _gameHud.SpeedRequested += speed => _host?.TrySetClock(speed: speed);
        _gameHud.PauseRequested += paused => _host?.TrySetClock(paused: paused);
        _gameHud.NewGameRequested += request =>
        {
            var requestId = ++_requestId;
            _pendingNewGameRequest = requestId;
            if (_host?.TryNewGame(requestId, request.Seed, request.Generator, request.OptionsJson, request.Paused) != true)
                _gameHud.PushFeed("SIM BUSY", "warn");
        };
        _gameHud.FreeTextSubmitted += text => _gameHud.PushFeed($"> {text}", "info");
        _gameHud.ViewChanged += _ =>
        {
            _actionMenu.Close();
            _moveAccumulator = 0;
        };
        _actionMenu = _gameHud.ActionMenus;
        _actionMenu.ActionSelected += action =>
        {
            var requestId = ++_requestId;
            if (_host?.TrySubmitAction(requestId, action) != true) _gameHud.PushFeed("SIM BUSY", "warn");
        };
        AddChild(_gameHud);
    }

    public override void _Process(double delta)
    {
        if (_host?.LatestFrame is { } latest && latest.Sequence != _lastSequence) ApplyFrame(latest);
        DrainHostResponses();
        if (_host?.Fault is { } fault && _lastFault != fault.Message)
        {
            _lastFault = fault.Message;
            _gameHud.PushFeed($"SIMULATION STOPPED · {fault.Message}", "fail");
        }

        foreach (var (id, actor) in _actors)
            if (_actorTargets.TryGetValue(id, out var target)) actor.Position = actor.Position.MoveToward(target, (float)delta * 12f);
        _trajectoryAnimator.Advance(delta);
        FollowPlayer();
        SendMovement(delta);
    }

    private void ApplyFrame(WorldFrame frame)
    {
        _frame = frame;
        _gameHud.UpdateFrame(frame);
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
        var sprites = Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets", "sprites");
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
            var feet = new Vector3((float)actor.X, actor.H * 0.5f, (float)actor.Y);
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
        if (_scripted || _frame is null || _host is null || !_gameHud.CanMoveWorld) return;
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

    private void DrainHostResponses()
    {
        if (_host is null) return;
        while (_host.TryReadResponse(out var response))
        {
            switch (response)
            {
                case HostMenuResponse menu when menu.RequestId == _pendingContextRequest:
                    _pendingContextRequest = 0;
                    if (menu.Menu is not null) _actionMenu.ShowContext(menu.Menu, _contextPosition);
                    else _gameHud.PushFeed("NOTHING HERE", "warn");
                    break;
                case HostMenuResponse radial when radial.RequestId == _pendingRadialRequest:
                    _pendingRadialRequest = 0;
                    if (radial.Menu is not null) _actionMenu.ShowRadial(radial.Menu, PlayerScreenPosition());
                    break;
                case HostActionResponse action:
                    if (!action.Result.Trajectory.IsDefaultOrEmpty) _trajectoryAnimator.Play(action.Result.Trajectory);
                    _gameHud.PushFeed(action.Result.Text ?? (action.Result.Accepted
                        ? action.Result.Action.Op.ToUpperInvariant()
                        : $"CAN'T {action.Result.Action.Op.ToUpperInvariant()} · {action.Result.Reason}"),
                        action.Result.Accepted ? "act" : "warn");
                    break;
                case HostActivityNotice notice:
                    _gameHud.PushFeed(notice.Outcome == "completed" ? $"{notice.Op.ToUpperInvariant()} DONE" :
                        $"{notice.Op.ToUpperInvariant()} {notice.Outcome.ToUpperInvariant()} {notice.Reason}",
                        notice.Outcome == "completed" ? "act" : "fail");
                    break;
                case HostNewGameResponse game when game.RequestId == _pendingNewGameRequest:
                    _pendingNewGameRequest = 0;
                    _gameHud.CloseNewGame();
                    _gameHud.ShowGeneratorError("");
                    _gameHud.PushFeed($"NEW WORLD · {game.Generator.ToUpperInvariant()} · SEED {game.Seed}", "seen");
                    break;
                case HostErrorResponse error:
                    if (error.RequestId == _pendingNewGameRequest)
                    {
                        _pendingNewGameRequest = 0;
                        _gameHud.ShowGeneratorError(error.Message);
                    }
                    _gameHud.PushFeed(error.Message.ToUpperInvariant(), "fail");
                    break;
            }
        }
    }

    private Vector2 PlayerScreenPosition()
    {
        if (!_actors.TryGetValue(Ids.Player, out var player)) return GetViewport().GetVisibleRect().Size * 0.5f;
        return _view.ScreenFromWorld(_root.ToGlobal(player.Position));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouse)
        {
            if (_gameHud.ActiveView != "GAME" || _gameHud.InputHasFocus || _gameHud.NewGameVisible) return;
            _actionMenu.Close();
            var (origin, direction) = _view.RayFromScreen(mouse.Position);
            var ray = new WorldRay(origin.X, origin.Z, origin.Y, direction.X, direction.Z, direction.Y);
            _pendingContextRequest = ++_requestId;
            _contextPosition = mouse.Position;
            if (_host?.TryRequestMenuAtRay(_pendingContextRequest, ray) != true) _gameHud.PushFeed("SIM BUSY", "warn");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_gameHud.ActiveView != "GAME" || _gameHud.InputHasFocus) return;
        switch (key.Keycode)
        {
            case Key.Key1: _view.SetScale(1); break;
            case Key.Key2: _view.SetScale(2); break;
            case Key.Key4: _view.SetScale(4); break;
            case Key.T: SetTexMode(1 - _texMode); break;
            case Key.C: _cutaway = !_cutaway; break;
            case Key.O: _env.SsaoEnabled = !_env.SsaoEnabled; break;
            case Key.H: _sun.ShadowEnabled = !_sun.ShadowEnabled; break;
            case Key.V:
                if (_actionMenu.Visible) _actionMenu.Close();
                else
                {
                    _pendingRadialRequest = ++_requestId;
                    if (_host?.TryRequestRadialMenu(_pendingRadialRequest) != true) _gameHud.PushFeed("SIM BUSY", "warn");
                }
                GetViewport().SetInputAsHandled();
                break;
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

        _pendingRadialRequest = ++_requestId;
        _host?.TryRequestRadialMenu(_pendingRadialRequest);
        if (!await WaitUntil(() => _actionMenu.Visible, 300)) GD.PrintErr("shots: radial menu did not open in time");
        await Frames(2);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "radial-menu.png"));
        _actionMenu.Close();

        if (_frame is { } frame)
        {
            var player = frame.Actors.First(actor => actor.Id == Ids.Player);
            var ray = new WorldRay(player.X, player.Y, player.H * 0.5 + 3, 0, 0, -1);
            _contextPosition = GetViewport().GetVisibleRect().Size * 0.55f;
            _pendingContextRequest = ++_requestId;
            _host?.TryRequestMenuAtRay(_pendingContextRequest, ray);
            if (!await WaitUntil(() => _actionMenu.Visible, 300)) GD.PrintErr("shots: context menu did not open in time");
            await Frames(2);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "context-menu.png"));
            _actionMenu.Close();
        }
        _gameHud.ShowNewGame();
        await Frames(4);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "new-game.png"));
        _gameHud.CloseNewGame();
        _gameHud.SwitchView("DEBUG");
        await Frames(4);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "debug-map.png"));
        _gameHud.SwitchView("LLM");
        await Frames(4);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "llm-placeholder.png"));
        GetTree().Quit();
    }

    private async Task<bool> WaitUntil(Func<bool> condition, int maxFrames)
    {
        for (var i = 0; i < maxFrames && !condition(); i++) await Frames(1);
        return condition();
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
