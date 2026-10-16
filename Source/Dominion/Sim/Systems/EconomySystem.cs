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

        public override void OnWorldLoaded() => worldWealthPerHead = -1f;

        public override void DailyGlobal()
        {
            ComputeWorldAverage();
            WorldComponent_Dominion.Instance.PruneMissingFactions();
        }

        private void ComputeWorldAverage()
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
            int embargoes = EconomyEffects.EmbargoesAgainst(faction, comp);
            if (embargoes > 0 && ed != null && DominionMod.Settings.embargoDrain)
                ed.TryConsumeWealth(faction, s.wealth * Mathf.Min(0.004f, 0.001f * embargoes));

            // After a load the world average is unknown until midnight; work it out now rather than drift toward 100.
            if (worldWealthPerHead < 0f) ComputeWorldAverage();
            float perHead = s.totalLiving > 0 ? s.wealth / s.totalLiving : 0f;
            float raw = worldWealthPerHead > 0f ? 100f * perHead / worldWealthPerHead : 100f;
            // The climate also drives E&D's own production (see EconomyEffects), so its effect shows up in wealth over time.
            EconomyEffects.Decay(s);
            s.climate = EconomyEffects.ComputeClimate(faction, s, comp);
            raw *= 1f + s.climate;
            raw = Mathf.Clamp(raw, 10f, 300f);
            s.economyIndex = Mathf.Lerp(s.economyIndex, raw, 0.2f);
        }

    }
}
