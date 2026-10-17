using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// After a trade with a faction's trade office, the offer to make it a standing deal: the same goods and silver,
    /// every 15, 30 or 60 days, for so many deliveries. They read it and press their seal, or stamp it REJECTED.
    /// </summary>
    public class Dialog_StandingDeal : PaperWindow
    {
        private readonly Faction faction;
        private readonly List<DealLine> lines;
        private readonly int silver;
        private int freqIndex = 1;
        private int deliveriesIndex = 1;
        private readonly SealCeremony ceremony = new SealCeremony();
        private string reply;
        private bool decided;
        private Vector2 scroll;
        private float viewHeight = 400f;

        public override Vector2 InitialSize => new Vector2(600f, 620f);

        public Dialog_StandingDeal(Faction faction, List<DealLine> lines, int silver)
        {
            this.faction = faction;
            this.lines = lines;
            this.silver = silver;
        }

        private int EveryDays => TradeDeals.Frequencies[freqIndex];
        private int DeliveriesCount => TradeDeals.Deliveries[deliveriesIndex];

        public override void PreClose()
        {
            base.PreClose();
            if (ceremony.Busy) Decide();
        }

        private SealCeremony.Stage Decide()
        {
            if (decided) return ceremony.stage;
            decided = true;
            int seed = Find.TickManager.TicksGame;
            if (Rand.Chance(TradeDeals.StandingChance(faction, TradeDeals.Value(lines), EveryDays)))
            {
                TradeDeals.Sign(faction, lines, silver, EveryDays, DeliveriesCount);
                reply = FactionVoice.Line(faction, VoiceLine.Accept, seed);
                return SealCeremony.Stage.Accepted;
            }
            WorldComponent_Dominion.Instance.Player.Log(faction, "Dominion_Deal_LogRefused".Translate(faction.Name), -1, byThem: true);
            reply = FactionVoice.Line(faction, VoiceLine.Refuse, seed);
            return SealCeremony.Stage.Refused;
        }

        protected override void DoPaper(Rect rect)
        {
            ceremony.Update(Decide);
            bool locked = ceremony.stage != SealCeremony.Stage.Draft;
            string sent = "Dominion_Paper_Locked".Translate();
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Standing_Title".Translate(faction.Name));
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), "“" + FactionVoice.Line(faction, VoiceLine.Bargain, faction.loadID) + "”", DominionTheme.InkFaded, GameFont.Tiny);
            y += 22f;

            const float FootHeight = 140f;
            Rect body = new Rect(rect.x, y, rect.width, rect.yMax - y - FootHeight);
            Rect view = new Rect(0f, 0f, body.width - 16f, Mathf.Max(body.height, viewHeight));
            Widgets.BeginScrollView(body, ref scroll, view);
            float vy = 0f, w = view.width, cw = Mathf.Min(w, 440f);
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Standing_Intro".Translate(faction.Name)) + 8f;

            // What changes hands each time.
            foreach (DealLine l in lines)
            {
                Widgets.DefIcon(new Rect(4f, vy, 22f, 22f), l.def, l.stuff);
                DominionTheme.InkLabel(new Rect(32f, vy, w - 32f, 22f), (l.weBuy ? "Dominion_Deal_YouGet" : "Dominion_Deal_YouGive").Translate(l.Label), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
                vy += 24f;
            }
            if (silver != 0)
            {
                DominionTheme.InkLabel(new Rect(32f, vy, w - 32f, 22f), (silver > 0 ? "Dominion_Standing_YouPay" : "Dominion_Standing_TheyPay").Translate(Mathf.Abs(silver)),
                    silver > 0 ? DominionTheme.InkRed : DominionTheme.InkGreen, GameFont.Small, TextAnchor.MiddleLeft);
                vy += 24f;
            }
            vy += 8f;

            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Deal_Frequency".Translate());
            vy += 18f;
            string[] freqs = TradeDeals.Frequencies.Select(f => "Dominion_Deal_Freq_Every".Translate(f).ToString()).ToArray();
            freqIndex = DominionPaper.Choice(new Rect(0f, vy, cw, 28f), freqs, freqIndex, null, locked ? freqs.Select(_ => sent).ToArray() : null);
            vy += 34f;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Deal_DeliveriesCount".Translate());
            vy += 18f;
            string[] counts = TradeDeals.Deliveries.Select(n => n.ToString()).ToArray();
            deliveriesIndex = DominionPaper.Choice(new Rect(0f, vy, cw, 28f), counts, deliveriesIndex, null, locked ? counts.Select(_ => sent).ToArray() : null);
            vy += 34f;

            var sb = new StringBuilder();
            float chance = TradeDeals.StandingChance(faction, TradeDeals.Value(lines), EveryDays, sb);
            DominionPaper.OddsMeter(new Rect(0f, vy, cw, 26f), chance, sb.ToString());
            vy += 32f;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Deal_Terms".Translate(-TradeDeals.MissedGoodwill, -TradeDeals.CancelGoodwill), DominionTheme.InkFaded, GameFont.Tiny) + 4f;
            if (reply != null)
            {
                vy += 6f;
                DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Letter_Reply".Translate());
                vy += 18f;
                vy += DominionPaper.Paragraph(0f, vy, w, "“" + reply + "”", ceremony.stage == SealCeremony.Stage.Refused ? DominionTheme.InkRed : DominionTheme.Ink) + 4f;
            }
            viewHeight = vy + 10f;
            Widgets.EndScrollView();

            Rect foot = new Rect(rect.x, rect.yMax - FootHeight, rect.width, FootHeight);
            Rect ours = new Rect(foot.x, foot.y + 40f, foot.width * 0.45f, 90f);
            Rect theirs = new Rect(foot.x + foot.width * 0.55f, foot.y + 40f, foot.width * 0.45f, 90f);
            DominionPaper.SignatureLine(ours, DominionPaper.PlayerSignatory(out string ourTitle), ourTitle);
            DominionPaper.SignatureLine(theirs, DominionPaper.Signatory(faction, out string theirTitle), theirTitle);
            ceremony.DrawOurSignature(ours);
            ceremony.DrawTheirAnswer(theirs, faction);
            if (ceremony.stage == SealCeremony.Stage.Draft)
            {
                if (DominionPaper.InkButton(new Rect(foot.x, foot.y, foot.width * 0.45f, 28f), "Dominion_Standing_Sign".Translate(), "Dominion_Standing_Sign_Desc".Translate(faction.Name),
                        TradeDeals.CannotDealReason(faction), false, DominionTheme.InkGreen))
                    ceremony.Sign();
                if (DominionPaper.InkButton(new Rect(foot.xMax - 200f, foot.y, 200f, 28f), "Dominion_Standing_JustOnce".Translate(), "Dominion_Standing_JustOnce_Desc".Translate()))
                    Close();
            }
            else if (ceremony.Done && DominionPaper.InkButton(new Rect(foot.xMax - 200f, foot.y, 200f, 28f), "Dominion_Paper_FileAway".Translate()))
                Close();
        }
    }

    /// <summary>The ledger of every standing deal: what is due when, and buttons to resume or cancel.</summary>
    public class Dialog_TradeLedger : PaperWindow
    {
        private Vector2 scroll;
        private float viewHeight = 400f;

        public override Vector2 InitialSize => new Vector2(720f, 620f);

        protected override void DoPaper(Rect rect)
        {
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Standing_LedgerTitle".Translate());
            Rect body = new Rect(rect.x, y, rect.width, rect.yMax - y);
            List<TradeAgreement> deals = TradeDeals.All.OrderBy(d => d.Open ? 0 : 1).ThenBy(d => d.nextTick).ToList();
            if (deals.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(body.x, body.y + 20f, body.width, 80f), "Dominion_Ledger_Empty".Translate(), DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
                return;
            }
            Rect view = new Rect(0f, 0f, body.width - 16f, Mathf.Max(body.height, viewHeight));
            Widgets.BeginScrollView(body, ref scroll, view);
            float vy = 0f;
            foreach (TradeAgreement d in deals)
                vy = DealRow(vy, view.width, d, true) + 4f;
            viewHeight = vy;
            Widgets.EndScrollView();
        }

        /// <summary>
        /// One deal as a ruled line: who, what, when next, and its state, with resume and cancel buttons while it is
        /// open. Shared with the Diplomacy telegram. Returns the y below.
        /// </summary>
        public static float DealRow(float y, float w, TradeAgreement d, bool showFaction)
        {
            const float H = 44f;
            Rect row = new Rect(0f, y, w, H);
            if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, DominionTheme.PaperShade);
            DealLine first = d.lines.FirstOrDefault();
            if (first != null) Widgets.DefIcon(new Rect(2f, y + 6f, 30f, 30f), first.def, first.stuff);
            Color ink = d.status == DealStatus.Suspended ? DominionTheme.InkRed : d.Open ? DominionTheme.Ink : DominionTheme.InkFaded;
            float bw = d.Open ? 170f : 0f;
            string who = showFaction ? d.faction.Name + ": " : "";
            DominionTheme.InkLabel(new Rect(38f, y + 2f, w - 40f - bw, 20f), (who + d.Summary).Truncate(w - 40f - bw), ink, GameFont.Tiny);
            int now = Find.TickManager.TicksGame;
            string state;
            switch (d.status)
            {
                case DealStatus.Active: state = "Dominion_Deal_State_Active".Translate(Mathf.Max(0, d.nextTick - now).ToStringTicksToPeriod(allowSeconds: false), d.deliveriesDone); break;
                case DealStatus.Suspended: state = "Dominion_Deal_State_Suspended".Translate(d.missed); break;
                case DealStatus.Completed: state = "Dominion_Deal_State_Completed".Translate(d.deliveriesDone); break;
                default: state = "Dominion_Deal_State_Cancelled".Translate(d.endReason ?? ""); break;
            }
            DominionTheme.InkLabel(new Rect(38f, y + 21f, w - 40f - bw, 20f), state.Truncate(w - 40f - bw), DominionTheme.InkFaded, GameFont.Tiny);
            TooltipHandler.TipRegion(new Rect(0f, y, w - bw, H), who + d.Summary + "\n" + state);
            if (d.Open)
            {
                if (DominionPaper.InkButton(new Rect(w - 84f, y + 8f, 82f, 26f), "Dominion_Deal_Cancel".Translate(), "Dominion_Deal_Cancel_Desc".Translate(-TradeDeals.CancelGoodwill),
                        null, false, DominionTheme.InkRed))
                    TradeDeals.Cancel(d);
                if (d.status == DealStatus.Suspended
                    && DominionPaper.InkButton(new Rect(w - 170f, y + 8f, 82f, 26f), "Dominion_Deal_Resume".Translate(), "Dominion_Deal_Resume_Desc".Translate(),
                        TradeDeals.CannotDealReason(d.faction), false, DominionTheme.InkGreen))
                    TradeDeals.Resume(d);
            }
            GUI.color = DominionPaper.EdgeFaint;
            Widgets.DrawLineHorizontal(4f, y + H, w - 8f);
            GUI.color = Color.white;
            return y + H + 2f;
        }
    }

    /// <summary>
    /// Notes what was traded with a faction's trade office, so the player can offer to repeat it as a standing deal.
    /// The counts are read before the trade goes through, because the trade window resets them afterwards.
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class Patch_TradeDeal_TryExecute_StandingDeal
    {
        public static void Prefix(TradeDeal __instance, out (Faction faction, List<DealLine> lines, int silver) __state)
        {
            __state = default;
            try
            {
                if (!(TradeSession.trader is FactionTradeOffice office) || TradeSession.giftMode) return;
                var lines = new List<DealLine>();
                float pay = 0f;
                foreach (Tradeable t in __instance.AllTradeables)
                {
                    if (t == null || t.IsCurrency || t.CountToTransfer == 0 || !t.HasAnyThing || t.AnyThing is Pawn) continue;
                    TradeAction action = t.ActionToDo;
                    int n = Mathf.Abs(t.CountToTransfer);
                    bool buy = action == TradeAction.PlayerBuys;
                    pay += (buy ? 1f : -1f) * t.GetPriceFor(action) * n;
                    ThingDef def = t.AnyThing.GetInnerIfMinified().def;
                    DealLine same = lines.Find(l => l.def == def && l.stuff == t.StuffDef && l.weBuy == buy);
                    if (same != null) same.count += n;
                    else lines.Add(new DealLine { def = def, stuff = t.StuffDef, count = n, weBuy = buy });
                }
                if (lines.Count > 0) __state = (office.Faction, lines, Mathf.RoundToInt(pay));
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("Reading a trade for a standing deal failed: " + e.Message, 0x7D03);
            }
        }

        public static void Postfix(bool __result, ref bool actuallyTraded, (Faction faction, List<DealLine> lines, int silver) __state)
        {
            if (!__result || !actuallyTraded || __state.faction == null || __state.lines == null) return;
            Find.WindowStack.Add(new Dialog_StandingDeal(__state.faction, __state.lines, __state.silver));
        }
    }
}
