using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Hostages are real pawns: colonists a faction has kidnapped (vanilla's kidnapped list) and prisoners from that
    /// faction held in your colonies. An exchange frees both sides physically: prisoners walk out, captives walk in.
    /// </summary>
    public static class HostageUtility
    {
        public static List<Pawn> HeldByThem(Faction f)
        {
            var list = new List<Pawn>();
            if (f?.kidnapped == null) return list;
            foreach (Pawn p in f.kidnapped.KidnappedPawnsListForReading)
                if (p != null && !p.Dead && p.Faction == Faction.OfPlayer)
                    list.Add(p);
            return list;
        }

        public static List<Pawn> HeldByUs(Faction f)
        {
            var list = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn p in map.mapPawns.PrisonersOfColonySpawned)
                    if (p.Faction == f && !p.Dead)
                        list.Add(p);
            }
            return list;
        }

        // Asked by the Diplomacy tab for every faction, every frame.
        private static readonly UICache<Faction, bool> anyCache = new UICache<Faction, bool>(f => HeldByThem(f).Count > 0 || HeldByUs(f).Count > 0);
        private static readonly UICache<Faction, float> acceptCache = new UICache<Faction, float>(f => ComputeAcceptance(f, 0f));

        public static bool AnyHostages(Faction f) => f != null && anyCache.Get(f);

        /// <summary>Acceptance without a personal bonus, cached for UI.</summary>
        public static float CachedAcceptance(Faction f) => acceptCache.Get(f);

        /// <summary>
        /// Chance they agree to exchange everyone. Freeing their people without asking anything back is always accepted.
        /// Holding their leader is strong leverage.
        /// </summary>
        public static float ExchangeAcceptance(Faction f, float bonus = 0f) => ComputeAcceptance(f, bonus);

        private static float ComputeAcceptance(Faction f, float bonus)
        {
            List<Pawn> theirs = HeldByThem(f), ours = HeldByUs(f);
            if (theirs.Count == 0 && ours.Count == 0) return 0f;
            if (theirs.Count == 0) return 1f;
            float leverage = ours.Count + (ours.Contains(f.leader) ? 3f : 0f);
            float opinion = WorldComponent_Dominion.Instance.GetRelation(f, Faction.OfPlayer).opinion;
            return Mathf.Clamp(0.25f + (leverage - theirs.Count * 0.7f) * 0.2f + opinion / 300f + bonus, 0.05f, 0.95f);
        }

        /// <summary>Frees everyone on both sides. Returns (freed by them, freed by us).</summary>
        public static void Exchange(Faction f, out int returned, out int released, Caravan caravan = null)
        {
            anyCache.Invalidate();
            acceptCache.Invalidate();
            List<Pawn> theirs = HeldByThem(f), ours = HeldByUs(f);
            released = 0;
            foreach (Pawn p in ours)
            {
                GenGuest.PrisonerRelease(p);
                released++;
            }

            returned = 0;
            var walkers = new List<Pawn>();
            Map map = VisitorUtility.PlayerCapitalMap();
            bool toCaravan = map == null && caravan != null && !caravan.Destroyed;
            // With nowhere to put them they stay where they are, rather than vanishing.
            if (map != null || toCaravan)
            {
                foreach (Pawn p in theirs)
                {
                    f.kidnapped.RemoveKidnappedPawn(p);
                    if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                    if (toCaravan) caravan.AddPawn(p, true);
                    else walkers.Add(p);
                    returned++;
                }
            }
            if (map != null && walkers.Count > 0)
            {
                if (CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map,
                        CellFinder.EdgeRoadChance_Friendly, out IntVec3 edge))
                {
                    foreach (Pawn p in walkers)
                        GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 4, null), map);
                }
                else
                    DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, walkers, 110, false, false, true, false, true, null);
            }

            RelationState view = WorldComponent_Dominion.Instance.GetRelation(f, Faction.OfPlayer);
            view.favour += released * 5f;
            if (released > 0) f.TryAffectGoodwillWith(Faction.OfPlayer, released * 3, canSendMessage: false, canSendHostilityLetter: false);
            EventBus.Raise(DominionEventDefOf.Dominion_HostagesExchanged, Faction.OfPlayer, f,
                detail: "Dominion_Hostages_Detail".Translate(returned, released));
        }
    }

    /// <summary>Basic action: arrange a hostage exchange over the comms.</summary>
    public class DiplomacyAction_HostageExchange : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => HostageUtility.AnyHostages(f);

        public override float? AcceptanceChance(Faction f)
        {
            float c = HostageUtility.CachedAcceptance(f);
            return c <= 0f || c >= 1f ? c : Mathf.Clamp(c + LeadershipUtility.AcceptanceBonus(), 0.05f, 0.95f);
        }

        public override void Execute(Faction f)
        {
            Commit(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                HostageUtility.Exchange(f, out int returned, out int released);
                Report("Dominion_Hostages_Done".Translate(f.Name, returned, released), true);
            }
            else
                Report("Dominion_Hostages_Refused".Translate(f.Name), false);
        }
    }
}
