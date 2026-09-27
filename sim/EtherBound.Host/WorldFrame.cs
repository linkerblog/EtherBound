using System.Collections.Immutable;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;
using EtherBound.Sim.World;

namespace EtherBound.Host;

public sealed record HostMaterial(int Id, string Key, string Name, string Color, bool Liquid);

public sealed record HostObjectKind(string Key, string Name, string Material, int Height, bool Solid, bool Surface);

public sealed record HostOptionField(string Path, string Label, string Kind, string? DefaultJson, double? Minimum,
    double? Maximum, double? Step, ImmutableArray<string> Choices, string? Group);

public sealed record HostGeneratorBay(string Key, int X, int Y, int Width, int Height);

public sealed record HostGenerator(string Key, string Name, int Version, ImmutableArray<HostOptionField> Fields,
    ImmutableArray<HostGeneratorBay> Bays);

public sealed record HostCarriedObject(int Id, string Kind, string Name, int Quantity, string Slot);

public sealed record HostActivity(string Op, int StartedMinute, int EndsMinute);

public sealed record HostActor(string Id, string Kind, string? Name, double X, double Y, int Z, int H,
    HostActivity? Activity, ImmutableArray<HostCarriedObject> Carried, double LoadKg);

public sealed record HostObject(int Id, string Kind, int X, int Y, int H, int Quantity, bool? Open);

public sealed record HostStratum(int Depth, string Key);

public sealed record HostLevel(int Z, ImmutableArray<short> FloorH, ImmutableArray<ushort> FloorMat,
    ImmutableArray<ushort> WallN, ImmutableArray<ushort> WallW, ImmutableArray<byte> EdgeFlags,
    ImmutableArray<byte> Flags);

public sealed record HostChunk(int Cx, int Cy, int Revision, ImmutableArray<short> GroundH,
    ImmutableArray<ushort> SurfaceMat, ImmutableArray<byte> Dug, ImmutableArray<HostStratum> Strata,
    ImmutableArray<HostLevel> Levels, ImmutableArray<HostObject> Objects)
{
    internal static HostChunk Copy(ChunkPayload payload) => new(
        payload.Cx,
        payload.Cy,
        payload.Revision,
        ImmutableArray.CreateRange(payload.Chunk.GroundH),
        ImmutableArray.CreateRange(payload.Chunk.SurfaceMat),
        ImmutableArray.CreateRange(payload.Chunk.Dug),
        ImmutableArray.CreateRange(payload.Chunk.Strata.Select(s => new HostStratum(s.Depth, s.Key))),
        ImmutableArray.CreateRange(payload.Levels.Select(level => new HostLevel(level.Z,
            ImmutableArray.CreateRange(level.FloorH), ImmutableArray.CreateRange(level.FloorMat),
            ImmutableArray.CreateRange(level.WallN), ImmutableArray.CreateRange(level.WallW),
            ImmutableArray.CreateRange(level.EdgeFlags), ImmutableArray.CreateRange(level.Flags)))),
        ImmutableArray.CreateRange(payload.Objects.Select(o => new HostObject(o.Id, o.Kind, o.X, o.Y, o.H, o.Quantity, o.Open))));
}

/// <summary>
/// A detached render/input snapshot. It contains no sim-owned arrays or mutable JSON nodes.
/// <see cref="MovesApplied"/> counts every <c>Move</c> the host has handled, blocked ones included, so
/// the client can tell which of its steps a frame already shows.
/// </summary>
public sealed record WorldFrame(long Sequence, long Seed, int GameMinute, int Speed, bool Paused,
    string Generator, int GenVersion, string GenOptionsJson, ImmutableArray<HostGenerator> Generators,
    ImmutableArray<HostActor> Actors, ImmutableArray<HostMaterial> Materials, ImmutableArray<HostObjectKind> ObjectKinds,
    ImmutableArray<HostChunk> Chunks, long MovesApplied);
