using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Dominion
{
    public static class DelegationUtility
    {
        /// <summary>Where a faction's delegations set out from: its capital, else any settlement, else -1.</summary>
        public static PlanetTile HomeTile(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (f.IsPlayer)
            {
                Settlement cap = comp.Player.capital;
                if (cap != null && !cap.Destroyed) return cap.Tile;
                Map home = Find.AnyPlayerHomeMap;
                return home != null ? home.Tile : PlanetTile.Invalid;
            }
            if (comp.TryGetState(f, out FactionState s) && s.capital != null && !s.capital.Destroyed) return s.capital.Tile;
            List<Settlement> settlements = comp.Cache.SettlementsOf(f);
            return settlements.Count > 0 ? settlements[0].Tile : PlanetTile.Invalid;
        }

        /// <summary>Sends a delegation from one faction to another. Returns null if there is no route (callers then act at once).</summary>
        public static Delegation Dispatch(Faction from, Faction to, DelegationPurpose purpose)
        {
            PlanetTile start = HomeTile(from);
            PlanetTile end = HomeTile(to);
            if (!start.Valid || !end.Valid || start == end) return null;
            // Delegations walk the surface; a home in orbit (Odyssey) has no road to anywhere.
            if (start.Layer != end.Layer || start.Layer != Find.WorldGrid.Surface) return null;

            var d = (Delegation)WorldObjectMaker.MakeWorldObject(DominionWorldObjectDefOf.Dominion_Delegation);
            d.SetFaction(from);
            d.purpose = purpose;
            d.target = to;
            if (!d.SetRoute(start, end)) return null;
            Find.WorldObjects.Add(d);
            EventBus.Raise(DominionEventDefOf.Dominion_DelegationDispatched, from, to, start.tileId);
            return d;
        }

        /// <summary>True if a delegation between the two (either way) is already on the road.</summary>
        public static bool AnyPending(Faction a, Faction b)
        {
            foreach (WorldObject o in Find.WorldObjects.AllWorldObjects)
                if (o is Delegation d && ((d.Faction == a && d.target == b) || (d.Faction == b && d.target == a)))
                    return true;
            return false;
        }

        /// <summary>Two AI factions agree on something; the agreement takes effect when their delegation arrives.</summary>
        public static void DispatchAgreement(Faction from, Faction to, AgreementKind kind, TreatyDef treaty = null)
        {
            if (AnyPending(from, to)) return; // envoys are already on the road
            Delegation d = Dispatch(from, to, DelegationPurpose.Agreement);
            if (d == null)
            {
                Conclude(from, to, kind, treaty);
                return;
            }
            d.agreement = kind;
            d.treaty = treaty;
        }

        public static void Arrived(Delegation d)
        {
            Faction from = d.Faction, to = d.target;
            if (from == null || to == null || from.defeated || to.defeated) return;
            switch (d.purpose)
            {
                case DelegationPurpose.Agreement:
                    Conclude(from, to, d.agreement, d.treaty);
                    break;
                case DelegationPurpose.OfferToPlayer:
                    PlayerPolicySystem.EnvoyArrived(from, d.treaty);
                    break;
                case DelegationPurpose.SummitToPlayer:
                    SummitSystem.DelegationArrived(d.summitId, from);
                    break;
                case DelegationPurpose.CouncilDelegate:
                    CouncilSystem.DelegateArrived(from);
                    break;
            }
        }

        /// <summary>Carries out an agreement if it still makes sense when the envoys meet.</summary>
        private static void Conclude(Faction from, Faction to, AgreementKind kind, TreatyDef treaty)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            switch (kind)
            {
                case AgreementKind.Peace:
                    if (from.HostileTo(to))
                        DiplomacyUtility.SetRelationKind(from, to, FactionRelationKind.Neutral, from, "Dominion_Reason_Peace".Translate());
                    break;
                case AgreementKind.Alliance:
                    if (comp.Treaties.CannotSignReason(DominionTreatyDefOf.Dominion_Alliance, from, to) == null)
                        comp.Treaties.Sign(DominionTreatyDefOf.Dominion_Alliance, from, to, from);
                    break;
                case AgreementKind.Treaty:
                    if (treaty != null && comp.Treaties.CannotSignReason(treaty, from, to) == null)
                        comp.Treaties.Sign(treaty, from, to, from);
                    break;
            }
        }

        /// <summary>A player caravan stops a delegation. It is an act of war: the escort fights on a real map.</summary>
        public static void Intercept(Delegation d, Caravan caravan)
        {
            // The world map keeps running while the menu is open: the delegation may have moved on or arrived.
            if (d == null || d.Destroyed || caravan == null || caravan.Destroyed || caravan.Tile != d.Tile) return;
            Faction f = d.Faction;
            Faction player = Faction.OfPlayer;
            if (!f.HostileTo(player))
                DiplomacyUtility.SetRelationKind(player, f, FactionRelationKind.Hostile, player, "Dominion_Reason_Intercepted".Translate());

            var enemies = new List<Pawn>();
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            var parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                faction = f,
                tile = caravan.Tile,
                points = Mathf.Clamp(200f + s.militaryStrength * 0.5f, 200f, 1200f),
                generateFightersOnly = true,
            };
            enemies.AddRange(PawnGroupMakerUtility.GeneratePawns(parms, false));

            // The envoy, or the leader in person if travelling to a summit.
            Pawn leader = f.leader;
            if (d.purpose == DelegationPurpose.SummitToPlayer && VisitorUtility.LeaderIsFree(leader))
            {
                Find.WorldPawns.RemovePawn(leader);
                enemies.Add(leader);
            }
            else
            {
                PawnKindDef kind = f.def.basicMemberKind ?? f.RandomPawnKind();
                if (kind != null) enemies.Add(PawnGenerator.GeneratePawn(kind, f, null));
            }

            EventBus.Raise(DominionEventDefOf.Dominion_DelegationIntercepted, player, f, caravan.Tile.tileId, detail: d.target?.Name);
            if (d.purpose == DelegationPurpose.SummitToPlayer)
                SummitSystem.Cancel(d.summitId, "Dominion_Summit_CancelledIntercepted".Translate());
            d.Destroy();

            if (enemies.Count == 0) return;
            Map map = CaravanIncidentUtility.SetupCaravanAttackMap(caravan, enemies, true);
            LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, true, true, false, false, true, false, true), map, enemies);
            Find.LetterStack.ReceiveLetter("Dominion_Delegation_InterceptedLabel".Translate(), "Dominion_Delegation_InterceptedText".Translate(f.Name),
                LetterDefOf.ThreatBig, new LookTargets(enemies), f);
        }
    }

    /// <summary>Spawns friendly visitors (envoys, leaders and their guards) that walk in, linger and leave.</summary>
    public static class VisitorUtility
    {
        public static Map PlayerCapitalMap()
        {
            Map map = WorldComponent_Dominion.Instance?.Player.capital?.Map;
            return map ?? Find.AnyPlayerHomeMap;
        }

        /// <summary>Whether visitors can walk in on this map at all. Check before taking pawns out of the world.</summary>
        public static bool CanHostVisitors(Map map) => map != null && TryFindEntry(map, out _);

        private static bool TryFindEntry(Map map, out IntVec3 edge) =>
            CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map,
                CellFinder.EdgeRoadChance_Friendly, out edge);

        /// <summary>A faction leader who is free in the world: not in a caravan, not anyone's prisoner, not on a map.</summary>
        public static bool LeaderIsFree(Pawn leader) =>
            leader != null && !leader.Dead && !leader.Spawned && !leader.Discarded && !leader.IsPrisoner && leader.GetCaravan() == null
            && Find.WorldPawns.Contains(leader)
            && Find.WorldPawns.GetSituation(leader) is WorldPawnSituation.Free or WorldPawnSituation.FactionLeader;

        public static bool SpawnVisitors(Faction f, Map map, List<Pawn> pawns, int durationTicks)
        {
            if (map == null || pawns.Count == 0) return false;
            if (!TryFindEntry(map, out IntVec3 edge))
                return false;
            foreach (Pawn p in pawns)
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 5, null), map);
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(edge, map, out IntVec3 chill))
                chill = edge;
            LordMaker.MakeNewLord(f, new LordJob_VisitColony(f, chill, durationTicks), map, pawns);
            return true;
        }

        /// <summary>An envoy with a couple of guards.</summary>
        public static List<Pawn> MakeEnvoyParty(Faction f, Pawn principal = null, int guards = 2)
        {
            var pawns = new List<Pawn>();
            if (principal != null) pawns.Add(principal);
            else
            {
                PawnKindDef kind = f.def.basicMemberKind ?? f.RandomPawnKind();
                if (kind != null) pawns.Add(PawnGenerator.GeneratePawn(kind, f, null));
            }
            if (guards <= 0) return pawns;
            float points = Mathf.Max(150f * guards, f.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
            var parms = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, faction = f, points = points, generateFightersOnly = true };
            if (f.def.pawnGroupMakers == null || !f.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat)) return pawns;
            List<Pawn> generated = PawnGroupMakerUtility.GeneratePawns(parms, false).ToList();
            for (int i = 0; i < generated.Count; i++)
            {
                if (i < guards) pawns.Add(generated[i]);
                else generated[i].Discard(); // more than we need
            }
            return pawns;
        }
    }

    [DefOf]
    public static class DominionWorldObjectDefOf
    {
        public static WorldObjectDef Dominion_Delegation;

        static DominionWorldObjectDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionWorldObjectDefOf));
        }
    }
}
