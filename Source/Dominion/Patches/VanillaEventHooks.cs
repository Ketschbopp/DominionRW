using HarmonyLib;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Feeds vanilla diplomacy changes into the event bus. These methods run only when something
    /// actually changes, never per tick.
    /// </summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
    public static class Patch_Faction_Notify_RelationKindChanged
    {
        // Vanilla may notify both sides of the same change; remember the last one to raise it once.
        private static int lastLow = -1, lastHigh = -1, lastTick = -1;
        private static FactionRelationKind lastKind;

        public static void Postfix(Faction __instance, Faction other, FactionRelationKind previousKind)
        {
            if (Current.ProgramState != ProgramState.Playing || other == null) return;

            FactionRelationKind now = __instance.RelationKindWith(other);
            int low = System.Math.Min(__instance.loadID, other.loadID);
            int high = System.Math.Max(__instance.loadID, other.loadID);
            int tick = Find.TickManager.TicksGame;
            if (low == lastLow && high == lastHigh && tick == lastTick && now == lastKind) return;
            lastLow = low;
            lastHigh = high;
            lastTick = tick;
            lastKind = now;

            DominionEventDef def = null;
            if (now == FactionRelationKind.Hostile)
                def = DominionEventDefOf.Dominion_WarDeclared;
            else if (previousKind == FactionRelationKind.Hostile)
                def = DominionEventDefOf.Dominion_PeaceMade;
            else if (now == FactionRelationKind.Ally)
                def = DominionEventDefOf.Dominion_RelationsWarmed;

            if (def != null)
                EventBus.Raise(def, __instance, other);
        }
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_LeaderDied))]
    public static class Patch_Faction_Notify_LeaderDied
    {
        public static void Prefix(Faction __instance)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            EventBus.Raise(DominionEventDefOf.Dominion_LeaderDied, __instance);
        }
    }
}
