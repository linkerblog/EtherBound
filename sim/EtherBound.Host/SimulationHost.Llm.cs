using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using EtherBound.Llm;
using EtherBound.Llm.Narration;
using EtherBound.Sim.Core;
using EtherBound.Sim.Engine;

namespace EtherBound.Host;

/// <summary>
/// Everything model-shaped the host does (Dev-007 and Dev-008). The sim thread builds immutable inputs, a model call
/// runs off it, and the answer comes back as a command, so the sim thread stays the only mutator and nothing waits
/// on a model. A model proposes; the engine validates and commits.
/// </summary>
public sealed partial class SimulationHost
{
    /// <summary>Facts older than this many are dropped: a long silence of the narrator must not become a long recital.</summary>
    private const int MaxFactBacklog = 12;

    private sealed record InterpretText(int Request, string Text) : Command(Request);
    private sealed record InterpretDone(int Request, string Text, Interpretation Interpretation) : Command(Request);
    private sealed record InterpretFailed(int Request, string Text, string Message) : Command(Request);
    private sealed record NarrationDelta(int Request, string Text) : Command(Request);
    private sealed record NarrationRestart(int Request) : Command(Request);
    private sealed record NarrationDone(int Request, int Epoch, NarrationOutcome Outcome, int FromSeq, int ToSeq) : Command(Request);
    private sealed record NarrationHistoryQuery(int Request, int Limit) : Command(Request);

    private readonly List<HostSimulationEvent> _pendingEvents = new();
    private CancellationTokenSource? _interpretCts;
    private int _latestInterpret;

    // Narration state belongs to the sim thread: only the loop and the handlers below touch it.
    private readonly List<string> _facts = new();
    private readonly List<string> _recentNarrations = new();
    private int _factsFrom, _factsTo, _lastSeq, _narrationId, _epoch;
    private bool _narrating;
    private CancellationTokenSource? _narrationCts;

    /// <summary>The LLM layer this host was given, for the LLM tab to read and edit; null when there is none.</summary>
    public LlmRuntime? Llm => _llm;

    /// <summary>True when free text can reach Jev; the HUD says so instead of calling when it cannot.</summary>
    public bool FreeTextAvailable => _llm?.Interpreter.Configured == true;

    /// <summary>
    /// Free text to one of Niko's own actions (Dev-007). The reply is a <see cref="HostInterpretResponse"/>;
    /// a newer call supersedes an older one that has not answered yet.
    /// </summary>
    public bool TryInterpretText(int requestId, string text) =>
        requestId > 0 && !string.IsNullOrWhiteSpace(text) && Enqueue(new InterpretText(requestId, text));

    /// <summary>The last narrations the log holds, oldest first, so the feed can show the story again after a restart.</summary>
    public bool TryRequestNarrationHistory(int requestId, int limit = 30) =>
        requestId > 0 && limit is > 0 and <= 200 && Enqueue(new NarrationHistoryQuery(requestId, limit));

    private ImmutableArray<HostSimulationEvent> TakeEvents()
    {
        var events = ImmutableArray.CreateRange(_pendingEvents);
        _pendingEvents.Clear();
        CollectFacts(events);
        return events;
    }

    private void QueueEvents()
    {
        var events = TakeEvents();
        if (!events.IsEmpty) _responses.Writer.TryWrite(new HostEventsResponse(0, events));
    }

    private void StartLlm(WorldEngine engine)
    {
        if (_llm is null) return;
        var s = _llm.Settings;
        engine.PruneLlmCalls(s.KeepFullCalls, s.KeepCallRows, s.CutTextChars);
        foreach (var row in engine.ReadLastEvents("llm.narrated", s.NarrationRecent))
            _recentNarrations.Add(row.Data["text"]!.GetValue<string>());
    }

    private void CancelLlmWork()
    {
        _interpretCts?.Cancel();
        _narrationCts?.Cancel();
    }

    /// <summary>A new world has a new story: what was pending or remembered belongs to the old one.</summary>
    private void ResetLlmForNewWorld()
    {
        _epoch++;
        _latestInterpret = 0;
        _interpretCts?.Cancel();
        _facts.Clear();
        _recentNarrations.Clear();
    }

    // --- Dev-007: free text -------------------------------------------------------------------------

    private void HandleInterpretText(WorldEngine engine, InterpretText ask)
    {
        if (_llm is null || !_llm.Interpreter.Configured)
        {
            _responses.Writer.TryWrite(new HostInterpretResponse(ask.Request, InterpretKind.Unavailable, "", "free text needs a Jev key", null));
            return;
        }
        var asker = engine.GetState().Actors.First(actor => actor.Id == Ids.Player);
        var candidates = CandidateBuilder.Build(engine.Menu(Ids.Player, asker.X, asker.Y, asker.Z, 1), _llm.Settings.MaxCandidates);
        _interpretCts?.Cancel();
        _interpretCts?.Dispose();
        _interpretCts = new CancellationTokenSource();
        _latestInterpret = ask.Request;
        StartInterpretation(ask.Request, ask.Text, candidates, _interpretCts.Token);
    }

    private void HandleInterpretFailed(WorldEngine engine, InterpretFailed failed)
    {
        if (failed.Request != _latestInterpret) return;
        LogJevError(engine, failed.Text, failed.Message);
        _responses.Writer.TryWrite(new HostInterpretResponse(failed.Request, InterpretKind.Unavailable, "", failed.Message, null));
    }

    /// <summary>Returns true when an action was submitted (the world changed).</summary>
    private bool HandleInterpretDone(WorldEngine engine, InterpretDone done)
    {
        // The world kept running while Jev thought; a superseded answer is dropped.
        if (done.Request != _latestInterpret) return false;
        var decision = done.Interpretation;
        if (decision.Kind == InterpretKind.Unavailable)
        {
            LogJevError(engine, done.Text, decision.Message);
            _responses.Writer.TryWrite(new HostInterpretResponse(done.Request, InterpretKind.Unavailable, "", decision.Message, null));
            return false;
        }
        engine.RecordDecision(Ids.Player, "llm.interpreted", decision.ToEventJson(done.Text, _llm!.Settings.LogTextChars));
        var call = new CallRecord("jev", decision.Model, decision.TokensIn, decision.TokensOut, decision.CostUsd, "ok", null,
            Truncate(done.Text, _llm.Settings.LogTextChars),
            $"{decision.Candidate?.Key ?? FreeTextInterpreter.None} {decision.Confidence:0.00} fits {decision.Fits:0.00} {decision.Kind}");
        LogCall(engine, call);
        _responses.Writer.TryWrite(new HostInterpretResponse(done.Request, decision.Kind,
            decision.Candidate?.Label ?? "", decision.Message, decision.Candidate?.Action));
        if (decision.Kind != InterpretKind.Accept) return false;
        // The ordinary door: the engine validates the action again against today's world.
        var submitted = engine.Submit(Ids.Player, decision.Candidate!.Action);
        NoteInspection(submitted);
        _responses.Writer.TryWrite(new HostActionResponse(done.Request, HostActionResult.Copy(done.Request, submitted), TakeEvents()));
        return true;
    }

    private void LogJevError(WorldEngine engine, string text, string message) =>
        LogCall(engine, new CallRecord("jev", _llm!.Jev.Model, 0, 0, null, "error", message, Truncate(text, _llm.Settings.LogTextChars), ""));

    private void LogCall(WorldEngine engine, CallRecord call)
    {
        engine.RecordLlmCall(call.Role, call.Model, call.TokensIn, call.TokensOut, call.CostUsd, call.Outcome, call.Error, call.Prompt, call.Response);
        _llm!.Record(call);
    }

    /// <summary>
    /// Jev runs off the sim thread; its answer comes back as a command so the sim thread stays the only
    /// mutator. A cancelled call (a newer Enter) says nothing.
    /// </summary>
    private void StartInterpretation(int request, string text, IReadOnlyList<Candidate> candidates, CancellationToken token)
    {
        var interpreter = _llm!.Interpreter;
        _ = Task.Run(async () =>
        {
            Command next;
            try
            {
                next = new InterpretDone(request, text, await interpreter.InterpretAsync(text, candidates, token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                next = new InterpretFailed(request, text, error.Message);
            }
            await DeliverAsync(next).ConfigureAwait(false);
        });
    }

    // --- Dev-008: the narrator ----------------------------------------------------------------------

    /// <summary>
    /// Turns the events a response is about to carry into facts for the next narration. Only the player's own
    /// committed events are here, the same filter the feed uses, so the narrator never hears of what Niko did not see.
    /// </summary>
    private void CollectFacts(ImmutableArray<HostSimulationEvent> events)
    {
        if (_llm?.Narrator is null) return;
        foreach (var e in events)
        {
            _lastSeq = Math.Max(_lastSeq, e.Sequence);
            if (NarrationFacts.Summarize(e.Type, JsonNode.Parse(e.DataJson)!.AsObject()) is { } fact) AddFact(fact, e.Sequence);
        }
    }

    /// <summary><c>inspect</c> emits no event, so what it said is a fact of its own.</summary>
    private void NoteInspection(ActionResult result)
    {
        if (_llm?.Narrator is null || !result.Accepted || result.Action.Op != "inspect") return;
        if (NarrationFacts.Inspection(result.Text) is { } fact) AddFact(fact, _lastSeq);
    }

    private void AddFact(string fact, int seq)
    {
        if (_facts.Count == 0) _factsFrom = seq;
        _factsTo = seq;
        _facts.Add(fact);
        while (_facts.Count > MaxFactBacklog) _facts.RemoveAt(0);
    }

    /// <summary>Called once per loop: starts a narration when there are facts, a model, room under the cap and none in flight.</summary>
    private void MaybeNarrate(WorldEngine engine)
    {
        if (_llm?.Narrator is not { } narrator || _facts.Count == 0 || _narrating) return;
        if (!narrator.Enabled)
        {
            _facts.Clear();
            return;
        }
        if (_llm.Spend.Capped)
        {
            _facts.Clear();
            if (_llm.Spend.AnnounceCapOnce())
                _responses.Writer.TryWrite(new HostNarrationDone(0, NarrationStatus.Capped, null,
                    $"the spend cap of {_llm.Spend.CapUsd:0.00} USD is reached"));
            return;
        }
        var state = engine.GetState();
        var player = state.Actors.First(actor => actor.Id == Ids.Player);
        var packet = NarrationPacket.Build(state, engine.Menu(Ids.Player, player.X, player.Y, player.Z, 1), _facts.ToArray(),
            _recentNarrations.ToArray(), _factsFrom, _factsTo, _llm.Settings.NarrationMaxNearby);
        _facts.Clear();
        _narrationCts?.Dispose();
        _narrationCts = new CancellationTokenSource();
        var token = _narrationCts.Token;
        var id = ++_narrationId;
        var epoch = _epoch;
        _narrating = true;
        _ = Task.Run(async () =>
        {
            NarrationOutcome outcome;
            try
            {
                outcome = await narrator.NarrateAsync(packet, text => Enqueue(new NarrationDelta(id, text)),
                    () => Enqueue(new NarrationRestart(id)), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                outcome = new NarrationOutcome(NarrationStatus.Error, null, Array.Empty<CallRecord>(), error.Message);
            }
            await DeliverAsync(new NarrationDone(id, epoch, outcome, packet.FromSeq, packet.ToSeq)).ConfigureAwait(false);
        });
    }

    private void HandleNarrationDone(WorldEngine engine, NarrationDone done)
    {
        _narrating = false;
        foreach (var call in done.Outcome.Calls) LogCall(engine, call);
        var status = done.Outcome.Status;
        string? text = null;
        var detail = done.Outcome.Detail;
        if (status == NarrationStatus.Ok && done.Epoch != _epoch)
        {
            status = NarrationStatus.Error;
            detail = "the world was replaced while the narrator wrote";
        }
        else if (status == NarrationStatus.Ok)
        {
            text = done.Outcome.Text!;
            engine.RecordDecision(Ids.Player, "llm.narrated", Json.Obj(("text", text), ("from_seq", done.FromSeq),
                ("to_seq", done.ToSeq), ("model", done.Outcome.Calls[^1].Model)));
            _recentNarrations.Add(text);
            while (_recentNarrations.Count > _llm!.Settings.NarrationRecent) _recentNarrations.RemoveAt(0);
        }
        _responses.Writer.TryWrite(new HostNarrationDone(done.Request, status, text, detail));
    }

    private void HandleNarrationHistory(WorldEngine engine, NarrationHistoryQuery query)
    {
        var lines = engine.ReadLastEvents("llm.narrated", query.Limit)
            .Select(row => new HostNarrationLine(row.Seq, row.GameMinute, row.Data["text"]!.GetValue<string>()));
        _responses.Writer.TryWrite(new HostNarrationHistory(query.Request, ImmutableArray.CreateRange(lines)));
    }

    // --- shared -------------------------------------------------------------------------------------

    /// <summary>Hands a background result to the sim thread; a host shutting down has nobody waiting for it.</summary>
    private async Task DeliverAsync(Command command)
    {
        try
        {
            await _commands.Writer.WriteAsync(command).ConfigureAwait(false);
            _wake.Set();
        }
        catch (Exception error) when (error is ChannelClosedException or ObjectDisposedException)
        {
            // Disposed: the answer has nowhere to go.
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
