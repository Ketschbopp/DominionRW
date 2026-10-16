using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Reads each faction's population and wealth from Economics &amp; Demography and turns them into an
    /// economy index (100 = world average wealth per head).
    /// </summary>
    public class EconomySystem : SimSystem
    {
        public override int Order => 0;

        // Degraded values if E&D could not be bound (e.g. no 1.7 build yet), so the rest of the sim still runs.
        private const int FallbackPopulationPerSettlement = 25;
        private const float FallbackWealthPerSettlement = 5000f;

        private float worldWealthPerHead = -1f;

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            float wealth = 0f;
            int living = 0;
            foreach (Faction f in comp.Cache.Simulated)
            {
                if (!comp.TryGetState(f, out FactionState s) || s.totalLiving <= 0) continue;
                wealth += s.wealth;
                living += s.totalLiving;
            }
            worldWealthPerHead = living > 0 ? wealth / living : -1f;
            comp.PruneMissingFactions();
        }

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(faction);
            s.settlementCount = comp.Cache.SettlementsOf(faction).Count;

            var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
            if (ed != null && ed.TryRead(faction, out int adults, out int living, out float wealth))
            {
                s.population = adults;
                s.totalLiving = living;
                s.wealth = wealth;
            }
            else
            {
                s.population = s.settlementCount * FallbackPopulationPerSettlement;
                s.totalLiving = Mathf.RoundToInt(s.population * 1.4f);
                s.wealth = s.settlementCount * FallbackWealthPerSettlement;
            }

            // Embargoes cost real goods: a small share of the stockpile is lost each day while they last.
            int embargoes = EmbargoesAgainst(faction, comp);
            if (embargoes > 0 && ed != null && DominionMod.Settings.embargoDrain)
                ed.TryConsumeWealth(faction, s.wealth * Mathf.Min(0.01f, 0.002f * embargoes));

            float perHead = s.totalLiving > 0 ? s.wealth / s.totalLiving : 0f;
            float raw = worldWealthPerHead > 0f ? 100f * perHead / worldWealthPerHead : 100f;
            raw *= 1f + TreatyEconomyBonus(faction, comp) + CouncilUtility.EconomyBonus(faction) - 0.04f * embargoes
                   + IntegrationRegistry.Sum(i => i.EconomyModifier(faction));
            raw = Mathf.Clamp(raw, 10f, 300f);
            s.economyIndex = Mathf.Lerp(s.economyIndex, raw, 0.2f);
        }

        private static float TreatyEconomyBonus(Faction f, WorldComponent_Dominion comp)
        {
            float sum = 0f;
            foreach (Treaty t in comp.Treaties.All)
                if (t.Involves(f)) sum += t.def.economyBonus;
            return sum;
        }

        private static int EmbargoesAgainst(Faction f, WorldComponent_Dominion comp)
        {
            int n = 0;
            foreach (Faction other in comp.Cache.Simulated)
                if (other != f && (comp.TryGetRelation(other, f)?.embargo ?? false)) n++;
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && (comp.TryGetRelation(player, f)?.embargo ?? false)) n++;
            return n;
        }
    }
}
