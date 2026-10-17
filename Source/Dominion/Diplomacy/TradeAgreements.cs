using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum DealStatus { Active, Suspended, Completed, Cancelled }

    /// <summary>One good in a standing deal: so many of a thing (and its stuff) that you buy or sell each delivery.</summary>
    public class DealLine : IExposable
    {
        public ThingDef def;
        public ThingDef stuff;
        public int count;
        public bool weBuy;

        public string Label => GenLabel.ThingLabel(def, stuff, count);

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref weBuy, "weBuy");
        }
    }

    /// <summary>
    /// A standing trade deal with a faction: the goods and silver of a trade made in the vanilla trade window, repeated
    /// every so many days. Saved on <see cref="PlayerState"/>. (Named so as not to clash with vanilla's
    /// <c>RimWorld.TradeDeal</c>, the trade window's deal.)
    /// </summary>
    public class TradeAgreement : IExposable
    {
        public int id = -1;
        public Faction faction;
        public List<DealLine> lines = new List<DealLine>();

        /// <summary>Silver the player pays each delivery; negative when the faction pays the player.</summary>
        public int silver;

        public int everyDays;
        public int deliveriesLeft;
        public int deliveriesDone;
        public int nextTick;
        public int signedTick;
        public DealStatus status = DealStatus.Active;
        public string endReason;
        public int endedTick = -1;
        public int missed;

        public bool Open => status == DealStatus.Active || status == DealStatus.Suspended;

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                string get = string.Join(", ", lines.Where(l => l.weBuy).Select(l => l.Label));
                string give = string.Join(", ", lines.Where(l => !l.weBuy).Select(l => l.Label));
                if (!get.NullOrEmpty()) parts.Add("Dominion_Deal_YouGet".Translate(get));
                if (!give.NullOrEmpty()) parts.Add("Dominion_Deal_YouGive".Translate(give));
                if (silver > 0) parts.Add("Dominion_Deal_YouPay".Translate(silver));
                else if (silver < 0) parts.Add("Dominion_Deal_TheyPay".Translate(-silver));
                return string.Join("; ", parts) + "  ·  " + "Dominion_Deal_Every".Translate(everyDays, deliveriesLeft);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            Scribe_Values.Look(ref silver, "silver");
            Scribe_Values.Look(ref everyDays, "everyDays");
            Scribe_Values.Look(ref deliveriesLeft, "deliveriesLeft");
            Scribe_Values.Look(ref deliveriesDone, "deliveriesDone");
            Scribe_Values.Look(ref nextTick, "nextTick");
            Scribe_Values.Look(ref signedTick, "signedTick");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref endReason, "endReason");
            Scribe_Values.Look(ref endedTick, "endedTick", -1);
            Scribe_Values.Look(ref missed, "missed");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lines ??= new List<DealLine>();
                lines.RemoveAll(l => l?.def == null || l.count <= 0);
            }
        }
    }

    /// <summary>
    /// Trade with factions over the wire, through RimWorld's own trade window. A one-time deal opens the vanilla trade
    /// window against the faction's trade office (<see cref="FactionTradeOffice"/>), so its prices, Dominion's price
    /// effects and Economics &amp; Demography's hooks all apply as in any trade. Afterwards the player may offer to repeat
    /// the same trade every 15, 30 or 60 days: a standing deal, delivered by drop pod with the silver changing hands
    /// through <see cref="EconomyEffects.Transfer"/> and the goods coming out of (or going into) the faction's E&amp;D
    /// stockpile. A missed payment or delivery suspends a standing deal and costs goodwill; war or an embargo ends it.
    /// </summary>
    public static class TradeDeals
    {
        public static readonly int[] Frequencies = { 15, 30, 60 };
        public static readonly int[] Deliveries = { 2, 4, 6, 12 };
        public const int MissedGoodwill = -5;
        public const int CancelGoodwill = -3;

        /// <summary>A faction's trade office keeps its stock for this long, so calling again does not restock it.</summary>
        public const int OfficeDays = 5;

        private const int KeepEndedDays = 60;

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        private static Integration_EconomicsDemography ED
        {
            get
            {
                var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
                return ed != null && ed.IsActive ? ed : null;
            }
        }

        public static List<TradeAgreement> All => Comp.Player.tradeDeals;

        public static IEnumerable<TradeAgreement> Open => All.Where(d => d.Open);

        public static IEnumerable<TradeAgreement> OpenWith(Faction f) => All.Where(d => d.Open && d.faction == f);

        // ------------------------------------------------------------------ trading now

        /// <summary>Why you cannot trade with them over the wire now, or null.</summary>
        public static string CannotDealReason(Faction f)
        {
            if (Comp == null || f == null || f.IsPlayer || f.defeated) return "Dominion_Action_Invalid".Translate();
            if (LeadershipUtility.BestDesk() == null && !LeadershipUtility.HasComms()) return "Dominion_Action_NeedsComms".Translate();
            if (f.HostileTo(Faction.OfPlayer)) return "Dominion_Action_NotWhileHostile".Translate();
            if (Comp.GetRelation(f, Faction.OfPlayer).embargo) return "Dominion_Deal_TheyEmbargo".Translate(f.Name);
            if (Comp.GetRelation(Faction.OfPlayer, f).embargo) return "Dominion_Deal_WeEmbargo".Translate(f.Name);
            if (DiplomacyTalk.Shuns(f)) return "Dominion_Talk_Shuns".Translate(f.Name);
            if (TradeMap() == null) return "Dominion_Deal_NoHome".Translate();
            if (TraderKindFor(f) == null) return "Dominion_Deal_NoTraders".Translate(f.Name);
            return null;
        }

        /// <summary>The home map trades are made from: the one with the most silver by the beacons, else the capital's.</summary>
        public static Map TradeMap()
        {
            Map map = TradeUtility.PlayerHomeMapWithMostLaunchableSilver();
            return map ?? Comp?.Player.capital?.Map ?? Find.AnyPlayerHomeMap;
        }

        /// <summary>The colonist who does the talking: the head of state if at hand, otherwise the best talker on the map.</summary>
        public static Pawn Negotiator(Map map)
        {
            Pawn hos = LeadershipUtility.HeadOfState();
            if (hos != null && hos.Map == map && CanNegotiate(hos)) return hos;
            return map.mapPawns.FreeColonistsSpawned.Where(CanNegotiate).OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).FirstOrDefault();
        }

        private static bool CanNegotiate(Pawn p) =>
            p != null && !p.Downed && !p.Dead && !p.InMentalState && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking);

        /// <summary>The kind of trader a faction sends: one of its caravan traders, else a settlement trader.</summary>
        public static TraderKindDef TraderKindFor(Faction f)
        {
            List<TraderKindDef> kinds = f.def.caravanTraderKinds;
            if (kinds == null || kinds.Count == 0) kinds = f.def.baseTraderKinds;
            if (kinds == null || kinds.Count == 0) return null;
            Rand.PushState(f.loadID * 31 + GenDate.DaysPassed / OfficeDays);
            try
            {
                return kinds.RandomElementByWeight(k => k.CalculatedCommonality);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static readonly Dictionary<Faction, (FactionTradeOffice office, int until)> offices = new Dictionary<Faction, (FactionTradeOffice, int)>();

        /// <summary>
        /// Opens the vanilla trade window against the faction's trade office. Bought goods arrive by drop pod; you sell
        /// what lies within range of your orbital trade beacons, as when trading over the comms console.
        /// </summary>
        public static void OpenTrade(Faction f)
        {
            string why = CannotDealReason(f);
            if (why != null)
            {
                Messages.Message(why, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Map map = TradeMap();
            Pawn negotiator = Negotiator(map);
            if (negotiator == null)
            {
                Messages.Message("Dominion_Deal_NoNegotiator".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (!offices.TryGetValue(f, out var entry) || entry.until < now || entry.office.Map != map)
            {
                try
                {
                    entry = (new FactionTradeOffice(TraderKindFor(f), f, map), now + OfficeDays * GenDate.TicksPerDay);
                }
                catch (System.Exception e)
                {
                    DLog.WarningOnce($"Could not stock the trade office of {f.Name}: {e}", f.loadID ^ 0x7D04);
                    Messages.Message("Dominion_Deal_NoTraders".Translate(f.Name), MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                offices[f] = entry;
            }
            Find.WindowStack.Add(new Dialog_Trade(negotiator, entry.office));
        }

        /// <summary>God mode: the faction's trade office restocks next time it is called.</summary>
        public static void DevRestock(Faction f) => offices.Remove(f);

        // ------------------------------------------------------------------ standing deals

        /// <summary>The chance they agree to repeat a trade worth <paramref name="value"/> silver every <paramref name="everyDays"/> days.</summary>
        public static float StandingChance(Faction f, float value, int everyDays, StringBuilder explain = null)
        {
            float c = Term(explain, 0.6f, "Dominion_Odds_Base");
            float op = Comp.GetRelation(f, Faction.OfPlayer).opinion;
            c += Term(explain, op / 250f, "Dominion_Talk_Odds_Opinion");
            float greed = Comp.TryGetState(f, out FactionState s) ? s.personality.greed : 0.5f;
            // The greedy like a steady buyer of their goods, but drive a hard bargain on a long commitment.
            c += Term(explain, -(greed - 0.5f) * 0.2f, "Dominion_Deal_Odds_Greed");
            c += Term(explain, everyDays <= 15 ? -0.1f : everyDays >= 60 ? 0.05f : 0f, "Dominion_Deal_Odds_Pace");
            c += Term(explain, -Mathf.Clamp(value / 20000f, 0f, 0.2f), "Dominion_Deal_Odds_Size");
            bool partners = Comp.Treaties.All.Any(t => t.def.tradeBonus > 0f && t.Between(Faction.OfPlayer, f));
            if (partners) c += Term(explain, 0.15f, "Dominion_Deal_Odds_Treaty");
            Voice v = FactionVoice.Of(f);
            c += Term(explain, v == Voice.Mercantile ? 0.1f : v == Voice.Aloof ? -0.1f : 0f, "Dominion_Talk_Odds_Manner");
            if (GovernmentUtility.HasMeme(f, "VME_Trader")) c += Term(explain, 0.05f, "Dominion_Deal_Odds_Traders");
            c += Term(explain, LeadershipUtility.AcceptanceBonus(), "Dominion_Odds_Leader");
            return Mathf.Clamp(c, 0.05f, 0.95f);
        }

        private static float Term(StringBuilder explain, float value, string key)
        {
            if (explain != null && Mathf.Abs(value) >= 0.005f) DominionPaper.Term(explain, value, key.Translate());
            return value;
        }

        /// <summary>Market value of a standing deal's goods, both ways.</summary>
        public static float Value(List<DealLine> lines) => lines.Sum(l => l.def.GetStatValueAbstract(StatDefOf.MarketValue, l.stuff) * l.count);

        public static TradeAgreement Sign(Faction f, List<DealLine> lines, int silver, int everyDays, int deliveries)
        {
            PlayerState player = Comp.Player;
            int now = Find.TickManager.TicksGame;
            var deal = new TradeAgreement
            {
                id = player.nextTradeDealId++,
                faction = f,
                lines = lines,
                silver = silver,
                everyDays = everyDays,
                deliveriesLeft = deliveries,
                signedTick = now,
                nextTick = now + everyDays * GenDate.TicksPerDay,
            };
            player.tradeDeals.Add(deal);
            player.Log(f, "Dominion_Deal_LogSigned".Translate(deal.Summary), 1);
            EventBus.Raise(TalkEventDefOf.Dominion_TradeDealSigned, f, Faction.OfPlayer, detail: string.Join(", ", lines.Select(l => l.def.label).Distinct()));
            return deal;
        }

        // ------------------------------------------------------------------ deliveries

        public static void DailyTick()
        {
            if (Comp == null) return;
            int now = Find.TickManager.TicksGame;
            List<TradeAgreement> all = All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                TradeAgreement d = all[i];
                if (d?.faction == null || d.lines.Count == 0)
                {
                    all.RemoveAt(i);
                    continue;
                }
                if (!d.Open)
                {
                    if (d.endedTick >= 0 && now - d.endedTick > KeepEndedDays * GenDate.TicksPerDay) all.RemoveAt(i);
                    continue;
                }
                string broken = BrokenReason(d.faction);
                if (broken != null)
                {
                    End(d, DealStatus.Cancelled, broken);
                    continue;
                }
                if (d.status == DealStatus.Active && now >= d.nextTick) Deliver(d);
            }
        }

        /// <summary>What ends every deal with a faction: it is gone, at war with you, or an embargo stands.</summary>
        private static string BrokenReason(Faction f)
        {
            if (f.defeated) return "Dominion_Deal_End_Gone".Translate(f.Name);
            if (f.HostileTo(Faction.OfPlayer)) return "Dominion_Deal_End_War".Translate(f.Name);
            if (Comp.GetRelation(f, Faction.OfPlayer).embargo || Comp.GetRelation(Faction.OfPlayer, f).embargo) return "Dominion_Deal_End_Embargo".Translate(f.Name);
            return null;
        }

        /// <summary>
        /// Makes the next delivery: you hand over what you sell and pay (or are paid) the silver, and what you buy comes
        /// down by drop pod. If you cannot pay or deliver, the deal is suspended. If their stores fall short, they send
        /// what they have and the price drops to match.
        /// </summary>
        public static void Deliver(TradeAgreement d)
        {
            Faction f = d.faction;
            Map map = Comp.Player.capital?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return; // nowhere to deliver to: try again tomorrow
            int now = Find.TickManager.TicksGame;

            foreach (DealLine l in d.lines)
                if (!l.weBuy && CountGoods(l.def, l.stuff) < l.count)
                {
                    Missed(d, "Dominion_Deal_Missed_Goods".Translate(l.Label, f.Name));
                    return;
                }
            if (d.silver > 0 && DiplomacyHelpers.AvailableSilverFresh() < d.silver)
            {
                Missed(d, "Dominion_Deal_Missed_Silver".Translate(d.silver, f.Name));
                return;
            }

            // Their side: what they can send from their stores.
            var arriving = new List<(DealLine line, int count)>();
            float wanted = 0f, short_ = 0f;
            foreach (DealLine l in d.lines)
            {
                if (!l.weBuy) continue;
                int have = ED?.RemoveGoods(f, l.def, l.count) ?? -1;
                int n = have < 0 ? l.count : have;
                float unit = l.def.GetStatValueAbstract(StatDefOf.MarketValue, l.stuff);
                wanted += unit * l.count;
                short_ += unit * (l.count - n);
                if (n > 0) arriving.Add((l, n));
            }
            int silver = d.silver;
            if (short_ > 0f && silver > 0) silver = Mathf.RoundToInt(silver * (1f - short_ / Mathf.Max(1f, wanted)));

            // Our side: goods out of the colony into their stockpile, then the silver.
            var toThem = new List<ThingDefCount>();
            foreach (DealLine l in d.lines)
            {
                if (l.weBuy) continue;
                TakeGoods(l.def, l.stuff, l.count);
                toThem.Add(new ThingDefCount(l.def, l.count));
            }
            if (toThem.Count > 0 && !(ED?.ReceiveGoods(f, toThem) ?? false))
                EconomyEffects.Receive(f, Mathf.RoundToInt(toThem.Sum(g => g.ThingDef.BaseMarketValue * g.Count)));
            int moved = silver > 0 ? EconomyEffects.Transfer(Faction.OfPlayer, f, silver) : silver < 0 ? -EconomyEffects.Transfer(f, Faction.OfPlayer, -silver) : 0;
            foreach ((DealLine l, int n) in arriving) DropGoods(map, l.def, l.stuff, n);

            string got = string.Join(", ", arriving.Select(a => GenLabel.ThingLabel(a.line.def, a.line.stuff, a.count)));
            string text = "Dominion_Deal_Delivered".Translate(f.Name, got.NullOrEmpty() ? "-" : got,
                moved > 0 ? "Dominion_Deal_YouPay".Translate(moved).ToString() : moved < 0 ? "Dominion_Deal_TheyPay".Translate(-moved).ToString() : "-");
            if (short_ > 0f) text += " " + "Dominion_Deal_TheyShort".Translate(f.Name);
            Messages.Message(text, arriving.Count > 0 ? new LookTargets(DropCellFinder.TradeDropSpot(map), map) : LookTargets.Invalid,
                MessageTypeDefOf.PositiveEvent, historical: true);
            Comp.Player.Log(f, text, short_ > 0f ? 0 : 1);
            d.deliveriesDone++;
            // Good business: each delivery made on time warms them a little.
            Comp.GetRelation(f, Faction.OfPlayer).favour += 1f;
            d.deliveriesLeft--;
            if (d.deliveriesLeft <= 0) End(d, DealStatus.Completed, "Dominion_Deal_End_Done".Translate());
            else d.nextTick = now + d.everyDays * GenDate.TicksPerDay;
        }

        /// <summary>The player could not pay or deliver: goodwill suffers and the deal waits until resumed.</summary>
        private static void Missed(TradeAgreement d, string why)
        {
            d.missed++;
            d.status = DealStatus.Suspended;
            d.faction.TryAffectGoodwillWith(Faction.OfPlayer, MissedGoodwill, canSendMessage: false, canSendHostilityLetter: false);
            Comp.GetRelation(d.faction, Faction.OfPlayer).favour -= 4f;
            Comp.Player.Log(d.faction, why, -1);
            Find.LetterStack.ReceiveLetter("Dominion_Deal_MissedLabel".Translate(), why + "\n\n" + "Dominion_Deal_MissedText".Translate(d.faction.Name, -MissedGoodwill),
                LetterDefOf.NegativeEvent, null, d.faction);
        }

        /// <summary>Picks a suspended deal up again: the next delivery comes tomorrow.</summary>
        public static void Resume(TradeAgreement d)
        {
            if (d.status != DealStatus.Suspended) return;
            d.status = DealStatus.Active;
            d.nextTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
        }

        /// <summary>The player calls a deal off. Walking out before it has run its course costs a little goodwill.</summary>
        public static void Cancel(TradeAgreement d)
        {
            if (!d.Open) return;
            if (d.deliveriesLeft > 0 && !d.faction.defeated)
            {
                d.faction.TryAffectGoodwillWith(Faction.OfPlayer, CancelGoodwill, canSendMessage: false, canSendHostilityLetter: false);
                Comp.GetRelation(d.faction, Faction.OfPlayer).favour -= 2f;
            }
            End(d, DealStatus.Cancelled, "Dominion_Deal_End_Cancelled".Translate());
        }

        private static void End(TradeAgreement d, DealStatus status, string reason)
        {
            d.status = status;
            d.endReason = reason;
            d.endedTick = Find.TickManager.TicksGame;
            if (status != DealStatus.Cancelled) return;
            Comp.Player.Log(d.faction, "Dominion_Deal_LogEnded".Translate(d.Summary, reason), -1);
            EventBus.Raise(TalkEventDefOf.Dominion_TradeDealBroken, d.faction, Faction.OfPlayer, detail: reason);
        }

        /// <summary>God mode: makes the next delivery of an open deal now, whatever the schedule.</summary>
        public static void DevDeliverNow(TradeAgreement d)
        {
            if (!d.Open) return;
            d.status = DealStatus.Active;
            Deliver(d);
        }

        // ------------------------------------------------------------------ goods in the colony

        private static IEnumerable<Thing> ColonyStacks(ThingDef def, ThingDef stuff)
        {
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Thing t in map.listerThings.ThingsOfDef(def))
                    if (t.Spawned && t.Stuff == stuff && !t.IsForbidden(Faction.OfPlayer) && !t.Position.Fogged(map)) yield return t;
            }
        }

        /// <summary>How many of a good the colony has on its home maps, not forbidden.</summary>
        public static int CountGoods(ThingDef def, ThingDef stuff)
        {
            int n = 0;
            foreach (Thing t in ColonyStacks(def, stuff)) n += t.stackCount;
            return n;
        }

        private static void TakeGoods(ThingDef def, ThingDef stuff, int count)
        {
            int taken = 0;
            foreach (Thing t in ColonyStacks(def, stuff).ToList())
            {
                if (taken >= count) break;
                int n = Mathf.Min(count - taken, t.stackCount);
                t.SplitOff(n).Destroy();
                taken += n;
            }
        }

        private static void DropGoods(Map map, ThingDef def, ThingDef stuff, int count)
        {
            var things = new List<Thing>();
            while (count > 0)
            {
                Thing t = ThingMaker.MakeThing(def, stuff);
                t.stackCount = Mathf.Min(count, Mathf.Max(1, def.stackLimit));
                count -= t.stackCount;
                things.Add(t.TryMakeMinified());
            }
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things, 110, false, false, true, false, true, null);
        }
    }

    /// <summary>
    /// A faction's trade office, reached over the wire: a trader with the faction's own caravan stock, traded with in
    /// the vanilla trade window like an orbital trader. It is not one of the map's passing ships, so it never shows on
    /// the comms console or leaves on its own.
    /// </summary>
    public class FactionTradeOffice : TradeShip
    {
        public FactionTradeOffice(TraderKindDef def, Faction faction, Map map) : base(def, faction)
        {
            passingShipManager = map.passingShipManager;
            ticksUntilDeparture = int.MaxValue / 2;
            name = "Dominion_Deal_OfficeName".Translate(faction.Name);
            GenerateThings();
        }

        public override string FullTitle => name;
    }

    /// <summary>Makes the day's deliveries under every standing deal.</summary>
    public class TradeDealSystem : SimSystem
    {
        public override int Order => 97;

        public override void DailyGlobal() => TradeDeals.DailyTick();
    }

    [DefOf]
    public static class TalkEventDefOf
    {
        public static DominionEventDef Dominion_TradeDealSigned;
        public static DominionEventDef Dominion_TradeDealBroken;

        static TalkEventDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TalkEventDefOf));
        }
    }
}
