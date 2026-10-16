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
                Faction captor = current.Destroyed ? null : current.Faction;
                s.capital = null;
                EventBus.Raise(DominionEventDefOf.Dominion_CapitalLost, faction, captor, current.Tile.tileId);
            }

            if (settlements.Count == 0) return;

            Settlement best = ChooseCapital(settlements);
            if (s.capital == null || (best != s.capital && IsCbfSeat(best) && !IsCbfSeat(s.capital)))
            {
                bool hadCapital = current != null;
                s.capital = best;
                if (hadCapital)
                    EventBus.Raise(DominionEventDefOf.Dominion_CapitalEstablished, faction, null, best.Tile.tileId);
            }
        }

        public static Settlement ChooseCapital(List<Settlement> settlements)
        {
            Settlement oldest = null;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement st = settlements[i];
                if (st.Destroyed) continue;
                if (IsCbfSeat(st)) return st;
                if (oldest == null || st.ID < oldest.ID) oldest = st;
            }
            return oldest;
        }

        private static bool IsCbfSeat(Settlement s) =>
            IntegrationRegistry.Get<Integration_CustomBaseFramework>()?.IsSeat(s) ?? false;
    }
}
