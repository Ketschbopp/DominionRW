using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
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
            // Quest factions come and go; keeping them in the log leaves dangling references in the save.
            if (__instance.temporary || other.temporary || __instance.Hidden || other.Hidden) return;

            FactionRelationKind now = __instance.RelationKindWith(other);
            if (now == previousKind) return;
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
            else if (previousKind == FactionRelationKind.Ally)
                def = DominionEventDefOf.Dominion_AllianceBroken;
            if (def == null) return;

            // When Dominion drives the change it names the actor (the aggressor, the one breaking the alliance...).
            Faction primary = __instance, secondary = other;
            if (DiplomacyUtility.Actor == other)
            {
                primary = other;
                secondary = __instance;
            }
            EventBus.Raise(def, primary, secondary);
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

    /// <summary>The player defeated a faction settlement on its map: remembered for the unification ending (conquest).</summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    public static class Patch_SettlementDefeated
    {
        // Postfix only: it runs even if another mod's prefix (FTV intercepts settlement defeat) skips the original.
        // A destroyed settlement keeps its faction.
        public static void Postfix(Settlement factionBase)
        {
            Faction f = factionBase?.Faction;
            if (f == null || f.IsPlayer || !factionBase.Destroyed) return;
            WorldComponent_Dominion.Instance?.Unification.beatenByPlayer.Add(f.loadID);
        }
    }
}
