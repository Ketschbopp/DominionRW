using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Dominion
{
    /// <summary>
    /// Visitors from factions with spacer tech (or better) fly in by shuttle instead of walking from the map edge:
    /// the vanilla transport shuttle lands near the colony, unloads them and flies off. Once they are all out, they get
    /// the same visiting duty as walkers, and leave on foot when their visit ends.
    /// </summary>
    public class ShuttleVisit : IExposable
    {
        public Faction faction;
        public List<Pawn> pawns = new List<Pawn>();
        public MapParent mapParent;
        public int durationTicks;
        public int startTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref durationTicks, "durationTicks");
            Scribe_Values.Look(ref startTick, "startTick");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns ??= new List<Pawn>();
                pawns.RemoveAll(p => p == null);
            }
        }
    }

    public static class ShuttleVisits
    {
        /// <summary>Give up waiting for the last passengers after this long; whoever is out gets their duty.</summary>
        private const int MaxWaitTicks = 6 * GenDate.TicksPerHour;

        public static bool CanArriveByShuttle(Faction f) =>
            DominionMod.Settings.visitorShuttles && f != null && f.def.techLevel >= TechLevel.Spacer
            && TransportShipDefOf.Ship_Shuttle?.shipThing != null;

        /// <summary>Sends the party in by shuttle. False if no shuttle or landing spot is available (they walk instead).</summary>
        public static bool TryArrive(Faction f, Map map, List<Pawn> pawns, int durationTicks)
        {
            if (!CanArriveByShuttle(f) || map?.Parent == null) return false;
            try
            {
                IntVec3 spot = DropCellFinder.GetBestShuttleLandingSpot(map, f);
                if (!spot.IsValid) return false;
                // Passengers are held by the shuttle, so they cannot also be world pawns.
                foreach (Pawn p in pawns)
                    if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                TransportShip ship = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, pawns, null);
                ship.shipThing?.SetFaction(f);
                ship.ArriveAt(spot, map.Parent);
                ship.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.FlyAway);
                WorldComponent_Dominion.Instance.ShuttleVisits.Add(new ShuttleVisit
                {
                    faction = f,
                    pawns = new List<Pawn>(pawns),
                    mapParent = map.Parent,
                    durationTicks = durationTicks,
                    startTick = Find.TickManager.TicksGame,
                });
                return true;
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("Shuttle arrival failed, visitors will walk in: " + e.Message, 0x5A77);
                return false;
            }
        }

        /// <summary>True while the pawn is still on a shuttle heading for the colony.</summary>
        public static bool InTransit(Pawn p)
        {
            if (p == null || p.Dead || p.Spawned) return false;
            foreach (ShuttleVisit v in WorldComponent_Dominion.Instance.ShuttleVisits)
                if (v.pawns.Contains(p)) return true;
            return false;
        }

        /// <summary>Gives the visiting duty to passengers once they are out of the shuttle.</summary>
        public static void Tick(List<ShuttleVisit> visits)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = visits.Count - 1; i >= 0; i--)
            {
                ShuttleVisit v = visits[i];
                Map map = v.mapParent?.Map;
                List<Pawn> waiting = v.pawns.Where(p => p != null && !p.Dead && !p.Destroyed).ToList();
                List<Pawn> landed = waiting.Where(p => p.Spawned && p.Map == map).ToList();
                bool allOut = landed.Count == waiting.Count;
                bool timedOut = now - v.startTick > MaxWaitTicks;
                if (map == null || waiting.Count == 0 || (!allOut && !timedOut)) continue;
                visits.RemoveAt(i);
                if (landed.Count == 0) continue;
                landed.RemoveAll(p => p.GetLord() != null);
                if (landed.Count > 0) VisitorUtility.MakeVisitLord(v.faction, map, landed, v.durationTicks, landed[0].Position);
            }
        }
    }
}
