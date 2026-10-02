using System.Collections.Immutable;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Host;

public abstract record HostResponse(int RequestId);

public sealed record HostActionResult(int RequestId, bool Accepted, string ActorId, GameAction Action,
    double X, double Y, int Z, int H, string? Reason, string? Text, HostActivity? Activity,
    ImmutableArray<HostCarriedObject> Carried, double LoadKg, ImmutableArray<HostTrajectoryPoint> Trajectory)
{
    internal static HostActionResult Copy(int requestId, ActionResult result) => new(
        requestId,
        result.Accepted,
        result.ActorId,
        result.Action,
        result.X,
        result.Y,
        result.Z,
        result.H,
        result.Reason,
        result.Text,
        result.Activity is { } activity ? new HostActivity(activity.Op, activity.StartedMinute, activity.EndsMinute) : null,
        ImmutableArray.CreateRange(result.Carried.Select(item => new HostCarriedObject(item.Id, item.Kind, item.Name, item.Quantity, item.Slot))),
        result.LoadKg,
        ImmutableArray.CreateRange(result.Trajectory.Select(point => new HostTrajectoryPoint(
            point.Kind,
            point.Kind == "actor" ? point.IdValue.ToString() : null,
            point.Kind == "object" ? int.Parse(point.IdValue.ToString(), System.Globalization.CultureInfo.InvariantCulture) : null,
            point.X,
            point.Y,
            point.H))));
}

public sealed record HostTrajectoryPoint(string Kind, string? ActorId, int? ObjectId, double X, double Y, int H);

public sealed record HostActionResponse(int RequestId, HostActionResult Result,
    ImmutableArray<HostSimulationEvent> Events) : HostResponse(RequestId);

public sealed record HostMenuEntry(string Op, string Label, ImmutableArray<string> Tags, bool Available, string? Reason,
    string? Subject, GameAction Action, int TileDx, int TileDy);

public sealed record HostMenuPlace(int Dx, int Dy, int H, string Label);

public sealed record HostMenuPayload(double X, double Y, int Z, string Target,
    ImmutableArray<HostMenuEntry> Entries, ImmutableArray<HostMenuPlace> Places)
{
    internal static HostMenuPayload Copy(MenuPayload payload) => new(
        payload.X,
        payload.Y,
        payload.Z,
        payload.Target,
        ImmutableArray.CreateRange(payload.Entries.Select(entry => new HostMenuEntry(entry.Op, entry.Label,
            ImmutableArray.CreateRange(entry.Tags), entry.Available, entry.Reason, entry.Subject, entry.Action, entry.TileDx, entry.TileDy))),
        ImmutableArray.CreateRange(payload.Places.Select(place => new HostMenuPlace(place.Dx, place.Dy, place.H, place.Label))));
}

public sealed record HostPick(Target Target, int TileX, int TileY, int TileH, double HitX, double HitY, double HitH);

public sealed record HostMenuResponse(int RequestId, HostMenuPayload? Menu, HostPick? Hit) : HostResponse(RequestId);

public sealed record HostPickResponse(int RequestId, HostPick? Hit) : HostResponse(RequestId);

public sealed record HostNewGameResponse(int RequestId, long Seed, string Generator,
    ImmutableArray<HostSimulationEvent> Events) : HostResponse(RequestId);

public sealed record HostSimulationEvent(int Sequence, int GameMinute, string Type, string ActorId, string DataJson);

public sealed record HostEventsResponse(int RequestId, ImmutableArray<HostSimulationEvent> Events) : HostResponse(RequestId);

public sealed record HostErrorResponse(int RequestId, string Message) : HostResponse(RequestId);

public sealed record HostNarrationDelta(int RequestId, string Text) : HostResponse(RequestId);

/// <summary>The narrator is retrying after a failed check: whatever the feed showed so far is void.</summary>
public sealed record HostNarrationRestart(int RequestId) : HostResponse(RequestId);

/// <summary>
/// A narration ended. <c>Ok</c> carries the final text; every other status leaves the engine's own line standing.
/// <see cref="HostResponse.RequestId"/> is the narration's id, 0 for a host-level notice (the spend cap).
/// </summary>
public sealed record HostNarrationDone(int RequestId, Llm.Narration.NarrationStatus Status, string? Text, string? Detail) : HostResponse(RequestId);

public sealed record HostNarrationLine(int Sequence, int GameMinute, string Text);

public sealed record HostNarrationHistory(int RequestId, ImmutableArray<HostNarrationLine> Lines) : HostResponse(RequestId);

/// <summary>
/// What became of a free-text line (Dev-007). <c>Accept</c> is followed by the ordinary
/// <see cref="HostActionResponse"/> of the submitted action under the same request id; <c>Confirm</c>
/// carries the action the client submits only if the player says yes; <c>Reject</c> and
/// <c>Unavailable</c> carry a <see cref="Message"/> and change nothing.
/// </summary>
public sealed record HostInterpretResponse(int RequestId, Llm.InterpretKind Kind, string Label, string Message,
    GameAction? Action) : HostResponse(RequestId);
