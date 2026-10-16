using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Prices at the trade screen. Every Dominion price effect (embargoes, trade agreements, later plagues and
    /// shortages) is combined here once, multiplicatively, on top of whatever other mods such as E&amp;D set.
    /// </summary>
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Patch_Tradeable_GetPriceFor
    {
        public static void Postfix(TradeAction action, ref float __result)
        {
            Faction trader = TradeSession.trader?.Faction;
            if (trader == null || trader.IsPlayer) return;
            __result *= DominionPricing.Multiplier(trader, action);
        }
    }

    public static class DominionPricing
    {
        public const float EmbargoMarkup = 0.4f;

        // The trade screen asks for every item every frame; cache per faction per tick.
        private static Faction cachedFaction;
        private static int cachedTick = -1;
        private static float cachedBuy = 1f, cachedSell = 1f;

        public static float Multiplier(Faction trader, TradeAction action)
        {
            int now = Find.TickManager.TicksGame;
            if (trader != cachedFaction || now != cachedTick)
            {
                Compute(trader, out cachedBuy, out cachedSell);
                cachedFaction = trader;
                cachedTick = now;
            }
            return action == TradeAction.PlayerBuys ? cachedBuy : action == TradeAction.PlayerSells ? cachedSell : 1f;
        }

        public static void Compute(Faction trader, out float buy, out float sell)
        {
            buy = sell = 1f;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction player = Faction.OfPlayerSilentFail;
            if (comp == null || player == null) return;

            if (comp.TryGetRelation(trader, player)?.embargo ?? false)
            {
                buy *= 1f + EmbargoMarkup;
                sell *= 1f - EmbargoMarkup;
            }
            float bonus = 0f;
            foreach (Treaty t in comp.Treaties.All)
                if (t.def.tradeBonus > 0f && t.Between(player, trader)) bonus += t.def.tradeBonus;
            bonus += CouncilUtility.TradeBonus(player, trader);
            buy *= 1f - bonus;
            sell *= 1f + bonus;
        }
    }

    /// <summary>
    /// Visitors and trader caravans respect borders and embargoes. Several incident workers override the check,
    /// so every override is patched.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_FactionCanBeGroupSource
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var types = new List<System.Type> { typeof(IncidentWorker_NeutralGroup) };
            types.AddRange(GenTypes.AllSubclasses(typeof(IncidentWorker_NeutralGroup)));
            foreach (System.Type t in types)
            {
                MethodInfo m = t.GetMethod("FactionCanBeGroupSource", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (m != null && !m.IsAbstract) yield return m;
            }
        }

        // The faction is bound by position (__0): overrides may name the parameter differently.
        public static void Postfix(IncidentWorker_NeutralGroup __instance, Faction __0, ref bool __result)
        {
            Faction f = __0;
            if (!__result || f == null || f.IsPlayer) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction player = Faction.OfPlayerSilentFail;
            if (comp == null || player == null) return;

            bool trader = __instance is IncidentWorker_TraderCaravanArrival;
            RelationState mine = comp.TryGetRelation(player, f);
            RelationState theirs = comp.TryGetRelation(f, player);
            if (mine != null)
            {
                if (mine.border == BorderStatus.Closed || (mine.border == BorderStatus.Restricted && !trader)) __result = false;
                if (mine.embargo && trader) __result = false;
            }
            if (theirs != null)
            {
                if (theirs.border == BorderStatus.Closed) __result = false;
                if (theirs.embargo && trader) __result = false;
            }
        }
    }

    /// <summary>No trading at a settlement under an embargo either way, or whose faction has closed its borders to you.</summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.CanTradeNow), MethodType.Getter)]
    public static class Patch_Settlement_CanTradeNow
    {
        public static void Postfix(Settlement __instance, ref bool __result)
        {
            if (!__result) return;
            Faction f = __instance.Faction;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction player = Faction.OfPlayerSilentFail;
            if (f == null || f.IsPlayer || comp == null || player == null) return;
            RelationState mine = comp.TryGetRelation(player, f);
            RelationState theirs = comp.TryGetRelation(f, player);
            if ((mine?.embargo ?? false) || (theirs?.embargo ?? false) || theirs?.border == BorderStatus.Closed)
                __result = false;
        }
    }
}
