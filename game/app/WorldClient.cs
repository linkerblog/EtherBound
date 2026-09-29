using System;
using System.Collections.Concurrent;
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
    private sealed record DecodedSprites(Image Top, Image Side, Image? CliffSide);

    private static readonly (string Key, string Top, string Side, string? CliffSide)[] Sheets =
    {
        ("grass", "grass/grass_x4.png", "grass/grass_side_x4.png", "grass/grass_cliff_side_x4.png"),
        ("wood_floor", "floor/planks_x4.png", "floor/planks_side_x4.png", null),
        ("concrete", "floor/concrete_x4.png", "floor/concrete_side_x4.png", null),
        ("asphalt", "floor/asphalt_x4.png", "floor/asphalt_side_x4.png", null),
        ("roofing", "floor/roofing_x4.png", "floor/roofing_side_x4.png", null),
        ("brick", "wall/brick_x4.png", "wall/brick_side_x4.png", null),
    };

    private readonly Dictionary<(int Cx, int Cy, int Z), MeshInstance3D> _chunkMeshes = new();
    private readonly Dictionary<(int Cx, int Cy), HostChunk> _renderedChunks = new();
    private HashSet<int> _roofBands = new();
    private readonly Dictionary<string, MeshInstance3D> _actors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActorMotion> _actorMotions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _args = new(StringComparer.Ordinal);
    private CapsuleMesh _actorMesh = null!;
    private StandardMaterial3D _playerActorMaterial = null!, _extraActorMaterial = null!;
    private Task<Dictionary<string, DecodedSprites>>? _spriteDecode;
    private SimulationHost? _host;
    private PixelView _view = null!;
    private Node3D _root = null!;
    private WorldDump? _world;
    private WorldFrame? _frame;
    private TrajectoryAnimator _trajectoryAnimator = null!;
    private ShaderMaterial _terrainMat = null!, _structureMat = null!, _glassMat = null!, _outlineMat = null!;
    private Godot.Environment _env = null!;
    private DirectionalLight3D _sun = null!;
    private GameHud _gameHud = null!;
    private ActionMenuOverlay _actionMenu = null!;
    private Dictionary<int, int> _topLayers = new();
    private Dictionary<int, int> _sideLayers = new();
    private Dictionary<int, int> _cliffSideLayers = new();
    private long _lastSequence;
    private double _moveAccumulator;
    private double _sinceLastStep = MoveInterval;
    private bool _walking;
    private readonly StepPlayout _playout = new();
    private bool _playoutOwnsPlayer;
    private long _movesSent;
    private StreamWriter? _trace;
    private double _traceElapsed;
    private bool _cutaway = true;
    private bool _levelOnlyView;
    private bool _scripted;
    private bool _shotsStarted;
    private int _texMode;
    private int _requestId;
    private int _pendingContextRequest;
    private int _pendingRadialRequest;
    private int _pendingNewGameRequest;
    private Vector2 _contextPosition;
    private string? _lastFault;

    private const double MoveInterval = 1.0 / SimulationHost.MoveHz;

    public override void _Ready()
    {
        var raw = OS.GetCmdlineUserArgs();
        for (var i = 0; i < raw.Length; i++)
            if (raw[i].StartsWith("--")) _args[raw[i][2..]] = i + 1 < raw.Length && !raw[i + 1].StartsWith("--") ? raw[++i] : "";
        _levelOnlyView = _args.ContainsKey("shots-level-only") && _args.ContainsKey("shots");
        // Screenshots keep the project's fixed 1280x720 so every capture has the same size.
        if (!_args.ContainsKey("shots")) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);

        var seed = _args.TryGetValue("seed", out var rawSeed) && long.TryParse(rawSeed, out var parsedSeed) ? parsedSeed : 7;
        var generator = _args.GetValueOrDefault("generator", "test");
        var databasePath = _args.GetValueOrDefault("database", Path.Combine(OS.GetUserDataDir(), "etherbound.db"));
        _host = new SimulationHost(databasePath, seed, generator);
        GD.Print($"world client started: seed {seed}, generator {generator}");

        var sprites = Path.Combine(ProjectSettings.GlobalizePath("res://"), "assets", "sprites");
        _spriteDecode = Task.Run(() => DecodeSprites(sprites));

        _view = new PixelView();
        AddChild(_view);
        _root = new Node3D { Scale = new Vector3(1, PixelView.VerticalScale, 1) };
        _view.Viewport.AddChild(_root);
        _trajectoryAnimator = new TrajectoryAnimator();
        _root.AddChild(_trajectoryAnimator);
        BuildActorMaterials();
        BuildEnvironment();
        BuildHud();
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
            SsaoRadius = 0.9f,
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
            DirectionalShadowMaxDistance = 28f,
            ShadowBias = 0.04f,
            ShadowNormalBias = 0.5f,
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
            _walking = false;
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
        var arrived = _host?.LatestFrame is { } latest && latest.Sequence != _lastSequence;
        if (arrived) ApplyFrame(_host!.LatestFrame!);
        DrainHostResponses();
        if (_host?.Fault is { } fault && _lastFault != fault.Message)
        {
            _lastFault = fault.Message;
            _gameHud.PushFeed($"SIMULATION STOPPED · {fault.Message}", "fail");
        }

        var now = Now;
        foreach (var (id, actor) in _actors)
        {
            var playout = id == Ids.Player ? _playout.Sample(now, delta) : null;
            if (playout is { } scheduled) actor.Position = ToVector(scheduled);
            else if (_actorMotions.TryGetValue(id, out var motion)) actor.Position = ToVector(motion.Sample(now));
        }
        _trajectoryAnimator.Advance(delta);
        FollowPlayer();
        SendMovement(delta, now);
        TraceWalk(delta, arrived);
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
        var decoded = (_spriteDecode ?? throw new InvalidOperationException("sprite decode was not started"))
            .GetAwaiter().GetResult();
        var shader = GD.Load<Shader>("res://spike/Terrain.gdshader");
        var tops = new Godot.Collections.Array<Image>();
        var sides = new Godot.Collections.Array<Image>();
        _topLayers = new Dictionary<int, int>();
        _sideLayers = new Dictionary<int, int>();
        _cliffSideLayers = new Dictionary<int, int>();
        foreach (var sheet in Sheets)
        {
            var material = _world!.Materials.Values.FirstOrDefault(item => item.Key == sheet.Key);
            if (material is null) continue;
            var images = decoded[sheet.Key];
            _topLayers[material.Id] = tops.Count;
            _sideLayers[material.Id] = sides.Count;
            tops.Add(images.Top);
            sides.Add(images.Side);
            if (images.CliffSide is { } cliffImage)
            {
                _cliffSideLayers[material.Id] = sides.Count;
                sides.Add(cliffImage);
            }
        }
        var topArray = new Texture2DArray();
        topArray.CreateFromImages(tops);
        var sideArray = new Texture2DArray();
        sideArray.CreateFromImages(sides);
        _spriteDecode = null;
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
                    uniform bool band_cut = false;
                    uniform int viewer_band = 30;
                    varying float hh;
                    void vertex() { hh = VERTEX.y * 2.0; }
                    void fragment() {
                        if (band_cut && int(floor(hh / 6.0)) > viewer_band) discard;
                        if (hh > clip_h + 0.001) discard;
                        ALBEDO = vec3(0.686, 0.791, 0.871);
                        ALPHA = 0.45;
                    }
                    """,
            },
        };
        _outlineMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, cull_disabled, depth_draw_opaque;
                    uniform bool cliff_cutaway_enabled = false;
                    uniform vec3 cliff_cut_center = vec3(0.0);
                    uniform bool cursor_cutaway_enabled = false;
                    uniform vec3 cursor_cut_center = vec3(0.0);
                    uniform vec3 cliff_cut_right = vec3(1.0, 0.0, 0.0);
                    uniform vec3 cliff_cut_up = vec3(0.0, 1.0, 0.0);
                    uniform vec3 cliff_cut_back = vec3(0.5773503);
                    uniform float cliff_cut_radius = 6.0;
                    uniform float viewer_h = 0.0;
                    varying vec3 wpos;
                    void vertex() { wpos = VERTEX; }
                    bool inside_window(vec3 center, bool front_cut) {
                        vec3 offset = wpos - center;
                        float len = length(vec2(dot(offset, cliff_cut_right), dot(offset, cliff_cut_up)));
                        return len < cliff_cut_radius && (wpos.y * 2.0 - viewer_h) > 1.0
                            && (!front_cut || dot(offset, cliff_cut_back) > 0.0);
                    }
                    void fragment() {
                        bool inside = (cliff_cutaway_enabled && inside_window(cliff_cut_center, true))
                            || (cursor_cutaway_enabled && inside_window(cursor_cut_center, false));
                        if (!inside) discard;
                        ALBEDO = COLOR.rgb;
                    }
                    """,
            },
        };
    }

    private static Dictionary<string, DecodedSprites> DecodeSprites(string sprites)
    {
        var result = new Dictionary<string, DecodedSprites>(StringComparer.Ordinal);
        foreach (var (key, top, side, cliffSide) in Sheets)
        {
            var topImage = DecodeImage(Path.Combine(sprites, top));
            var sideImage = DecodeImage(Path.Combine(sprites, side));
            var cliffImage = cliffSide is null ? null : DecodeImage(Path.Combine(sprites, cliffSide));
            result.Add(key, new DecodedSprites(topImage, sideImage, cliffImage));
        }
        return result;
    }

    private static Image DecodeImage(string path)
    {
        var image = Image.LoadFromFile(path);
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    private void BuildActorMaterials()
    {
        var silhouette = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = "shader_type spatial; render_mode unshaded, depth_test_inverted, depth_draw_never, cull_back; void fragment() { ALBEDO = vec3(0.55, 0.75, 1.0); ALPHA = 0.7; }",
            },
            RenderPriority = 10,
        };
        _playerActorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("#2a7fff"),
            NextPass = silhouette,
        };
        _extraActorMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("#d48b55"),
            NextPass = silhouette,
        };
        _actorMesh = new CapsuleMesh { Radius = Cutaway.BodyRadius, Height = Cutaway.BodyHeight };
    }

    private void RebuildChunks(WorldFrame frame, Dictionary<(int Cx, int Cy), HostChunk> current)
    {
        var roofBands = ChunkMesher.RoofBands(_world!);
        var build = roofBands.SetEquals(_roofBands) ? ChunksToBuild(current) : current.Keys.ToHashSet();
        _roofBands = roofBands;
        foreach (var key in _chunkMeshes.Keys.Where(key => build.Contains((key.Cx, key.Cy)) || !current.ContainsKey((key.Cx, key.Cy))).ToArray())
        {
            _chunkMeshes[key].QueueFree();
            _chunkMeshes.Remove(key);
        }
        var mesher = new ChunkMesher(_world!, _topLayers, _sideLayers, _cliffSideLayers, _terrainMat, _structureMat,
            _glassMat, _outlineMat);
        var geometries = new ConcurrentDictionary<(int Cx, int Cy), ChunkMesher.ChunkGeometry>();
        Parallel.ForEach(build, key => geometries[key] = mesher.BuildGeometry(key.Cx, key.Cy));
        foreach (var chunk in frame.Chunks)
        {
            if (!build.Contains((chunk.Cx, chunk.Cy))) continue;
            foreach (var (z, result) in mesher.ToMeshes(geometries[(chunk.Cx, chunk.Cy)]))
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
        }
        _renderedChunks.Clear();
        foreach (var pair in current) _renderedChunks[pair.Key] = pair.Value;
    }

    // The mesher reads one cell past a chunk on every side (edge faces, wall corners), so a new,
    // changed or dropped chunk also reshapes its four neighbours.
    private HashSet<(int Cx, int Cy)> ChunksToBuild(Dictionary<(int Cx, int Cy), HostChunk> current)
    {
        var changed = current.Where(pair => !_renderedChunks.TryGetValue(pair.Key, out var old) || !ReferenceEquals(old, pair.Value))
            .Select(pair => pair.Key)
            .Concat(_renderedChunks.Keys.Where(key => !current.ContainsKey(key)));
        return changed
            .SelectMany(key => new[] { key, (key.Cx - 1, key.Cy), (key.Cx + 1, key.Cy), (key.Cx, key.Cy - 1), (key.Cx, key.Cy + 1) })
            .Where(current.ContainsKey)
            .ToHashSet();
    }

    private void UpdateActors(WorldFrame frame)
    {
        var current = frame.Actors.Select(actor => actor.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _actors.Keys.Where(id => !current.Contains(id)).ToArray())
        {
            _actors[stale].QueueFree();
            _actors.Remove(stale);
            _actorMotions.Remove(stale);
        }
        var now = Now;
        // Niko moves once per Move step, everyone else once per clock tick.
        var tickSeconds = 1.0 / Math.Max(1, frame.Speed);
        foreach (var actor in frame.Actors)
        {
            var target = (actor.X, actor.H * 0.5 + 0.85, actor.Y);
            if (!_actors.TryGetValue(actor.Id, out var node))
            {
                node = new MeshInstance3D
                {
                    Name = actor.Id,
                    Mesh = _actorMesh,
                    MaterialOverride = actor.Id == Ids.Player ? _playerActorMaterial : _extraActorMaterial,
                };
                _root.AddChild(node);
                node.Position = ToVector(target);
                _actors[actor.Id] = node;
                _actorMotions[actor.Id] = ActorMotion.At(target, now);
            }
            if (actor.Id == Ids.Player)
            {
                // WASD steps play on their own schedule; anything else (actions, new game) glides or snaps.
                if (_playout.Applied(frame.MovesApplied, target, now))
                {
                    _playoutOwnsPlayer = true;
                    continue;
                }
                if (_playoutOwnsPlayer) _actorMotions[actor.Id] = ActorMotion.At(FromVector(node.Position), now);
                _playoutOwnsPlayer = false;
            }
            var duration = actor.Id == Ids.Player ? MoveInterval : tickSeconds;
            _actorMotions[actor.Id].Retarget(target, duration, ActorMotion.MaxStep(duration), now);
        }
    }

    private void SendMovement(double delta, double now)
    {
        _sinceLastStep += delta;
        if (_scripted || _frame is null || _host is null || !_gameHud.CanMoveWorld) return;
        var tracing = _trace is not null && _traceElapsed is >= TraceSettle and < TraceSettle + TraceWalkSeconds;
        var kx = (Input.IsKeyPressed(Key.D) || tracing ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0);
        var ky = (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0);
        var direction = new Vector2(kx + ky, ky - kx);
        if (direction == Vector2.Zero)
        {
            _moveAccumulator = 0;
            _walking = false;
            return;
        }
        direction = direction.Normalized();
        // The first step leaves on the key-down frame, but never sooner than one interval after the
        // last one, so tapping is never faster than holding.
        var starting = !_walking;
        _moveAccumulator = _walking ? _moveAccumulator + delta : Math.Min(MoveInterval, _sinceLastStep);
        _walking = true;
        while (_moveAccumulator >= MoveInterval)
        {
            if (!_host.TryMove(direction.X, direction.Y, MoveInterval))
            {
                _moveAccumulator = Math.Min(_moveAccumulator, MoveInterval);
                break;
            }
            _moveAccumulator -= MoveInterval;
            _sinceLastStep = 0;
            // The step's ideal time is when the accumulator crossed the interval, not this frame.
            var idealAt = now - _moveAccumulator;
            if (starting && _actors.TryGetValue(Ids.Player, out var player)) _playout.Anchor(FromVector(player.Position), idealAt);
            starting = false;
            _playout.Sent(++_movesSent, idealAt);
        }
    }

    private static double Now => Time.GetTicksUsec() / 1e6;

    private static Vector3 ToVector((double X, double Y, double Z) position) =>
        new((float)position.X, (float)position.Y, (float)position.Z);

    private static (double X, double Y, double Z) FromVector(Vector3 position) => (position.X, position.Y, position.Z);

    private const double TraceSettle = 1.5, TraceWalkSeconds = 6;

    /// <summary>
    /// <c>--trace-walk PATH</c>: holds D for six seconds after a settle and logs one CSV row per frame,
    /// so walking smoothness is a number (<c>scripts/trace-walk.mjs</c>), not an impression.
    /// </summary>
    private void TraceWalk(double delta, bool arrived)
    {
        if (!_args.TryGetValue("trace-walk", out var path) || _frame is null || !_actors.TryGetValue(Ids.Player, out var player)) return;
        if (_trace is null)
        {
            _trace = new StreamWriter(path) { AutoFlush = true };
            _trace.WriteLine("usec,delta,arrived,moves_applied,x,y,z,cam_x,cam_y,cam_z,rem_x,rem_y,scale");
        }
        _traceElapsed += delta;
        var cam = _view.Camera.Position;
        _trace.WriteLine(string.Join(",", Time.GetTicksUsec(), Fmt(delta), arrived ? 1 : 0, _frame.MovesApplied,
            Fmt(player.Position.X), Fmt(player.Position.Y), Fmt(player.Position.Z), Fmt(cam.X), Fmt(cam.Y), Fmt(cam.Z),
            Fmt(_view.Remainder.X), Fmt(_view.Remainder.Y), _view.Scale));
        if (_traceElapsed < TraceSettle + TraceWalkSeconds + 0.5) return;
        _trace.Dispose();
        GetTree().Quit();
    }

    private static string Fmt(double value) => value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);

    private void FollowPlayer()
    {
        if (!_actors.TryGetValue(Ids.Player, out var player)) return;
        var feet = player.Position - new Vector3(0, 0.85f, 0);
        _view.Follow(feet * new Vector3(1, PixelView.VerticalScale, 1) + new Vector3(0, 0.6f, 0));
        if (_world is null || _frame is null) return;
        Cutaway.UpdateView(_terrainMat, _structureMat, _glassMat, _outlineMat, _world, feet, player.Position,
            CursorTerrainPoint(), _frame.Actors.First(a => a.Id == Ids.Player).H, _root, _view.Camera,
            _cutaway, _levelOnlyView);
    }

    /// <summary>The terrain point under the cursor in mesh space, while the right button is held.</summary>
    private Vector3? CursorTerrainPoint()
    {
        if (_scripted || !_cutaway || _levelOnlyView || _world is null
            || !Input.IsMouseButtonPressed(MouseButton.Right)
            || _gameHud.ActiveView != "GAME" || _gameHud.InputHasFocus || _gameHud.NewGameVisible)
            return null;
        var (origin, direction) = _view.RayFromScreen(GetViewport().GetMousePosition());
        var basis = _root.GlobalTransform.Basis;
        return Cutaway.CursorTerrain(_world, _root.ToLocal(origin), (basis.Inverse() * direction).Normalized());
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
            case Key.X:
                _levelOnlyView = !_levelOnlyView;
                _gameHud.PushFeed(_levelOnlyView ? "VIEW: CURRENT LEVEL" : "VIEW: AROUND NIKO", "seen");
                GetViewport().SetInputAsHandled();
                break;
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
