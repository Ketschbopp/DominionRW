using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    public enum DeskTier { Wood, Steel, Gold }

    /// <summary>Who leads the player's colony, and what the Leadership Desk and comms allow.</summary>
    public static class LeadershipUtility
    {
        /// <summary>True when the head of state is the ideology leader (setting, and Ideology active).</summary>
        public static bool UsesIdeoLeader =>
            DominionMod.Settings.headOfStateMode == HeadOfStateMode.IdeoLeader && ModsConfig.IdeologyActive;

        /// <summary>The player's head of state, or null if there is none or they cannot act.</summary>
        public static Pawn HeadOfState()
        {
            Pawn p = UsesIdeoLeader ? IdeoLeader() : WorldComponent_Dominion.Instance?.Player.chosenHeadOfState;
            return IsValid(p) ? p : null;
        }

        public static bool IsValid(Pawn p) =>
            p != null && !p.Dead && !p.Destroyed && p.Faction == Faction.OfPlayerSilentFail && !p.IsPrisoner && !p.IsSlave;

        private static Pawn IdeoLeader()
        {
            Ideo ideo = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
            if (ideo == null) return null;
            foreach (Precept_Role role in ideo.RolesListForReading)
                if (role.def == PreceptDefOf.IdeoRole_Leader)
                    return role.ChosenPawnSingle();
            return null;
        }

        /// <summary>Why there is no head of state, for tooltips.</summary>
        public static string NoHeadOfStateReason() => UsesIdeoLeader
            ? "Dominion_HeadOfState_NoIdeoLeader".Translate()
            : "Dominion_HeadOfState_NoneChosen".Translate();

        // Both scans walk every colonist building, and the UI asks every frame, so results are kept for a tick.
        private static int deskTick = -1, commsTick = -1;
        private static Building_LeadershipDesk cachedDesk;
        private static bool cachedComms;

        /// <summary>The best Leadership Desk on any home map, or null.</summary>
        public static Building_LeadershipDesk BestDesk()
        {
            int now = Find.TickManager.TicksGame;
            if (deskTick == now && (cachedDesk == null || cachedDesk.Spawned)) return cachedDesk;
            deskTick = now;
            cachedDesk = FindBestDesk();
            return cachedDesk;
        }

        private static Building_LeadershipDesk FindBestDesk()
        {
            Building_LeadershipDesk best = null;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].IsPlayerHome) continue;
                foreach (Building_LeadershipDesk d in maps[i].listerBuildings.AllBuildingsColonistOfClass<Building_LeadershipDesk>())
                    if (best == null || d.Tier > best.Tier) best = d;
            }
            return best;
        }

        /// <summary>True if a powered comms console stands on any home map.</summary>
        public static bool HasComms()
        {
            int now = Find.TickManager.TicksGame;
            if (commsTick == now) return cachedComms;
            commsTick = now;
            cachedComms = FindComms();
            return cachedComms;
        }

        private static bool FindComms()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (!maps[i].IsPlayerHome) continue;
                foreach (Building b in maps[i].listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.CommsConsole))
                {
                    CompPowerTrader power = b.TryGetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn) return true;
                }
            }
            return false;
        }

        /// <summary>Added to every AI acceptance chance: the head of state's Social skill and a gold desk's prestige.</summary>
        public static float AcceptanceBonus()
        {
            float bonus = 0f;
            Pawn hos = HeadOfState();
            if (hos?.skills != null)
                bonus += (hos.skills.GetSkill(SkillDefOf.Social).Level - 8) * 0.015f;
            if (BestDesk()?.Tier == DeskTier.Gold)
                bonus += 0.08f;
            return bonus;
        }

        public static float InfluenceRateMultiplier(DeskTier tier) => tier == DeskTier.Wood ? 1f : 1.25f;
    }
}
