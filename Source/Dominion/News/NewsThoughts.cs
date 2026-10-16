using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>Colonists react to news that matters to them: wars and unrest nearby, peace, and allies under attack.</summary>
    public static class NewsThoughts
    {
        /// <summary>Factions with a settlement closer than this many tiles to the colony count as nearby.</summary>
        public const float NearbyDistance = 30f;

        public static void Apply(NewsItem n)
        {
            if (!DominionMod.Settings.colonistNewsThoughts) return;
            ThoughtDef thought = ThoughtFor(n);
            if (thought == null) return;

            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                if (!maps[m].IsPlayerHome) continue;
                foreach (Pawn p in maps[m].mapPawns.FreeColonistsSpawned)
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(thought, null, null);
            }
        }

        private static ThoughtDef ThoughtFor(NewsItem n)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || n.def == null) return null;
            if (n.primary != null && n.primary.IsPlayer || n.secondary != null && n.secondary.IsPlayer) return null;
            bool near = IsNear(n.primary) || IsNear(n.secondary);
            bool allyInvolved = IsAlly(n.primary, player) || IsAlly(n.secondary, player);

            if (n.def == DominionEventDefOf.Dominion_WarDeclared)
            {
                if (IsAlly(n.secondary, player)) return DominionThoughtDefOf.Dominion_News_AllyAttacked;
                return near ? DominionThoughtDefOf.Dominion_News_WarNearby : null;
            }
            if (n.def == DominionEventDefOf.Dominion_PeaceMade)
                return near || allyInvolved ? DominionThoughtDefOf.Dominion_News_PeaceNearby : null;
            if (n.def == DominionEventDefOf.Dominion_Coup || n.def == DominionEventDefOf.Dominion_Revolution || n.def == DominionEventDefOf.Dominion_Riots)
                return near ? DominionThoughtDefOf.Dominion_News_UnrestNearby : null;
            return null;
        }

        private static bool IsAlly(Faction f, Faction player) => f != null && !f.IsPlayer && f.RelationKindWith(player) == FactionRelationKind.Ally;

        private static bool IsNear(Faction f)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (f == null || player == null || f.IsPlayer) return false;
            RelationState r = WorldComponent_Dominion.Instance?.TryGetRelation(f, player);
            return r != null && r.distance >= 0f && r.distance < NearbyDistance;
        }
    }

    [DefOf]
    public static class DominionThoughtDefOf
    {
        public static ThoughtDef Dominion_News_WarNearby;
        public static ThoughtDef Dominion_News_AllyAttacked;
        public static ThoughtDef Dominion_News_PeaceNearby;
        public static ThoughtDef Dominion_News_UnrestNearby;
        public static ThoughtDef Dominion_ForeignPropaganda;
        public static ThoughtDef Dominion_Broadcast_Informed;

        static DominionThoughtDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionThoughtDefOf));
        }
    }
}
