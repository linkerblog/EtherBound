using EtherBound.Llm.Narration;

namespace EtherBound.Game.App;

/// <summary>
/// Which narration endings deserve a line in the feed (Dev-008). A model that fails is told once per kind of failure
/// and again only after it has worked, so a dead network does not print a warning on every action. A rejection after
/// two tries and a switched-off narrator say nothing: the engine's own line already stands, and the LLM tab explains.
/// </summary>
public sealed class NarrationNotices
{
    private NarrationStatus? _last;

    /// <summary>The feed text for this ending, or null when it should stay quiet.</summary>
    public string? Notice(NarrationStatus status, string? detail)
    {
        var repeated = _last == status;
        _last = status;
        return status switch
        {
            NarrationStatus.Ok => null,
            NarrationStatus.Capped => $"NARRATOR STOPPED · {Clip(detail ?? "SPEND CAP REACHED")}",
            NarrationStatus.Error when !repeated => $"NARRATOR OFFLINE · {Clip(detail ?? "CALL FAILED")}",
            _ => null,
        };
    }

    private static string Clip(string text)
    {
        var one = text.Replace('\n', ' ').Trim().ToUpperInvariant();
        return one.Length <= 80 ? one : one[..80];
    }
}
