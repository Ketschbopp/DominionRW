using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Lookups rebuilt once a day (or when the faction list changes) so systems never scan the
    /// world object list themselves. Not saved.
    /// </summary>
    public class WorldCache
    {
        private readonly Dictionary<int, Faction> factionsById = new Dictionary<int, Faction>();
        private readonly Dictionary<int, List<Settlement>> settlementsByFaction = new Dictionary<int, List<Settlement>>();
        private static readonly List<Settlement> Empty = new List<Settlement>();

        public readonly List<Faction> Simulated = new List<Faction>();

        private int builtTick = -1;
        private int builtFactionCount = -1;
        private int builtSettlementCount = -1;

        public Faction FactionById(int loadId) => factionsById.TryGetValue(loadId, out Faction f) ? f : null;

        public List<Settlement> SettlementsOf(Faction f) =>
            f != null && settlementsByFaction.TryGetValue(f.loadID, out List<Settlement> list) ? list : Empty;

        public void EnsureFresh()
        {
            int ticks = Find.TickManager.TicksGame;
            if (builtTick >= 0 && ticks - builtTick < GenDate.TicksPerDay
                && Find.FactionManager.AllFactionsListForReading.Count == builtFactionCount
                && Find.WorldObjects.Settlements.Count == builtSettlementCount)
                return;
            Rebuild();
        }

        public void Rebuild()
        {
            factionsById.Clear();
            Simulated.Clear();
            foreach (List<Settlement> list in settlementsByFaction.Values)
                list.Clear();

            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                factionsById[f.loadID] = f;
                if (DominionScheduler.IsSimulated(f))
                    Simulated.Add(f);
            }

            List<Settlement> settlements = Find.WorldObjects.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s.Faction == null) continue;
                if (!settlementsByFaction.TryGetValue(s.Faction.loadID, out List<Settlement> list))
                {
                    list = new List<Settlement>();
                    settlementsByFaction[s.Faction.loadID] = list;
                }
                list.Add(s);
            }

            builtTick = Find.TickManager.TicksGame;
            builtFactionCount = all.Count;
            builtSettlementCount = settlements.Count;
        }
    }
}
