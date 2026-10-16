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

            float perHead = s.totalLiving > 0 ? s.wealth / s.totalLiving : 0f;
            float raw = worldWealthPerHead > 0f ? 100f * perHead / worldWealthPerHead : 100f;
            raw = Mathf.Clamp(raw, 10f, 300f);
            s.economyIndex = Mathf.Lerp(s.economyIndex, raw, 0.2f);
        }
    }
}
