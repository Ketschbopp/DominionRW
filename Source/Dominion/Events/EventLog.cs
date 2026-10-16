using System.Collections.Generic;
using Verse;

namespace Dominion
{
    /// <summary>Saved history of recent events, capped by the event log setting (oldest dropped first).</summary>
    public class EventLog : IExposable
    {
        private List<DominionEvent> events = new List<DominionEvent>();
        private int nextId;

        public IReadOnlyList<DominionEvent> Events => events;

        public int NextId() => nextId++;

        public void Add(DominionEvent ev)
        {
            events.Add(ev);
            int cap = DominionMod.Settings?.eventLogCap ?? 500;
            int overflow = events.Count - cap;
            if (overflow > 0)
                events.RemoveRange(0, overflow);
        }

        public void Clear() => events.Clear();

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextId, "nextId");
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (events == null) events = new List<DominionEvent>();
                // Drop events whose def no longer exists (mod removed or renamed).
                events.RemoveAll(e => e == null || e.def == null);
            }
        }
    }
}
