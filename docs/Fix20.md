# Fix20: Faster start to the first frame

[Performance] [Startup] [Tooling] [Review]

The game takes about 2.6 s from launch to its first drawn frame on a warm start, and 3.4–4.7 s on
the first launch after new files land. Most of that is the engine itself, but about 0.55 s is ours:
code compiled on first use, work done one step after another that could overlap, one shader
compiled per actor, and chunk meshing on a single thread. This doc removes that part.

Reference: `docs/done/Fix18.md` and `docs/done/Fix19.md` (remesh, `WorldClient`); `docs/PENDING.md`
(chunk meshing hitch); `CONTEXT.md` (host contract, commands).

## 0. Findings (measured 26/09/2026)

Method: an ExportRelease copy of the export in the scratchpad, a copy of the user's own save
(4.4 MB: 64 chunks, 7 actors, 1,791 events), temporary timers (removed), `--verbose` output with a
timestamp on every line, and 3 runs per variant. Times count from process launch, warm runs.

| # | Phase | Time | Whose |
|---|---|---|---|
| F1 | Loading the 112 MB executable, before Godot prints anything (`--version` alone takes 650–690 ms) | ~620 ms | Engine template and OS. The pck is small (assets 207 KB), and the export already uses the release template |
| F2 | Vulkan and device init (one harmless error from a stale NVIDIA Nsight 2019 layer entry) | ~480 ms | Engine and system |
| F3 | Forward+ loads its internal shaders from the cache (FSR2, SDFGI, SSIL, SSR, fog…) | ~770 ms | Engine; fixed by the renderer choice, and SSAO needs Forward+ |
| F4 | .NET start and script load | ~200 ms | Engine, plus our JIT |
| F5 | `_Ready`: environment 12 ms, HUD 84 ms, script loads | 110–230 ms | Ours |
| F6 | `SimulationHost` starts only at the **end** of `_Ready`, then opens the save (~150 ms) and loads the world (~90 ms). The data is tiny, so this is mostly first-use JIT | ~270 ms | Ours, on the critical path |
| F7 | First frame: `UpdateActors` **~90–110 ms**. Each actor builds its own `Shader` for the silhouette pass, so 7 actors compile the same shader 7 times. Still ~90 ms with ReadyToRun, so it is shader work, not JIT | ~100 ms | Ours |
| F8 | First frame: sprite PNG decode 57–66 ms, then meshing 20 chunks 95–109 ms, both on the main thread | ~165 ms | Ours |
| F9 | ReadyToRun, tested: `dotnet publish -c ExportRelease -r win-x64 -p:PublishReadyToRun=true` works with Godot.NET.Sdk. Warm first frame 2.52–2.56 s → **2.20–2.39 s** (host load 240 → 150 ms, meshing 100 → 60 ms). Its own first run was cold (3.4 s) while the new files were scanned | −200 to −300 ms | Ours |
| F10 | Accessibility off (`override.cfg`, AccessKit disabled) | −80 ms | Rejected [Sec. 8] |

## 1. Decisions

| Topic | Decision | Why |
|---|---|---|
| Host first | Create `SimulationHost` at the very start of `_Ready`, right after reading the arguments and before the view, the environment and the HUD | Its ~270 ms load (F6) then overlaps the ~110–230 ms of `_Ready` (F5) instead of following it. The host touches no Godot node |
| Sprites in the background | `_Ready` starts a `Task` that decodes and converts the 12 sprite PNGs into `Image`s. `BuildTerrainMaterials` awaits it and keeps only the id-to-layer mapping, which needs the world's materials | F8's 60 ms of decoding overlaps the host load. Decoding an `Image` off the main thread is supported. The Texture2DArray is still created on the main thread |
| Shared actor materials | One silhouette `ShaderMaterial` (one `Shader`), plus one `StandardMaterial3D` for Niko and one for Extras, created once and shared by every actor, together with one shared `CapsuleMesh` | F7: one compile instead of one per actor. It also removes the per-spawn cost when Extras appear. Nothing changes any actor's material on its own |
| Parallel meshing | Split `ChunkMesher.Build` in two: `BuildGeometry(cx, cy)`, which is pure CPU and can run on any thread, returns the vertex lists per band, and `ToMeshes(geometry)` creates the `ArrayMesh` on the main thread. `RebuildChunks` runs `BuildGeometry` for the chunks it must build with `Parallel.ForEach`, then creates meshes and nodes on the main thread in a fixed order | F8 at start (20 chunks), plus the border-crossing hitch in `PENDING.md` (23–37 ms). The roof-band set is computed once in the constructor, not lazily, so worker threads never race on it |
| ReadyToRun | `game/EtherBound.Game.csproj` sets `PublishReadyToRun=true`. It only affects publish, and the Godot export is a publish with a runtime identifier, so editor builds and `dotnet build` stay as they are | F9: the largest single win. The sim and host assemblies are compiled with it, because publish covers referenced projects |
| Swapping DLLs without the editor | New `scripts/publish-game.mjs`: runs the R2R publish into a temp folder and copies the managed DLLs (EtherBound.*, GodotSharp, SQLite and the rest already in `data_EtherBound.Game_windows_x86_64/`) over the export, after checking that the game is not running | That is how the export was updated in Fix18 and Fix19, by hand. It is also the only way to check F9 here, with no Godot editor on this machine |
| Stated limit | Engine boot (F1–F4, about 2 s) stays. The plan's target is warm launch to first frame at **≤ 2.1 s** on this machine (from about 2.6 s) | Nothing we control in the project shortens exe loading, Vulkan init or Forward+'s shader set |

## 2. Changes

**game.app** (`game/app/WorldClient.cs`, `game/EtherBound.Game.csproj`)
- `_Ready`: arguments, maximize (already done), `SimulationHost`, the sprite `Task`, then the view,
  environment and HUD, as before.
- `BuildTerrainMaterials`: takes the decoded images from the task (`Task.Result` on first use, by
  then long finished) and builds the arrays and materials as today.
- `UpdateActors`: the shared materials and mesh as in [Sec. 1], created once in `_Ready`.
- `RebuildChunks`: `Parallel.ForEach` over the keys to build, into a
  `ConcurrentDictionary<(cx, cy), geometry>`, then meshes and nodes on the main thread in `frame.Chunks`
  order. Node names and the `_chunkMeshes` keys do not change.
- The csproj adds `<PublishReadyToRun>true</PublishReadyToRun>`.

**game.render** (`game/spike/ChunkMesher.cs`): `BuildGeometry` and `ToMeshes` as in [Sec. 1].
`Build` stays as `ToMeshes(BuildGeometry(…))`, so `WorldDump`-based tools keep working. Roof bands
are computed in the constructor.

**tooling** (`scripts/publish-game.mjs`): as in [Sec. 1], with an optional `--export DIR`.

No change to the sim, the host contract, the save or the rendering settings.

## 3. What must not break

- Every chunk mesh is identical to today's. The Fix18 check (compare every rebuilt mesh with a
  from-scratch build) still gives 0 differences, now against the parallel path.
- Mesh order and node names are deterministic, so `--shots` output is unchanged.
- No Godot node, `ArrayMesh` or `Texture2DArray` is touched off the main thread. Only
  `BuildGeometry` and `Image` decoding run on workers.
- The walk trace stays as in Fix19: every walking frame within 5 % on `lab` (`--trace-walk`).
- Actors look the same. Niko stays blue and Extras stay orange, with the silhouette through walls.
- `dotnet build` and `npm run check` behave as before. ReadyToRun only applies at publish.

## 4. Acceptance

### Automated

1. `npm run check`.
2. `sim/EtherBound.Sim.Tests` gets no new cases. The changed code is Godot-side, and
   `ChunkMesher` needs Godot types. [Sec. 3] is checked by manual 4.

### Manual

3. Manual, with numbers (Godot client needed): 5 warm launches against a copy of the user's save
   with the temporary phase timers of [Sec. 0]. The median launch to first frame is **≤ 2.1 s**, and
   each changed phase shows its gain (actors ≤ 20 ms, host ready before `_Ready` ends or at most
   50 ms after, meshing ≤ 40 ms). The numbers go into [Sec. 0].
4. Manual: the Fix18 remesh check (temporary, walking across borders on `test` and `lab`) gives 0
   differences with parallel meshing, and the crossing cost is recorded. `--shots` gives the same
   images as before the change (pixel compare). `--trace-walk` passes as in Fix19.
5. Manual: `node scripts/publish-game.mjs` updates the export, the export starts maximized, and
   the DLL sizes show R2R (EtherBound.Sim.dll > 800 KB).
6. Manual, for the user: the next export from the Godot editor also gives R2R DLLs. That checks
   that the editor's export honours the csproj setting.

## 5. Docs to update in the same change

- `CONTEXT.md`: `scripts/publish-game.mjs` in the commands, ReadyToRun in the game.app row, and
  the startup order in `_Ready` (host first).
- `docs/utils/PITFALLS.md` [Sec. 5]: `ChunkMesher.BuildGeometry` is thread-safe and `ToMeshes`
  is not; roof bands are computed up front. [Sec. 8]: actor materials are shared, so never change
  one per actor.
- `docs/PENDING.md`: update the meshing-hitch item with the new crossing cost. Add the NVIDIA
  Nsight layer note as optional system cleanup for the user.
- `docs/utils/VERSION.md`, the Notion Systems Index, the Dev Blog entry and the daily Work Report.

## 6. Modules and versions

| Module | Change | Version |
|---|---|---|
| game.app | Host first, sprite task, shared materials, parallel remesh, R2R | v0.0.7 (see below) |
| game.render | `BuildGeometry` / `ToMeshes`, roof bands up front | v0.0.5 (see below) |
| tooling | `scripts/publish-game.mjs` | v0.0.20 (see below) |

Fix18, Fix19 and the user's own changes are still uncommitted. In one commit every module moves by
+0.0.1 against `HEAD` once, so these stay where the working tree has them. `game.app` needs v0.0.5
against `HEAD` v0.0.4 (Fix19 [Sec. 6]). If Fix18/19 are committed first, each module here gets
+0.0.1 on top.

## 7. Todo

- [ ] `WorldClient._Ready` order; the sprite task; `BuildTerrainMaterials` takes its images
- [ ] Shared actor materials and mesh
- [ ] `ChunkMesher.BuildGeometry` / `ToMeshes`, roof bands in the constructor; parallel `RebuildChunks`
- [ ] `PublishReadyToRun` in the csproj; `scripts/publish-game.mjs`
- [ ] `npm run check`; manual 3–5 with their numbers in [Sec. 0]
- [ ] Docs of [Sec. 5]
- [ ] Move this doc to `docs/done/`

## 8. Out of scope

- **Disabling accessibility (F10).** It saves 80 ms but removes Narrator support, which the UI
  has been checked against since Fix17.
- **Another renderer.** Mobile or Compatibility would load fewer shaders (F3), but they lose the
  SSAO and shadow look the game is built on (Dev-025).
- **The executable (F1).** A smaller custom template or code signing (which can let antivirus skip
  a rescan) are distribution topics, not code.
- **Streaming the first chunks (drawing the player's chunk first, the rest later).** It gives
  visible pop-in at the edges, and parallel meshing already brings the full set under ~40 ms.

---

## TL;DR

Launch to first frame is about 2.6 s warm. About 2 s of that is the engine (loading the exe,
Vulkan, Forward+ shaders, .NET) and stays. The ~0.55 s that is ours goes like this: the sim starts
loading the save at the very start of `_Ready` instead of the end, sprites decode on a background
task, actors share one silhouette shader instead of compiling it 7 times, chunk geometry is built on
all cores, and the assemblies ship precompiled with ReadyToRun (tested: −200 to −300 ms). The target
is ≤ 2.1 s. A new `scripts/publish-game.mjs` updates the export's DLLs without the Godot editor.
