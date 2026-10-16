using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The rest of E&amp;D's own simulation, tied into Dominion:
    /// <list type="bullet">
    /// <item>E&amp;D's trade between factions respects Dominion: no deals between enemies, across an embargo or closed
    /// borders, and few with a blockaded faction. Deals that do happen build goodwill between the partners.</item>
    /// <item>A faction whose stores cannot cover its upkeep (E&amp;D starves it) loses stability and legitimacy, goes into
    /// crisis (so relief aid can be sent) and makes the news.</item>
    /// <item>Pirate raids on a faction's stores hurt its economy and make the news.</item>
    /// </list>
    /// Each part is optional: a member that cannot be found only switches that part off.
    /// </summary>
    public partial class Integration_EconomicsDemography
    {
        // Trade partner filter: the lambda "p => p != actor &amp;&amp; ..." in ProcessVirtualTrade, and its closure's actor.
        private static MethodInfo tradePartnerFilter;
        private static FieldInfo tradeActorField;
        private MethodInfo processVirtualTrade;
        private FieldInfo saleLogsField;
        private FieldInfo stealLogsField;
        private static FieldInfo logTick, logAmount, logValue, logPartner, logItem;
        private bool linksPatched;

        private const int StarvationNewsCooldownDays = 15;
        private const int TheftNewsCooldownDays = 7;

        private void BindLinks(Type manager, Type stockpile, Binder b)
        {
            if (manager == null) return;
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            processVirtualTrade = manager.GetMethod("ProcessVirtualTrade", all);

            // The compiler keeps lambda and parameter names: <ProcessVirtualTrade>b__N(Faction p) on a closure with "actor".
            foreach (Type nested in manager.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                FieldInfo actor = nested.GetField("actor", all);
                if (actor == null || actor.FieldType != typeof(Faction)) continue;
                foreach (MethodInfo m in nested.GetMethods(all))
                {
                    if (!m.Name.Contains("ProcessVirtualTrade") || m.ReturnType != typeof(bool)) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Faction) && ps[0].Name == "p")
                    {
                        tradePartnerFilter = m;
                        tradeActorField = actor;
                    }
                }
            }
            b.Note(tradePartnerFilter != null ? "faction trade respects wars, embargoes, borders and blockades"
                : "trade partner filter not found: E&D trade ignores Dominion diplomacy");

            saleLogsField = manager.GetField("saleLogs", all);
            stealLogsField = manager.GetField("stealLogs", all);
            Type entry = AccessTools.TypeByName("EconomicsDemography.TradingLogEntry");
            if (entry != null)
            {
                logTick = entry.GetField("tick");
                logAmount = entry.GetField("amount");
                logValue = entry.GetField("value");
                logPartner = entry.GetField("partnerID");
                logItem = entry.GetField("itemLabel");
            }
            bool logsOk = processVirtualTrade != null && logTick != null && logAmount != null && logPartner != null;
            b.Note(logsOk && saleLogsField != null ? "faction trade builds goodwill between partners" : "trade logs not found: trade does not build goodwill");
            b.Note(logsOk && stealLogsField != null ? "pirate raids on stores make the news" : "theft logs not found: pirate raids are not reported");
            b.Note(tryConsumeWealth != null ? "upkeep shortfalls (starvation) reach Dominion" : "upkeep hook not found: E&D starvation is not seen by Dominion");
        }

        private void PatchLinks()
        {
            if (linksPatched) return;
            linksPatched = true;
            Patch(tradePartnerFilter, postfix: nameof(TradePartnerPostfix));
            Patch(processVirtualTrade, postfix: nameof(VirtualTradePostfix));
            Patch(tryConsumeWealth, prefix: nameof(ConsumeWealthPrefix));
        }

        private static void Patch(MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null) return;
            try
            {
                DominionMod.Harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(Integration_EconomicsDemography), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(Integration_EconomicsDemography), postfix) : null);
            }
            catch (Exception e)
            {
                DLog.Warning($"Could not hook Economics & Demography ({target.Name}): {e.Message}");
            }
        }

        private static Integration_EconomicsDemography Self => IntegrationRegistry.Get<Integration_EconomicsDemography>();

        private static bool Running => Current.ProgramState == ProgramState.Playing && WorldComponent_Dominion.Instance != null;

        // ------------------------------------------------------------------ trade between factions

        /// <summary>Postfix on E&amp;D's trade partner filter: Dominion decides who may trade with whom.</summary>
        public static void TradePartnerPostfix(object __instance, Faction __0, ref bool __result)
        {
            if (!__result || __0 == null || !Running) return;
            try
            {
                if (tradeActorField.GetValue(__instance) is Faction actor && !DominionTrade.CanTrade(actor, __0))
                    __result = false;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("E&D trade filter failed: " + e.Message, 0x0EDA);
            }
        }

        private int lastScanTick = -1;
        private readonly Dictionary<long, float> tradeGoodwillToday = new Dictionary<long, float>();
        private readonly Dictionary<long, int> lastTheftNews = new Dictionary<long, int>();

        /// <summary>After E&amp;D's daily trade: today's deals build goodwill, today's thefts hurt and make the news.</summary>
        public static void VirtualTradePostfix(object __instance)
        {
            if (!Running) return;
            try { Self?.ScanLogs(__instance); }
            catch (Exception e) { DLog.WarningOnce("E&D trade log scan failed: " + e.Message, 0x0EDB); }
        }

        private void ScanLogs(object manager)
        {
            int now = Find.TickManager.TicksGame;
            if (now == lastScanTick) return;
            lastScanTick = now;
            tradeGoodwillToday.Clear();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;

            // Sales: one entry per deal in the seller's log, naming the buyer.
            foreach ((int sellerId, object e) in TodaysEntries(saleLogsField?.GetValue(manager) as IDictionary, now))
            {
                if ((int)logAmount.GetValue(e) <= 0) continue;
                Faction seller = comp.Cache.FactionById(sellerId), buyer = comp.Cache.FactionById((int)logPartner.GetValue(e));
                if (!DominionScheduler.IsSimulated(seller) || !DominionScheduler.IsSimulated(buyer)) continue;
                // Trading partners warm to each other, a little at a time (at most 2 favour a day per pair).
                long key = PairKey(seller, buyer);
                float given = tradeGoodwillToday.TryGetValue(key, out float g) ? g : 0f;
                if (given >= 2f) continue;
                tradeGoodwillToday[key] = given + 0.5f;
                comp.GetRelation(seller, buyer).favour += 0.5f;
                comp.GetRelation(buyer, seller).favour += 0.5f;
            }

            // Thefts: the victim's entry has a negative amount and names the thief.
            foreach ((int victimId, object e) in TodaysEntries(stealLogsField?.GetValue(manager) as IDictionary, now))
            {
                if ((int)logAmount.GetValue(e) >= 0) continue;
                Faction victim = comp.Cache.FactionById(victimId), thief = comp.Cache.FactionById((int)logPartner.GetValue(e));
                if (thief == null || !DominionScheduler.IsSimulated(victim)) continue;
                FactionState vs = comp.GetState(victim);
                float value = Mathf.Abs(logValue != null ? (float)logValue.GetValue(e) : 0f);
                EconomyEffects.Shock(victim, -Mathf.Clamp(value / Mathf.Max(1000f, vs.wealth), 0.003f, 0.03f), destroyGoods: false, days: 6);
                vs.stability = Mathf.Max(0f, vs.stability - 0.5f);
                if (DominionScheduler.IsSimulated(thief)) comp.GetRelation(victim, thief).grievance += 3f;

                long key = PairKey(thief, victim);
                if (lastTheftNews.TryGetValue(key, out int last) && now - last < TheftNewsCooldownDays * GenDate.TicksPerDay && now >= last) continue;
                lastTheftNews[key] = now;
                string item = logItem?.GetValue(e) as string;
                EventBus.Raise(IntegrationEventDefOf.Dominion_PirateTheft, thief, victim, detail: item.NullOrEmpty() ? null : item.ToLower(), source: PackageId);
            }
        }

        private static IEnumerable<(int, object)> TodaysEntries(IDictionary logs, int now)
        {
            if (logs == null || logTick == null) yield break;
            foreach (DictionaryEntry kv in logs)
            {
                if (!(kv.Value is IList list) || !(kv.Key is int id)) continue;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && (int)logTick.GetValue(list[i]) == now)
                        yield return (id, list[i]);
            }
        }

        private static long PairKey(Faction a, Faction b) => ((long)a.loadID << 32) | (uint)b.loadID;

        // ------------------------------------------------------------------ upkeep and starvation

        /// <summary>
        /// Set when E&amp;D produces goods for a faction; its next stockpile spend is that day's upkeep. E&amp;D pays the
        /// upkeep in full when it can, and otherwise spends everything it has: that is the starvation case.
        /// </summary>
        [ThreadStatic] private static Faction pendingUpkeep;

        internal static void NoteProduction(Faction f) => pendingUpkeep = f;

        public static void ConsumeWealthPrefix(object __instance, float __0, bool __2)
        {
            Faction f = pendingUpkeep;
            if (f == null || __2 || !Running) return;
            pendingUpkeep = null;
            try
            {
                Integration_EconomicsDemography ed = Self;
                if (ed == null || ed.StockpileOf(f) != __instance) return;
                float have = (float)ed.getTotalWealth.Invoke(__instance, null);
                ed.NoteUpkeep(f, __0 > 0f ? __0 >= have - 0.5f : have <= 0.5f);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("E&D upkeep hook failed: " + e.Message, 0x0EDC);
            }
        }

        private readonly Dictionary<int, int> shortageDays = new Dictionary<int, int>();
        private readonly Dictionary<int, int> lastStarvationNews = new Dictionary<int, int>();

        private void NoteUpkeep(Faction f, bool shortfall)
        {
            if (!DominionScheduler.IsSimulated(f)) return;
            if (!shortfall)
            {
                shortageDays.Remove(f.loadID);
                return;
            }
            int days = (shortageDays.TryGetValue(f.loadID, out int d) ? d : 0) + 1;
            shortageDays[f.loadID] = days;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            if (days == 1)
            {
                // A lean day: tightened belts, grumbling.
                s.stability = Mathf.Max(0f, s.stability - 1f);
                return;
            }

            // Starving: people die (E&D does that), the government gets the blame.
            s.stability = Mathf.Max(0f, s.stability - 3f);
            s.legitimacy = Mathf.Max(0f, s.legitimacy - 2f);
            int now = Find.TickManager.TicksGame;
            DominionEventDef starvation = IntegrationEventDefOf.Dominion_Starvation;
            if (!s.InCrisis || s.crisis == starvation)
            {
                // A crisis others can send relief to; it lasts while the shortage does.
                s.crisis = starvation;
                s.crisisUntilTick = Mathf.Max(s.crisisUntilTick, now + 6 * GenDate.TicksPerDay);
            }
            if (lastStarvationNews.TryGetValue(f.loadID, out int last) && now - last < StarvationNewsCooldownDays * GenDate.TicksPerDay && now >= last) return;
            lastStarvationNews[f.loadID] = now;
            EventBus.Raise(starvation, f, magnitude: Mathf.Min(2f, days / 3f), source: PackageId);
        }

        private void ResetLinks()
        {
            lastScanTick = -1;
            tradeGoodwillToday.Clear();
            lastTheftNews.Clear();
            shortageDays.Clear();
            lastStarvationNews.Clear();
            pendingUpkeep = null;
        }
    }

    /// <summary>Who may trade with whom, as Dominion sees it. Used by E&amp;D's trade between factions.</summary>
    public static class DominionTrade
    {
        /// <summary>A blockaded faction still gets the odd deal through.</summary>
        private const float BlockadeLeak = 0.25f;

        private static int cacheTick = -1;
        private static readonly HashSet<Faction> blockaded = new HashSet<Faction>();

        public static bool CanTrade(Faction a, Faction b)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || a == null || b == null || a == b) return true;
            if (a.HostileTo(b)) return false;
            RelationState ab = comp.TryGetRelation(a, b), ba = comp.TryGetRelation(b, a);
            if (ab != null && (ab.embargo || ab.border == BorderStatus.Closed)) return false;
            if (ba != null && (ba.embargo || ba.border == BorderStatus.Closed)) return false;
            if ((IsBlockaded(a, comp) || IsBlockaded(b, comp)) && !Rand.Chance(BlockadeLeak)) return false;
            return true;
        }

        private static bool IsBlockaded(Faction f, WorldComponent_Dominion comp)
        {
            int now = Find.TickManager.TicksGame;
            if (now != cacheTick)
            {
                cacheTick = now;
                blockaded.Clear();
                foreach (Faction g in comp.Cache.Simulated)
                    if (EconomyEffects.Blockaded(g, comp)) blockaded.Add(g);
            }
            return blockaded.Contains(f);
        }
    }
}
