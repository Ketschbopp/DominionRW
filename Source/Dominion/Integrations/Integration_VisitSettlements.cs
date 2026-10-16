using System;
using System.Collections;
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
    /// Visit Settlements (Nina.Goblin's build of alt4s' mod): caravans enter friendly settlements and walk around them.
    /// Visits are read from its own registry of visited maps, so maps opened by an attack or an operation do not count.
    /// <list type="bullet">
    /// <item>Each day of a visit: intel, favour and cultural influence with the host.</item>
    /// <item>Its theft, vandalism, encroaching and prying penalties become Dominion incidents: grievance and news.</item>
    /// <item>Covert operations can be run by colonists already on a visited map (Intelligence tab).</item>
    /// <item>A summit at their capital opens when your head of state is visiting it.</item>
    /// </list>
    /// </summary>
    public partial class Integration_VisitSettlements : DominionIntegration
    {
        public override string PackageId => "NinaGoblin.VisitSettlements";
        public override string[] AlternatePackageIds => new[] { "alt4s.VisitSettlements" };
        public override string Label => "Visit Settlements";

        /// <summary>History events Visit Settlements records with its goodwill penalties, and what Dominion calls them.</summary>
        private static readonly Dictionary<string, string> PenaltyEvents = new Dictionary<string, string>
        {
            ["ItemStolenFromSettlement"] = "Dominion_VisitIncident_Theft",
            ["VandalizedInSettlement"] = "Dominion_VisitIncident_Vandalism",
            ["EncroachedInSettlement"] = "Dominion_VisitIncident_Encroaching",
            ["ContainerOpenedInSettlement"] = "Dominion_VisitIncident_Prying",
        };

        private Type dataType;
        private FieldInfo settlementMaps;

        // One incident report per faction per day; the grievance still adds up.
        private readonly Dictionary<int, int> lastIncidentDay = new Dictionary<int, int>();

        protected internal override void OnWorldLoaded() => lastIncidentDay.Clear();

        protected internal override void Bind(Binder b)
        {
            dataType = b.Type("VisitSettlements.WorldComponent_SettlementData");
            settlementMaps = b.Field(dataType, "settlementMaps");
            b.Field(dataType, "settlementMapParents");
            if (settlementMaps != null && !typeof(IDictionary).IsAssignableFrom(settlementMaps.FieldType))
                b.Fail("settlementMaps is not a dictionary");
            // Recorded so the self-test shows whether the penalty hooks can be recognised.
            b.Type("VS_PenalizeForStealing");
            b.Type("VS_SettlementLordJob_DefendBase_Patch");
        }

        private IDictionary Maps
        {
            get
            {
                WorldComponent c = Find.World?.components?.Find(x => dataType.IsInstanceOfType(x));
                return c != null ? settlementMaps.GetValue(c) as IDictionary : null;
            }
        }

        /// <summary>Whether the player is visiting this map through Visit Settlements.</summary>
        public bool IsVisit(Map map)
        {
            if (map == null || !IsActive) return false;
            IDictionary maps = Maps;
            return maps != null && maps.Contains(map.Tile.tileId) && maps[map.Tile.tileId] == map;
        }

        /// <summary>The map of a settlement the player is visiting, or null.</summary>
        public Map VisitMapOf(Settlement s)
        {
            if (s == null || !IsActive) return null;
            IDictionary maps = Maps;
            if (maps == null || !maps.Contains(s.Tile.tileId)) return null;
            Map m = maps[s.Tile.tileId] as Map;
            return m != null && m.Parent == s ? m : null;
        }

        /// <summary>Maps being visited right now with colonists on them, and the settlement each belongs to.</summary>
        public IEnumerable<(Settlement settlement, Map map)> ActiveVisits()
        {
            IDictionary maps = IsActive ? Maps : null;
            if (maps == null) yield break;
            foreach (object v in maps.Values)
            {
                if (!(v is Map map) || !(map.Parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer) continue;
                if (map.mapPawns.FreeColonistsSpawnedCount == 0) continue;
                yield return (s, map);
            }
        }

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach ((Settlement s, Map _) in ActiveVisits())
            {
                Faction f = s.Faction;
                if (f.HostileTo(Faction.OfPlayer)) continue;
                FactionState fs = comp.GetState(f);
                fs.playerIntel = Mathf.Min(100f, fs.playerIntel + 2f);
                comp.GetRelation(f, Faction.OfPlayer).favour += 0.5f;
                BroadcastSystem.AddCulture(f, 0.5f);
            }
        }

        /// <summary>Called for every goodwill change; picks out the ones Visit Settlements makes as penalties.</summary>
        internal void Notify_GoodwillPenalty(Faction f, int change, HistoryEventDef reason)
        {
            if (reason == null || change >= 0 || !PenaltyEvents.TryGetValue(reason.defName, out string key)) return;
            if (f == null || f.IsPlayer || !DominionScheduler.IsSimulated(f)) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState view = comp.GetRelation(f, Faction.OfPlayer);
            view.grievance = Mathf.Min(100f, view.grievance - change * 0.5f);
            view.favour += change * 0.3f;
            int day = GenDate.DaysPassed;
            if (lastIncidentDay.TryGetValue(f.loadID, out int last) && last == day) return;
            lastIncidentDay[f.loadID] = day;
            EventBus.Raise(DominionEventDefOf.Dominion_VisitIncident, f, Faction.OfPlayer, detail: key.Translate(), source: PackageId);
        }

        /// <summary>A summit waiting at their capital opens once your head of state is visiting that capital.</summary>
        public Summit SummitAtVisit(out Pawn headOfState)
        {
            headOfState = LeadershipUtility.HeadOfState();
            if (headOfState == null || !headOfState.Spawned || headOfState.Downed || headOfState.InMentalState) return null;
            if (!(headOfState.Map?.Parent is Settlement s) || !IsVisit(headOfState.Map)) return null;
            return SummitSystem.SummitAbroadAt(s);
        }
    }

    /// <summary>Visit Settlements penalises theft and vandalism through goodwill with its own history events; Dominion listens.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    public static class Patch_Faction_TryAffectGoodwillWith_VisitPenalty
    {
        public static void Postfix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, bool __result)
        {
            if (!__result || reason == null || goodwillChange >= 0 || other == null || !other.IsPlayer) return;
            if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
            Integration_VisitSettlements vs = IntegrationRegistry.Get<Integration_VisitSettlements>();
            if (vs == null || !vs.IsActive) return;
            try { vs.Notify_GoodwillPenalty(__instance, goodwillChange, reason); }
            catch (Exception e) { DLog.WarningOnce("Visit Settlements penalty hook failed: " + e.Message, 0x7155); }
        }
    }
}
