using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The one place Dominion changes a faction's economy. Everything that hurts or helps an economy (wars, embargoes,
    /// blockades, sabotage, disasters, treaties, booms) goes through here, and with Economics &amp; Demography it reaches
    /// E&amp;D itself:
    /// <list type="bullet">
    /// <item>a lasting <b>shock</b> that fades over about a month (instead of the economy index snapping back in days);</item>
    /// <item>the daily <b>climate</b> (shock + wars, embargoes, blockades, sabotage, crisis, treaties, stability) scales
    /// E&amp;D's own production for the faction, so its stockpile and wealth really grow or shrink;</item>
    /// <item>losses destroy stockpile goods, and payments move real silver between E&amp;D stockpiles (and the player).</item>
    /// </list>
    /// </summary>
    public static class EconomyEffects
    {
        public const float MinShock = -0.6f;
        public const float MaxShock = 0.4f;

        /// <summary>The shock fades this share a day (about a 30-day half-life).</summary>
        public const float ShockDecay = 0.023f;

        private static Integration_EconomicsDemography ED
        {
            get
            {
                var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
                return ed != null && ed.IsActive ? ed : null;
            }
        }

        /// <summary>
        /// A lasting change to a faction's economy (−0.08 = 8% worse). Losses also destroy part of its E&amp;D stockpile,
        /// so the damage is real goods, not just a number.
        /// </summary>
        public static void Shock(Faction f, float delta, bool destroyGoods = true)
        {
            if (f == null || f.IsPlayer || delta == 0f) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || !DominionScheduler.IsSimulated(f)) return;
            FactionState s = comp.GetState(f);
            s.economyShock = Mathf.Clamp(s.economyShock + delta, MinShock, MaxShock);
            s.economyIndex = Mathf.Clamp(s.economyIndex * (1f + delta), 10f, 300f);
            if (delta < 0f && destroyGoods && s.wealth > 0f)
                ED?.TryConsumeWealth(f, s.wealth * Mathf.Min(0.15f, -delta * 0.5f));
        }

        /// <summary>Destroys goods worth <paramref name="value"/> silver in a faction's stockpile (fires, looting, blockades).</summary>
        public static void DestroyGoods(Faction f, float value)
        {
            if (f == null || f.IsPlayer || value <= 0f) return;
            ED?.TryConsumeWealth(f, value);
        }

        /// <summary>Called daily: the shock fades.</summary>
        public static void Decay(FactionState s)
        {
            s.economyShock *= 1f - ShockDecay;
            if (Mathf.Abs(s.economyShock) < 0.002f) s.economyShock = 0f;
        }

        /// <summary>
        /// Everything that pushes a faction's economy up or down today, as a share (0.1 = +10%). Worked out once a day
        /// and cached on the faction state, because E&amp;D asks for it whenever it produces goods.
        /// </summary>
        public static float ComputeClimate(Faction f, FactionState s, WorldComponent_Dominion comp, StringBuilder explain = null)
        {
            float c = 0f;
            void Add(float v, string key)
            {
                if (Mathf.Abs(v) < 0.0005f) return;
                c += v;
                explain?.AppendLine("  " + key.Translate() + ": " + v.ToStringPercentSigned());
            }

            Add(s.economyShock, "Dominion_Climate_Shock");
            Add(-Mathf.Min(0.18f, 0.06f * s.activeWars), "Dominion_Climate_Wars");
            Add(-0.04f * EmbargoesAgainst(f, comp), "Dominion_Climate_Embargoes");
            Add(-0.02f * Mathf.Min(3, s.coldWars), "Dominion_Climate_ColdWars");
            Add(Blockaded(f, comp) ? -0.12f : 0f, "Dominion_Climate_Blockade");
            Add(Find.TickManager.TicksGame < s.sabotagedUntilTick ? -0.08f : 0f, "Dominion_Climate_Sabotage");
            Add(s.InCrisis ? -0.08f : 0f, "Dominion_Climate_Crisis");
            Add(TreatyBonus(f, comp), "Dominion_Climate_Treaties");
            Add(CouncilUtility.EconomyBonus(f), "Dominion_Climate_Council");
            Add((s.stability - 50f) / 400f, "Dominion_Climate_Stability");
            Add(IntegrationRegistry.Sum(i => i.EconomyModifier(f)), "Dominion_Climate_Mods");
            return Mathf.Clamp(c, -0.7f, 0.6f);
        }

        /// <summary>E&amp;D production multiplier: 1 + today's climate, never below 0.3.</summary>
        public static float ProductionFactor(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || f == null || !comp.TryGetState(f, out FactionState s)) return 1f;
            return Mathf.Clamp(1f + s.climate, 0.3f, 1.6f);
        }

        private static float TreatyBonus(Faction f, WorldComponent_Dominion comp)
        {
            float sum = 0f;
            foreach (Treaty t in comp.Treaties.All)
            {
                if (!t.Involves(f)) continue;
                sum += t.def.economyBonus;
                if (t.def.tribute > 0f) sum += t.a == f ? -t.def.tribute : t.def.tribute * 0.7f;
            }
            return sum;
        }

        public static int EmbargoesAgainst(Faction f, WorldComponent_Dominion comp)
        {
            int n = 0;
            foreach (Faction other in comp.Cache.Simulated)
                if (other != f && (comp.TryGetRelation(other, f)?.embargo ?? false)) n++;
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null && (comp.TryGetRelation(player, f)?.embargo ?? false)) n++;
            return n;
        }

        private static bool Blockaded(Faction f, WorldComponent_Dominion comp)
        {
            foreach (Blockade b in comp.Wars.blockades)
                if (b.settlement != null && b.settlement.Faction == f && b.StillValid) return true;
            foreach (Warband w in WarbandUtility.All())
                if (w.Blockading && w.target == f) return true;
            return false;
        }

        // ---------------------------------------------------------------- payments

        /// <summary>Silver the player hands over (gifts, bribes, aid, concessions) ends up in the faction's E&amp;D stockpile.</summary>
        public static void Receive(Faction f, int silver)
        {
            if (f == null || f.IsPlayer || silver <= 0) return;
            ED?.AddSilver(f, silver);
        }

        /// <summary>
        /// Moves wealth worth <paramref name="value"/> silver from one faction to another. Between AI factions it is
        /// real E&amp;D silver (goods are sold off when the silver runs out); to the player it arrives by drop pod; from
        /// the player it is taken from the colony. Returns the amount that actually changed hands.
        /// </summary>
        public static int Transfer(Faction payer, Faction receiver, int value)
        {
            if (payer == null || receiver == null || value <= 0) return 0;
            int moved;
            if (payer.IsPlayer)
            {
                moved = DiplomacyHelpers.TakeSilver(value);
                if (moved > 0) ED?.AddSilver(receiver, moved);
                return moved;
            }
            Integration_EconomicsDemography ed = ED;
            if (ed != null)
            {
                moved = ed.TakeSilver(payer, value);
                if (moved < value && ed.TryConsumeWealth(payer, value - moved)) moved = value; // the rest in goods
            }
            else moved = value;
            if (moved <= 0) return 0;
            if (receiver.IsPlayer)
            {
                if (!DiplomacyHelpers.DropSilver(moved))
                {
                    ed?.AddSilver(payer, moved); // nowhere to deliver it: it stays home
                    return 0;
                }
            }
            else ed?.AddSilver(receiver, moved);
            return moved;
        }
    }
}
