using EtherBound.Sim.Core;
using EtherBound.Sim.Engine.Ops;

namespace EtherBound.Sim.Engine;

internal static class ActivityWork
{
    public static void SaveOnInterrupt(Session session, ActorRow actor, ActivityState running, int gameMinute)
    {
        if (!OpCatalog.HandlerFor(running.Op).RetainsWorkProgress) return;
        var key = GameAction.Parse(running.Action).ToJson().ToJsonString();
        var total = Math.Max(0, running.EndsMinute - running.StartedMinute);
        var earned = Math.Clamp(gameMinute - running.StartedMinute, 0, total);
        if (earned > 0)
            session.SetActivityWorkMinutes(actor.Id, key, Math.Max(session.ActivityWorkMinutes(actor.Id, key), earned));
    }
}
