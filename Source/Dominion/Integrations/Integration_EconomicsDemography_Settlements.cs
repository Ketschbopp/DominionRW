using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// E&amp;D grows and shrinks factions on the world map during its daily growth step: a rich, crowded faction founds a
    /// new settlement, a faction with no towns left settles again, and a shrinking one abandons a town to ruins. Dominion
    /// compares the settlements before and after that step and reports each change as news.
    /// </summary>
    public partial class Integration_EconomicsDemography
    {
        private MethodInfo processDailyGrowth;
        private bool settlementsPatched;

        private void BindSettlements(Type manager, Binder b)
        {
            processDailyGrowth = manager?.GetMethod("ProcessDailyGrowth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            b.Note(processDailyGrowth != null ? "new and abandoned settlements make the news" : "growth step not found: E&D expansion stays silent");
        }

        private void PatchSettlements()
        {
            if (settlementsPatched || processDailyGrowth == null) return;
            try
            {
                DominionMod.Harmony.Patch(processDailyGrowth,
                    prefix: new HarmonyMethod(typeof(Integration_EconomicsDemography), nameof(GrowthPrefix)),
                    postfix: new HarmonyMethod(typeof(Integration_EconomicsDemography), nameof(GrowthPostfix)));
                settlementsPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook Economics & Demography expansion: " + e.Message);
            }
        }

        public struct TownRecord
        {
            public int owner;
            public int tile;
            public string label;
        }

        /// <summary>Harmony prefix on WorldPopulationManager.ProcessDailyGrowth: notes every AI settlement.</summary>
        public static void GrowthPrefix(out Dictionary<int, TownRecord> __state)
        {
            __state = null;
            try
            {
                if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
                __state = new Dictionary<int, TownRecord>();
                List<Settlement> all = Find.WorldObjects.Settlements;
                for (int i = 0; i < all.Count; i++)
                {
                    Settlement s = all[i];
                    if (s.Faction == null || s.Faction.IsPlayer) continue;
                    __state[s.ID] = new TownRecord { owner = s.Faction.loadID, tile = s.Tile.tileId, label = s.Label };
                }
            }
            catch (Exception e)
            {
                __state = null;
                DLog.WarningOnce("E&D expansion hook failed: " + e.Message, 0x0ED5);
            }
        }

        /// <summary>Harmony postfix on WorldPopulationManager.ProcessDailyGrowth: reports settlements founded and abandoned.</summary>
        public static void GrowthPostfix(Dictionary<int, TownRecord> __state)
        {
            if (__state == null) return;
            try
            {
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                if (comp == null) return;
                var before = new HashSet<int>();
                foreach (TownRecord r in __state.Values) before.Add(r.owner);

                var now = new HashSet<int>();
                bool changed = false;
                List<Settlement> all = Find.WorldObjects.Settlements;
                for (int i = 0; i < all.Count; i++)
                {
                    Settlement s = all[i];
                    if (s.Faction == null || s.Faction.IsPlayer) continue;
                    now.Add(s.ID);
                    if (__state.ContainsKey(s.ID)) continue;
                    Faction f = s.Faction;
                    if (!DominionScheduler.IsSimulated(f)) continue;
                    changed = true;
                    FactionState fs = comp.GetState(f);
                    bool resettled = !before.Contains(f.loadID);
                    fs.stability = Mathf.Min(100f, fs.stability + (resettled ? 5f : 2f));
                    fs.legitimacy = Mathf.Min(100f, fs.legitimacy + (resettled ? 3f : 1f));
                    EventBus.Raise(resettled ? IntegrationEventDefOf.Dominion_FactionResettled : IntegrationEventDefOf.Dominion_SettlementExpanded,
                        f, null, s.Tile.tileId, detail: s.Label, source: "helldan.economicsdemography");
                }

                foreach (KeyValuePair<int, TownRecord> kv in __state)
                {
                    if (now.Contains(kv.Key)) continue;
                    // E&D destroys the town and leaves ruins on its tile; a town that changed hands keeps its id.
                    Faction f = comp.Cache.FactionById(kv.Value.owner);
                    if (f == null || !DominionScheduler.IsSimulated(f)) continue;
                    changed = true;
                    FactionState fs = comp.GetState(f);
                    fs.stability = Mathf.Max(0f, fs.stability - 3f);
                    EventBus.Raise(IntegrationEventDefOf.Dominion_SettlementAbandoned, f, null, kv.Value.tile,
                        detail: kv.Value.label, source: "helldan.economicsdemography");
                }
                if (changed)
                {
                    comp.Cache.Rebuild();
                    IntegrationRegistry.Get<Integration_FactionTerritories>()?.RefreshTerritory();
                }
            }
            catch (Exception e)
            {
                DLog.WarningOnce("E&D expansion hook failed: " + e.Message, 0x0ED5);
            }
        }
    }
}
