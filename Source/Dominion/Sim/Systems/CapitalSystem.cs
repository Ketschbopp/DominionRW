using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Keeps every faction's capital valid. Custom Base Framework's seat wins when present; otherwise the
    /// oldest settlement. Losing a capital (destroyed or captured) is a political event.
    /// </summary>
    public class CapitalSystem : SimSystem
    {
        public override int Order => 2;

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(faction);
            List<Settlement> settlements = comp.Cache.SettlementsOf(faction);
            Settlement current = s.capital;
            bool valid = current != null && !current.Destroyed && current.Faction == faction;

            if (!valid && current != null)
            {
                // Some mods (FTV invasions) replace a captured settlement with a new one on the same tile.
                Faction captor = current.Destroyed ? Find.WorldObjects.SettlementAt(current.Tile)?.Faction : current.Faction;
                if (captor == faction) captor = null;
                s.capital = null;
                s.capitalTile = -1;
                EventBus.Raise(DominionEventDefOf.Dominion_CapitalLost, faction, captor, current.Tile.tileId);
            }
            else if (current == null && s.capitalTile >= 0)
            {
                // The capital was destroyed before a save: the reference came back empty, but the tile was remembered.
                int tile = s.capitalTile;
                s.capitalTile = -1;
                Faction captor = Find.WorldObjects.SettlementAt(new PlanetTile(tile))?.Faction;
                EventBus.Raise(DominionEventDefOf.Dominion_CapitalLost, faction, captor == faction ? null : captor, tile);
            }

            if (settlements.Count == 0) return;

            Settlement best = ChooseCapital(settlements, faction);
            if (best == null) return;
            if (s.capital == null || (best != s.capital && IsCbfSeat(best) && !IsCbfSeat(s.capital)))
            {
                bool hadCapital = current != null;
                s.capital = best;
                if (hadCapital)
                    EventBus.Raise(DominionEventDefOf.Dominion_CapitalEstablished, faction, null, best.Tile.tileId);
            }
            s.capitalTile = s.capital.Tile.tileId;
        }

        public static Settlement ChooseCapital(List<Settlement> settlements, Faction faction = null)
        {
            Settlement oldest = null;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement st = settlements[i];
                // The cache can be a day old: skip settlements that changed hands since.
                if (st.Destroyed || (faction != null && st.Faction != faction)) continue;
                if (IsCbfSeat(st)) return st;
                if (oldest == null || st.ID < oldest.ID) oldest = st;
            }
            return oldest;
        }

        private static bool IsCbfSeat(Settlement s) =>
            IntegrationRegistry.Get<Integration_CustomBaseFramework>()?.IsSeat(s) ?? false;
    }
}
