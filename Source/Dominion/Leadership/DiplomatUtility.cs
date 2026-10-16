using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Who may act as an envoy. The head of state always can. Diplomats come from the setting: either colonists
    /// holding an ideology role (any role other than leader), or colonists picked at the Leadership Desk.
    /// </summary>
    public static class DiplomatUtility
    {
        public static bool UsesIdeoRoles =>
            DominionMod.Settings.diplomatMode == DiplomatMode.IdeoRole && ModsConfig.IdeologyActive;

        public static bool IsDiplomat(Pawn p)
        {
            if (!LeadershipUtility.IsValid(p)) return false;
            if (UsesIdeoRoles)
            {
                Precept_Role role = p.Ideo?.GetRole(p);
                return role != null && role.def != PreceptDefOf.IdeoRole_Leader;
            }
            return WorldComponent_Dominion.Instance?.Player.chosenDiplomats.Contains(p) ?? false;
        }

        /// <summary>The head of state or a diplomat: someone who can speak for the colony abroad.</summary>
        public static bool IsEnvoy(Pawn p) => p != null && (p == LeadershipUtility.HeadOfState() || IsDiplomat(p));

        /// <summary>The best envoy in a caravan (head of state first, then highest Social), or null.</summary>
        public static Pawn BestEnvoy(Caravan caravan)
        {
            Pawn hos = LeadershipUtility.HeadOfState();
            Pawn best = null;
            foreach (Pawn p in caravan.PawnsListForReading)
            {
                if (p == hos && !p.Downed) return p;
                if (IsDiplomat(p) && !p.Downed && (best == null || Social(p) > Social(best))) best = p;
            }
            return best;
        }

        public static int Social(Pawn p) => p?.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;

        /// <summary>In-person bonus to acceptance: being there, plus the envoy's Social skill.</summary>
        public static float InPersonBonus(Pawn envoy) => 0.15f + (Social(envoy) - 8) * 0.015f;

        public static IEnumerable<Pawn> AllDiplomats()
        {
            foreach (Map map in Find.Maps)
                foreach (Pawn p in map.mapPawns.FreeColonists)
                    if (IsDiplomat(p)) yield return p;
        }
    }
}
