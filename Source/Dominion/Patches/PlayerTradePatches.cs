using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Trading with a faction (its caravans, settlements or ships) warms it to you and gives its economy a short lift,
    /// which E&amp;D production then feels through Dominion's economic climate. Measured by the goods' market value.
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class Patch_TradeDeal_TryExecute
    {
        /// <summary>Silver of goods traded per point of favour.</summary>
        private const float SilverPerFavour = 500f;

        /// <summary>A faction's favour from trade is capped per day, so one big haul is not a treaty.</summary>
        private const float MaxFavourPerDay = 8f;

        private static readonly Dictionary<int, float> favourToday = new Dictionary<int, float>();
        private static int favourDay = -1;

        public static void Prefix(TradeDeal __instance, out float __state)
        {
            __state = 0f;
            try
            {
                foreach (Tradeable t in __instance.AllTradeables)
                {
                    if (t == null || t.IsCurrency || t.CountToTransfer == 0 || !t.HasAnyThing) continue;
                    __state += Mathf.Abs(t.CountToTransfer) * t.BaseMarketValue;
                }
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("Measuring a trade failed: " + e.Message, 0x7D01);
            }
        }

        public static void Postfix(bool __result, ref bool actuallyTraded, float __state)
        {
            if (!__result || !actuallyTraded || __state <= 0f) return;
            try
            {
                Faction f = TradeSession.trader?.Faction;
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                if (comp == null || f == null || f.IsPlayer || !DominionScheduler.IsSimulated(f)) return;

                int day = GenDate.DaysPassed;
                if (day != favourDay)
                {
                    favourDay = day;
                    favourToday.Clear();
                }
                float given = favourToday.TryGetValue(f.loadID, out float g) ? g : 0f;
                float favour = Mathf.Min(__state / SilverPerFavour, MaxFavourPerDay - given);
                if (favour > 0f)
                {
                    favourToday[f.loadID] = given + favour;
                    comp.GetRelation(f, Faction.OfPlayer).favour += favour;
                }
                // Business is good: up to +3% for a week.
                EconomyEffects.Shock(f, Mathf.Min(0.03f, __state / 60000f), destroyGoods: false, days: 7);
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("Trade goodwill failed: " + e.Message, 0x7D02);
            }
        }
    }
}
