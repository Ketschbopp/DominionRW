using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// An envoy's letter: gifts, compliments, insults, denunciations and demands go out as a sealed letter. The tone
    /// changes the odds and how much it counts: warm letters make friends, threatening ones squeeze the weak.
    /// </summary>
    public class Dialog_EnvoyLetter : PaperWindow
    {
        private readonly DiplomacyActionDef def;
        private readonly Faction faction;
        private LetterTone tone = LetterTone.Formal;
        private int giftIndex = 1;
        private readonly SealCeremony ceremony = new SealCeremony();
        private string reply;
        private int outcome;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(620f, 640f);

        public Dialog_EnvoyLetter(DiplomacyActionDef def, Faction faction)
        {
            this.def = def;
            this.faction = faction;
            if (def.Worker.IsDemand) tone = LetterTone.Threatening;
            int silver = DiplomacyHelpers.AvailableSilver();
            while (giftIndex > 0 && DiplomacyAction_Gift.Amounts[giftIndex] > silver) giftIndex--;
        }

        private bool IsGift => def.Worker is DiplomacyAction_Gift;

        private int GiftAmount => DiplomacyAction_Gift.Amounts[giftIndex];

        public override void PreClose()
        {
            base.PreClose();
            if (ceremony.Busy) Send();
        }

        protected override void DoPaper(Rect rect)
        {
            ceremony.Update(Send);
            DiplomacyActionWorker worker = def.Worker;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), def.LabelCap);
            string leader = DominionPaper.Signatory(faction, out string leaderTitle);
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), DominionPaper.Date(Find.TickManager.TicksGame), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperRight);
            y += 20f;

            const float FootHeight = 150f;
            Rect body = new Rect(rect.x, y, rect.width, rect.yMax - y - FootHeight);
            Rect view = new Rect(0f, 0f, body.width - 16f, 520f);
            Widgets.BeginScrollView(body, ref scroll, view);
            float vy = 0f, w = view.width;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Letter_Salutation".Translate(leader, leaderTitle)) + 8f;
            string text = ("Dominion_Letter_" + def.defName + "_" + tone).Translate(faction.Name, leader, GiftAmount, Faction.OfPlayer.Name);
            vy += DominionPaper.Paragraph(0f, vy, w, text) + 8f;
            vy += DominionPaper.Paragraph(0f, vy, w, ("Dominion_Letter_Closing_" + tone).Translate()) + 14f;

            bool locked = ceremony.stage != SealCeremony.Stage.Draft;
            string sent = "Dominion_Paper_Locked".Translate();
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Letter_Tone".Translate());
            vy += 18f;
            string[] tones = { "Dominion_Tone_Warm".Translate(), "Dominion_Tone_Formal".Translate(), "Dominion_Tone_Threatening".Translate() };
            string[] tips = { "Dominion_Tone_Warm_Desc".Translate(), "Dominion_Tone_Formal_Desc".Translate(), "Dominion_Tone_Threatening_Desc".Translate() };
            tone = (LetterTone)DominionPaper.Choice(new Rect(0f, vy, Mathf.Min(w, 420f), 28f), tones, (int)tone, tips, locked ? new[] { sent, sent, sent } : null);
            vy += 34f;

            if (IsGift)
            {
                DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Letter_Enclosed".Translate());
                vy += 18f;
                int silver = DiplomacyHelpers.AvailableSilver();
                var labels = new string[DiplomacyAction_Gift.Amounts.Length];
                var off = new string[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = "Dominion_Paper_Silver".Translate(DiplomacyAction_Gift.Amounts[i]);
                    off[i] = locked ? sent : DiplomacyAction_Gift.Amounts[i] > silver ? "Dominion_Action_NeedsSilver".Translate(DiplomacyAction_Gift.Amounts[i]).ToString() : null;
                }
                giftIndex = DominionPaper.Choice(new Rect(0f, vy, Mathf.Min(w, 420f), 28f), labels, giftIndex, null, off);
                vy += 34f;
            }

            float? chance = worker.ChanceWith(faction, tone);
            if (chance.HasValue)
            {
                LetterTone old = worker.tone;
                worker.tone = tone;
                string breakdown = worker.ChanceBreakdown(faction);
                worker.tone = old;
                DominionPaper.OddsMeter(new Rect(0f, vy, Mathf.Min(w, 420f), 26f), chance.Value, breakdown);
                vy += 32f;
            }
            string outcomeKey = "Dominion_Outcome_" + def.defName;
            if (outcomeKey.CanTranslate())
                vy += DominionPaper.Paragraph(0f, vy, w, outcomeKey.Translate(), DominionTheme.InkFaded, GameFont.Tiny) + 4f;
            vy += DominionPaper.Paragraph(0f, vy, w, ("Dominion_Tone_" + tone + "_Effect").Translate(), DominionTheme.InkFaded, GameFont.Tiny) + 4f;

            if (reply != null)
            {
                vy += 8f;
                DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Letter_Reply".Translate());
                vy += 18f;
                vy += DominionPaper.Paragraph(0f, vy, w, reply, outcome < 0 ? DominionTheme.InkRed : DominionTheme.Ink) + 4f;
            }
            Widgets.EndScrollView();

            // Foot: our signature and seal, and the send button.
            Rect foot = new Rect(rect.x, rect.yMax - FootHeight, rect.width, FootHeight);
            Rect line = new Rect(foot.x, foot.y + 40f, foot.width * 0.5f, 90f);
            string ourName = DominionPaper.PlayerSignatory(out string ourTitle);
            DominionPaper.SignatureLine(line, ourName, ourTitle);
            ceremony.DrawOurSignature(line);
            if (ceremony.stage != SealCeremony.Stage.Draft && ceremony.stage != SealCeremony.Stage.Signing)
                DominionPaper.DropSeal(new Rect(line.xMax + 10f, line.y, 60f, 60f), Faction.OfPlayer, ceremony.stage == SealCeremony.Stage.Reading ? ceremony.T : 1f);

            Rect button = new Rect(foot.x, foot.y, foot.width * 0.5f, 28f);
            if (ceremony.stage == SealCeremony.Stage.Draft)
            {
                string why = DiplomacyActions.CannotUseReason(def, faction);
                if (why == null && IsGift && DiplomacyHelpers.AvailableSilver() < GiftAmount) why = "Dominion_Action_NeedsSilver".Translate(GiftAmount);
                if (DominionPaper.InkButton(button, "Dominion_Letter_SealAndSend".Translate(), "Dominion_Letter_SealAndSend_Desc".Translate(faction.Name), why, false, DominionTheme.InkRed))
                    ceremony.Sign();
            }
            else if (ceremony.Done)
            {
                Rect stampRect = new Rect(foot.xMax - 230f, foot.y + 40f, 220f, 70f);
                Texture2D stamp = outcome > 0 ? DominionPaper.StampSuccess : outcome < 0 && !Hostile ? DominionPaper.StampFailed : DominionPaper.StampDelivered;
                Color c = outcome > 0 ? DominionTheme.InkGreen : outcome < 0 && !Hostile ? DominionTheme.InkRed : DominionTheme.InkAmber;
                DominionPaper.SlamStamp(stampRect, stamp, c, -6f, ceremony.T);
                if (DominionPaper.InkButton(new Rect(foot.xMax - 200f, foot.y, 200f, 28f), "Dominion_Paper_FileAway".Translate())) Close();
            }
        }

        /// <summary>Insults and denunciations are meant to sting; a "bad" outcome is not a failure for them.</summary>
        private bool Hostile => def.Worker is DiplomacyAction_Insult || def.Worker is DiplomacyAction_Denounce;

        private SealCeremony.Stage Send()
        {
            if (reply != null) return SealCeremony.Stage.Accepted;
            DiplomacyActionWorker worker = def.Worker;
            List<DiplomacyLogEntry> log = WorldComponent_Dominion.Instance.Player.diplomacyLog;
            int before = log.Count;
            DiplomacyLogEntry last = before > 0 ? log[before - 1] : null;
            LetterTone old = worker.tone;
            worker.tone = tone;
            try
            {
                if (worker is DiplomacyAction_Gift gift) gift.Give(faction, GiftAmount);
                else worker.Execute(faction);
            }
            finally
            {
                worker.tone = old;
            }
            if (tone == LetterTone.Threatening)
                WorldComponent_Dominion.Instance.GetRelation(faction, Faction.OfPlayer).grievance += 3f;

            DiplomacyLogEntry entry = log.Count > 0 ? log[log.Count - 1] : null;
            if (entry != null && entry != last && entry.factionId == faction.loadID)
            {
                // Their answer in their own voice, then what came of it.
                outcome = entry.outcome;
                reply = "“" + FactionVoice.Answer(faction, outcome, Hostile, entry.tick) + "”\n\n" + entry.text;
            }
            else
            {
                reply = "Dominion_Letter_Delivered".Translate(faction.Name);
                outcome = 0;
                WorldComponent_Dominion.Instance.Player.Log(faction, "Dominion_Letter_LogLine".Translate(def.label, ("Dominion_Tone_" + tone).Translate()), 0);
            }
            return outcome < 0 && !Hostile ? SealCeremony.Stage.Refused : SealCeremony.Stage.Accepted;
        }
    }
}
