using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Counts each faction's territory from FTV. The surface is scanned in small pieces every slot, so
    /// a full scan takes one in-game day and no tick does more than a few hundred lookups.
    /// Also counts FTV vassal outposts once a day.
    /// </summary>
    public class TerritorySystem : SimSystem
    {
        public override int Order => 1;

        /// <summary>The player's FTV territory and vassal outposts, refreshed daily. Not saved.</summary>
        public static int PlayerTerritoryTiles { get; private set; }
        public static int PlayerVassalOutposts { get; private set; }

        private readonly Dictionary<int, int> counting = new Dictionary<int, int>();
        private bool scanFromStart;

        // Vassal outposts per master faction yesterday. Not saved: the first day after loading only records.
        private Dictionary<int, int> lastVassals;
        private RimWorld.Planet.World lastWorld;

        public override void Slot(int slot)
        {
            var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            if (ftv == null || !ftv.IsActive || !ftv.CanReadTerritory) return;

            if (slot == 0)
            {
                counting.Clear();
                scanFromStart = true;
            }
            if (!scanFromStart) return; // wait for a full day after loading so counts are complete

            int tiles = Find.WorldGrid.Surface.TilesCount;
            int perSlot = Mathf.CeilToInt(tiles / (float)DominionScheduler.SlotsPerDay);
            int start = slot * perSlot;
            int end = Mathf.Min(tiles, start + perSlot);
            for (int t = start; t < end; t++)
            {
                if (ftv.TryGetOwner(t, out int owner) && owner >= 0)
                    counting[owner] = counting.TryGetValue(owner, out int c) ? c + 1 : 1;
            }

            if (slot == DominionScheduler.SlotsPerDay - 1)
                Commit();
        }

        private void Commit()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in comp.Cache.Simulated)
                comp.GetState(f).territoryTiles = counting.TryGetValue(f.loadID, out int c) ? c : 0;
            Faction player = Faction.OfPlayerSilentFail;
            PlayerTerritoryTiles = player != null && counting.TryGetValue(player.loadID, out int p) ? p : 0;
        }

        public override void DailyGlobal()
        {
            var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            List<Faction> simulated = comp.Cache.Simulated;

            // (original faction, master faction) -> outposts
            var held = new Dictionary<long, int>();
            var perMaster = new Dictionary<int, int>();
            ftv?.ForEachVassalOutpost((master, original) =>
            {
                if (master < 0) return;
                perMaster[master] = perMaster.TryGetValue(master, out int c) ? c + 1 : 1;
                if (original >= 0)
                {
                    long key = RelationState.Key(original, master);
                    held[key] = held.TryGetValue(key, out int h) ? h + 1 : 1;
                }
            });

            if (ftv != null && ftv.IsActive)
            {
                if (lastWorld != Find.World) lastVassals = null;
                lastWorld = Find.World;
                if (lastVassals != null)
                {
                    foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                    {
                        if (f.defeated) continue;
                        perMaster.TryGetValue(f.loadID, out int now);
                        lastVassals.TryGetValue(f.loadID, out int before);
                        if (now > before) EventBus.Raise(DominionEventDefOf.Dominion_VassalOutpostGained, f, magnitude: now - before, source: ftv.PackageId);
                        else if (now < before) EventBus.Raise(DominionEventDefOf.Dominion_VassalOutpostLost, f, magnitude: before - now, source: ftv.PackageId);
                    }
                }
                lastVassals = perMaster;
                Faction player = Faction.OfPlayerSilentFail;
                PlayerVassalOutposts = player != null && perMaster.TryGetValue(player.loadID, out int pv) ? pv : 0;
            }

            for (int i = 0; i < simulated.Count; i++)
            {
                Faction f = simulated[i];
                comp.GetState(f).vassalOutposts = perMaster.TryGetValue(f.loadID, out int c) ? c : 0;
                for (int j = 0; j < simulated.Count; j++)
                {
                    if (i == j) continue;
                    Faction g = simulated[j];
                    held.TryGetValue(RelationState.Key(f.loadID, g.loadID), out int h);
                    RelationState r = comp.TryGetRelation(f, g);
                    if (r != null) r.outpostsHeldByOther = h;
                    else if (h > 0) comp.GetRelation(f, g).outpostsHeldByOther = h;
                }
                Faction player = Faction.OfPlayerSilentFail;
                if (player != null)
                {
                    held.TryGetValue(RelationState.Key(f.loadID, player.loadID), out int ph);
                    RelationState pr = comp.TryGetRelation(f, player);
                    if (pr != null) pr.outpostsHeldByOther = ph;
                    else if (ph > 0) comp.GetRelation(f, player).outpostsHeldByOther = ph;
                }
            }
        }
    }
}
