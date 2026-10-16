using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Visit Settlements adds an "enter settlement" command to caravans at a friendly town (a postfix on
    /// Settlement.GetCaravanGizmos). Dominion disables it while the town is besieged, blockaded or held by an occupier:
    /// nobody gives tours of a town under attack.
    /// </summary>
    public partial class Integration_VisitSettlements
    {
        private const string VisitSettlementsHarmonyId = "alt4s.visitsettlements";

        private bool gizmoPatched;

        // Gizmos are rebuilt every frame while a caravan is selected; the answer is cached per settlement and tick.
        private static int cachedSettlementId = -1;
        private static int cachedTick = -1;
        private static string cachedReason;

        protected internal override void OnActivated()
        {
            if (gizmoPatched) return;
            try
            {
                var target = AccessTools.Method(typeof(Settlement), "GetCaravanGizmos", new[] { typeof(Caravan) });
                if (target == null)
                {
                    DLog.Warning("Visit Settlements: Settlement.GetCaravanGizmos not found, visits are not blocked during sieges.");
                    return;
                }
                // Runs after Visit Settlements' own postfix, so its command is already in the list.
                var postfix = new HarmonyMethod(typeof(Integration_VisitSettlements), nameof(CaravanGizmosPostfix))
                {
                    priority = Priority.Last,
                    after = new[] { VisitSettlementsHarmonyId },
                };
                DominionMod.Harmony.Patch(target, postfix: postfix);
                gizmoPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook the Visit Settlements command: " + e.Message);
            }
        }

        /// <summary>Harmony postfix on Settlement.GetCaravanGizmos: disables Visit Settlements' enter command when visiting is unsafe.</summary>
        public static void CaravanGizmosPostfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            try
            {
                if (__result == null || !(__instance is Settlement s) || WorldComponent_Dominion.Instance == null) return;
                Integration_VisitSettlements vs = IntegrationRegistry.Get<Integration_VisitSettlements>();
                if (vs == null || !vs.IsActive) return;
                string reason = VisitBlockedReason(s);
                if (reason == null) return;
                string label = "VS_EnterSettlementLabel".Translate();
                List<Gizmo> list = __result as List<Gizmo> ?? __result.ToList();
                foreach (Gizmo g in list)
                    if (g is Command c && c.defaultLabel == label) c.Disable(reason);
                __result = list;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Visit Settlements command hook failed: " + e.Message, 0x7156);
            }
        }

        /// <summary>Why a caravan cannot visit this settlement right now, or null if it can.</summary>
        public static string VisitBlockedReason(Settlement s)
        {
            if (s?.Faction == null || s.Faction.IsPlayer) return null;
            int tick = Find.TickManager.TicksGame;
            if (s.ID == cachedSettlementId && tick == cachedTick) return cachedReason;
            cachedSettlementId = s.ID;
            cachedTick = tick;
            cachedReason = ComputeBlockedReason(s);
            return cachedReason;
        }

        private static string ComputeBlockedReason(Settlement s)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            WarRegistry wars = comp.Wars;
            int tile = s.Tile.tileId;

            // Held by an occupier: the original owner is someone else.
            if (wars.originalOwner.TryGetValue(tile, out int original) && original != s.Faction.loadID)
                return "Dominion_Visit_BlockedOccupied".Translate(s.Label, s.Faction.Name);

            // A Dominion warband's siege, or any FTV invasion at the town.
            if (wars.siegePoints.ContainsKey(tile)) return "Dominion_Visit_BlockedSiege".Translate(s.Label);
            Integration_FactionTerritories ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            if (ftv != null && ftv.CanInvade)
                foreach ((int t, int _, int _, int _) in ftv.Invasions())
                    if (t == tile) return "Dominion_Visit_BlockedSiege".Translate(s.Label);

            // Blockaded by a warband or a caravan.
            foreach (Warband w in WarbandUtility.All())
                if (w.Blockading && w.Tile == s.Tile) return "Dominion_Visit_BlockedBlockade".Translate(s.Label);
            foreach (Blockade b in wars.blockades)
                if (b.settlement == s && b.StillValid) return "Dominion_Visit_BlockedBlockade".Translate(s.Label);
            return null;
        }
    }
}
