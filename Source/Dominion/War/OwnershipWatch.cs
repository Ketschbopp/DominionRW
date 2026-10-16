using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Keeps Dominion in step with settlements changing hands outside it: Faction Territories' invasions (including the
    /// ones Dominion's warbands start), quests and other mods. Every hour it compares each tile's owner with the last
    /// known one; a change is an occupation (news, war score, people and wealth moving), and an FTV siege that ends
    /// with the defender still in charge is a battle the defender won.
    /// </summary>
    public class OwnershipWatch : SimSystem
    {
        public override int Order => 33;

        private static readonly Dictionary<int, int> ownerByTile = new Dictionary<int, int>();
        private static readonly Dictionary<int, (int tile, int attacker, int defender)> sieges = new Dictionary<int, (int, int, int)>();
        private static bool primed;

        public override void OnWorldLoaded()
        {
            ownerByTile.Clear();
            sieges.Clear();
            primed = false;
        }

        /// <summary>Dominion moved this settlement itself: no second report.</summary>
        public static void Note(Settlement s)
        {
            if (s?.Faction != null) ownerByTile[s.Tile.tileId] = s.Faction.loadID;
        }

        public override void Hourly()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            List<Settlement> settlements = Find.WorldObjects.Settlements;
            var seen = new HashSet<int>();
            var changed = new List<(Settlement s, Faction former)>();
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                if (s.Faction == null) continue;
                int tile = s.Tile.tileId;
                seen.Add(tile);
                if (ownerByTile.TryGetValue(tile, out int before) && before != s.Faction.loadID && primed)
                {
                    Faction former = comp.Cache.FactionById(before);
                    if (former != null) changed.Add((s, former));
                }
                ownerByTile[tile] = s.Faction.loadID;
            }
            if (ownerByTile.Count != seen.Count)
            {
                var gone = new List<int>();
                foreach (int t in ownerByTile.Keys)
                    if (!seen.Contains(t)) gone.Add(t);
                foreach (int t in gone) ownerByTile.Remove(t);
            }
            if (changed.Count > 0) comp.Cache.Rebuild();
            foreach ((Settlement s, Faction former) in changed)
                ExternalCapture(s, former, comp);

            WatchSieges(comp);
            primed = true;
        }

        private static void ExternalCapture(Settlement s, Faction former, WorldComponent_Dominion comp)
        {
            Faction taker = s.Faction;
            int tile = s.Tile.tileId;
            comp.Wars.siegePoints.Remove(tile);
            if (taker == null || taker == former || taker.IsPlayer || former.IsPlayer) return;
            WarRegistry reg = comp.Wars;
            if (!reg.originalOwner.ContainsKey(tile)) reg.originalOwner[tile] = former.loadID;
            if (reg.originalOwner[tile] == taker.loadID)
            {
                reg.originalOwner.Remove(tile);
                EventBus.Raise(DominionEventDefOf.Dominion_SettlementReturned, taker, former, tile, detail: s.Label);
            }
            else
                EventBus.Raise(DominionEventDefOf.Dominion_SettlementOccupied, taker, former, tile, detail: s.Label);
            WarUtility.AddScore(taker, former, 15f);
            WarUtility.SettlementChangedHands(former, taker);
        }

        /// <summary>FTV sieges: news when one begins; a defender's win when one ends and the town did not change hands.</summary>
        private static void WatchSieges(WorldComponent_Dominion comp)
        {
            var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            if (ftv == null || !ftv.CanInvade) return;
            var now = new HashSet<int>();
            foreach ((int tile, int attacker, int defender, int id) in ftv.Invasions())
            {
                now.Add(id);
                if (sieges.ContainsKey(id)) continue;
                sieges[id] = (tile, attacker, defender);
                Faction a = comp.Cache.FactionById(attacker), d = comp.Cache.FactionById(defender);
                if (!primed || a == null || d == null) continue;
                ftv.EnsureWarForSiege(a, d); // AI-vs-AI siege without a Dominion war: make it one (integration)
                Settlement s = Find.WorldObjects.SettlementAt(new PlanetTile(tile));
                EventBus.Raise(DominionEventDefOf.Dominion_SiegeBegan, a, d, tile, detail: s?.Label);
            }
            if (sieges.Count == now.Count) return;
            var ended = new List<int>();
            foreach (KeyValuePair<int, (int tile, int attacker, int defender)> kv in sieges)
                if (!now.Contains(kv.Key)) ended.Add(kv.Key);
            foreach (int id in ended)
            {
                (int tile, int attacker, int defender) = sieges[id];
                sieges.Remove(id);
                comp.Wars.siegePoints.Remove(tile);
                Settlement s = Find.WorldObjects.SettlementAt(new PlanetTile(tile));
                Faction a = comp.Cache.FactionById(attacker), d = comp.Cache.FactionById(defender);
                if (s == null || a == null || d == null || s.Faction != d) continue; // taken (reported above) or gone
                EventBus.Raise(DominionEventDefOf.Dominion_BattleFought, d, a, tile, detail: s.Label);
                WarUtility.AddScore(d, a, 8f);
                EconomyEffects.Shock(a, -0.02f);
            }
        }
    }
}
