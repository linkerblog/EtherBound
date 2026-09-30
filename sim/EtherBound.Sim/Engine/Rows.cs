using System.Text.Json.Nodes;
using EtherBound.Sim.Core;
using EtherBound.Sim.World;

namespace EtherBound.Sim.Engine;

/// <summary>The <c>world_meta</c> row: seed, clock and which generator made the save.</summary>
public sealed class WorldMetaRow
{
    public long Seed { get; set; }
    public int GameMinute { get; set; }
    public int Speed { get; set; } = 1;
    public bool Paused { get; set; }
    public int GenVersion { get; set; }
    public string Generator { get; set; } = "test";
    public JsonObject GenOptions { get; set; } = new();

    public WorldMetaRow Clone() => new()
    {
        Seed = Seed, GameMinute = GameMinute, Speed = Speed, Paused = Paused, GenVersion = GenVersion,
        Generator = Generator, GenOptions = (JsonObject)GenOptions.DeepClone(),
    };

    public bool SameAs(WorldMetaRow o) => Seed == o.Seed && GameMinute == o.GameMinute && Speed == o.Speed &&
        Paused == o.Paused && GenVersion == o.GenVersion && Generator == o.Generator && Json.Same(GenOptions, o.GenOptions);
}

/// <summary>An <c>actor</c> row. <c>Activity</c> and <c>Mind</c> are the stored JSON documents.</summary>
public sealed class ActorRow
{
    public required string Id { get; init; }
    public required string Kind { get; set; }
    public string? Name { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int Z { get; set; }
    public int H { get; set; }
    public double MassKg { get; set; } = 80.0;
    public JsonObject? Activity { get; set; }
    public JsonObject? Mind { get; set; }

    public ActorRow Clone() => new()
    {
        Id = Id, Kind = Kind, Name = Name, X = X, Y = Y, Z = Z, H = H, MassKg = MassKg,
        Activity = (JsonObject?)Activity?.DeepClone(), Mind = (JsonObject?)Mind?.DeepClone(),
    };

    public bool SameAs(ActorRow o) => Id == o.Id && Kind == o.Kind && Name == o.Name && X.Equals(o.X) && Y.Equals(o.Y) &&
        Z == o.Z && H == o.H && MassKg.Equals(o.MassKg) && Json.Same(Activity, o.Activity) && Json.Same(Mind, o.Mind);

    public int TileX => PyMath.Floor(X);
    public int TileY => PyMath.Floor(Y);
}

/// <summary>An <c>object</c> row; <c>Loc</c> pins which location columns are set.</summary>
public sealed class ObjectRow
{
    public int Id { get; set; }
    public required string Kind { get; set; }
    public required string Loc { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? H { get; set; }
    public int? Cx { get; set; }
    public int? Cy { get; set; }
    public int? ContainerId { get; set; }
    public string? ActorId { get; set; }
    public string? Slot { get; set; }
    public int Quantity { get; set; } = 1;
    public JsonObject State { get; set; } = new();
    public double? Integrity { get; set; }
    public string? Owner { get; set; }

    public ObjectRow Clone() => new()
    {
        Id = Id, Kind = Kind, Loc = Loc, X = X, Y = Y, H = H, Cx = Cx, Cy = Cy, ContainerId = ContainerId,
        ActorId = ActorId, Slot = Slot, Quantity = Quantity, State = (JsonObject)State.DeepClone(), Integrity = Integrity,
        Owner = Owner,
    };

    public bool SameAs(ObjectRow o) => Id == o.Id && Kind == o.Kind && Loc == o.Loc && X == o.X && Y == o.Y && H == o.H &&
        Cx == o.Cx && Cy == o.Cy && ContainerId == o.ContainerId && ActorId == o.ActorId && Slot == o.Slot &&
        Quantity == o.Quantity && Json.Same(State, o.State) && Integrity.Equals(o.Integrity) && Owner == o.Owner;

    public bool IsOpen => State["open"] is { } open && open.GetValue<bool>();
}

/// <summary>Remaining joules of a partly damaged wall, keyed by the slot that holds it.</summary>
public readonly record struct WallKey(int Cx, int Cy, int Z, int CellIndex, string Slot);

/// <summary>One stored event log row.</summary>
public sealed record EventRow(int Seq, int GameMinute, string Type, string? ActorId, JsonObject Data);

/// <summary>One committed record of work saved across an interrupted activity.</summary>
public sealed record ActivityWorkRow(string ActorId, string ActionKey, int ProgressMinutes);

/// <summary>An input row read from the ordered replay journal.</summary>
public sealed record InputJournalEntry(long Sequence, string Kind, JsonObject Payload, int RepeatCount = 1);

/// <summary>An input staged by the engine for the same transaction as its world changes.</summary>
public sealed record InputToRecord(string Kind, JsonObject Payload);

/// <summary>Immutable engine catalogs loaded before a world session starts.</summary>
public sealed record WorldEngineCatalogs(string MaterialsToml, MaterialRegistry Registry, ObjectCatalog Catalog);
