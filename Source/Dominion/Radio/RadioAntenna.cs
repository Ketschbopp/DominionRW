using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A large radio antenna built right next to a radio receiver links to it and gives the colony global radio
    /// coverage: 100%, and it does not fade while both are powered. Dominion's own antenna counts, and so does any
    /// modded building whose defName or label calls it an antenna (at least 2x2, so roof aerials do not count).
    /// </summary>
    public static class RadioAntenna
    {
        private static int cachedTick = -1;
        private static bool cachedGlobal;

        public static bool IsAntenna(ThingDef def) =>
            def != null && def.building != null &&
            (def == DominionThingDefOf.Dominion_LargeRadioAntenna
             || (def.size.x * def.size.z >= 4 && (def.defName.IndexOf("antenna", System.StringComparison.OrdinalIgnoreCase) >= 0
                                                 || (def.label?.IndexOf("antenna", System.StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)));

        public static bool Working(Thing t)
        {
            if (t == null || !t.Spawned || t.IsBrokenDown()) return false;
            CompPowerTrader power = t.TryGetComp<CompPowerTrader>();
            return power == null || power.PowerOn;
        }

        /// <summary>A working antenna touching the receiver (sides or corners), or null.</summary>
        public static Thing LinkedAntenna(Thing receiver)
        {
            if (receiver?.Map == null) return null;
            foreach (IntVec3 c in receiver.OccupiedRect().ExpandedBy(1))
            {
                if (!c.InBounds(receiver.Map)) continue;
                List<Thing> things = c.GetThingList(receiver.Map);
                for (int i = 0; i < things.Count; i++)
                    if (things[i] != receiver && IsAntenna(things[i].def) && things[i].Faction == receiver.Faction && Working(things[i]))
                        return things[i];
            }
            return null;
        }

        /// <summary>True while any working player receiver is linked to a working antenna. Cached per tick.</summary>
        public static bool GlobalCoverage
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now == cachedTick) return cachedGlobal;
                cachedTick = now;
                cachedGlobal = false;
                if (!DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished) return false;
                foreach (Map map in Find.Maps)
                {
                    if (!map.IsPlayerHome) continue;
                    foreach (Building b in map.listerBuildings.AllBuildingsColonistOfDef(DominionThingDefOf.Dominion_RadioReceiver))
                    {
                        CompRadioReceiver r = b.TryGetComp<CompRadioReceiver>();
                        if (r != null && r.CanMonitorNow && LinkedAntenna(b) != null)
                            return cachedGlobal = true;
                    }
                }
                return false;
            }
        }
    }
}
