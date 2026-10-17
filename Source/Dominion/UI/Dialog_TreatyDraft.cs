using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The signing desk: a treaty drafted on parchment. Pick its term and what you offer, watch the odds, sign with the
    /// quill, and the other side presses its seal, stamps it REJECTED, or writes in a counter-offer.
    /// </summary>
    public class Dialog_TreatyDraft : PaperWindow
    {
        private readonly TreatyDraft draft;
        private readonly SealCeremony ceremony = new SealCeremony();
        private Vector2 scroll;
        private float viewHeight = 400f;

        public override Vector2 InitialSize => new Vector2(760f, 700f);

        public Dialog_TreatyDraft(TreatyDef def, Faction faction, Treaty renewing = null)
        {
            draft = new TreatyDraft(def, faction, renewing);
        }

        /// <summary>Choose a partner's treaty from a menu, then open the desk.</summary>
        public static void ProposeMenu(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var options = new List<FloatMenuOption>();
            foreach (TreatyDef t in DefDatabase<TreatyDef>.AllDefsListForReading)
            {
                if (t.imposed) continue;
                TreatyDef def = t;
                string reason = comp.Treaties.CannotSignReason(t, Faction.OfPlayer, f);
                if (reason == null && t.requiredResearch != null && !t.requiredResearch.IsFinished)
                    reason = "Dominion_Action_NeedsResearch".Translate(t.requiredResearch.label);
                string label = t.LabelCap + "  (" + "Dominion_Paper_OddsShort".Translate(DiplomacyHelpers.TreatyAcceptance(t, f).ToStringPercent()) + ")";
                options.Add(reason != null
                    ? new FloatMenuOption(label + " — " + reason, null)
                    : new FloatMenuOption(label, () => Find.WindowStack.Add(new Dialog_TreatyDraft(def, f)),
                        mouseoverGuiAction: r => TooltipHandler.TipRegion(r, def.description)));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_Treaty_ChooseKind".Translate(f.Name)));
        }

        /// <summary>Choose a faction, then a treaty.</summary>
        public static void ProposeAnyMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (Faction f in WorldComponent_Dominion.Instance.Cache.Simulated)
            {
                if (f.HostileTo(Faction.OfPlayer)) continue;
                Faction captured = f;
                options.Add(new FloatMenuOption(f.Name, () => ProposeMenu(captured), f.def.FactionIcon, f.Color));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_Treaty_ChoosePartner".Translate()));
        }

        public override void PreClose()
        {
            base.PreClose();
            // Closing during the reading would lose the answer; settle it now.
            if (ceremony.stage == SealCeremony.Stage.Signing || ceremony.stage == SealCeremony.Stage.Reading)
            {
                // No one is left to answer a counter-offer: it counts as refused.
                if (draft.Decide() == SealCeremony.Stage.Countered) draft.DeclineCounter();
            }
            else if (ceremony.stage == SealCeremony.Stage.Countered) draft.DeclineCounter();
        }

        protected override void DoPaper(Rect rect)
        {
            ceremony.Update(draft.Decide);
            Faction f = draft.faction;
            TreatyDef def = draft.def;
            float y = rect.y;

            // Heading
            string title = draft.Renewal ? "Dominion_Paper_RenewTitle".Translate(def.LabelCap) : (string)def.LabelCap;
            y = DominionPaper.Title(new Rect(rect.x, y, rect.width, 40f), title);
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), "Dominion_Paper_Between".Translate(Faction.OfPlayer.Name, f.Name),
                DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
            y += 24f;

            // Body (scrolls if the window is small); the signatures stay at the bottom.
            const float SignHeight = 150f;
            Rect bodyOut = new Rect(rect.x, y, rect.width, rect.yMax - y - SignHeight);
            Rect view = new Rect(0f, 0f, bodyOut.width - 16f, viewHeight);
            Widgets.BeginScrollView(bodyOut, ref scroll, view);
            float vy = 0f;
            float w = view.width;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Paper_Preamble".Translate(Faction.OfPlayer.Name, f.Name), DominionTheme.InkFaded) + 8f;

            // Article I: what it does.
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article1".Translate());
            vy += 16f;
            foreach (string line in TreatyDraft.Provisions(def, Faction.OfPlayer, f))
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, "· " + line) + 2f;
            vy += 6f;

            bool locked = ceremony.stage != SealCeremony.Stage.Draft;
            string sent = "Dominion_Paper_Locked".Translate();

            // Article II: term.
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article2".Translate());
            vy += 18f;
            if (def.Expires)
            {
                string[] labels = { TreatyDraft.TermLabel(def, 0), TreatyDraft.TermLabel(def, 1), TreatyDraft.TermLabel(def, 2) };
                string[] tips = { "Dominion_Term_0_Desc".Translate(), "Dominion_Term_1_Desc".Translate(), "Dominion_Term_2_Desc".Translate() };
                string[] off = locked ? new[] { sent, sent, sent } : null;
                draft.term = DominionPaper.Choice(new Rect(10f, vy, Mathf.Min(w - 10f, 420f), 28f), labels, draft.term, tips, off);
                vy += 32f;
            }
            else
            {
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, "Dominion_Paper_OpenEnded".Translate()) + 6f;
            }

            // Article III: considerations.
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article3".Translate());
            vy += 18f;
            int silver = DiplomacyHelpers.AvailableSilver();
            DominionTheme.InkLabel(new Rect(10f, vy, 140f, 28f), "Dominion_Paper_Sweetener".Translate(), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
            var sweet = new string[TreatyDraft.SweetenerOptions.Length];
            var sweetOff = new string[sweet.Length];
            for (int i = 0; i < sweet.Length; i++)
            {
                int amount = TreatyDraft.SweetenerOptions[i];
                sweet[i] = amount == 0 ? "Dominion_None".Translate().ToString() : "Dominion_Paper_Silver".Translate(amount).ToString();
                sweetOff[i] = locked ? sent : amount > silver ? "Dominion_Action_NeedsSilver".Translate(amount).ToString() : null;
            }
            int sweetIndex = System.Array.IndexOf(TreatyDraft.SweetenerOptions, draft.sweetener);
            sweetIndex = DominionPaper.Choice(new Rect(150f, vy, Mathf.Min(w - 150f, 420f), 28f), sweet, Mathf.Max(0, sweetIndex),
                new[] { "Dominion_Paper_Sweetener_Desc".Translate().ToString(), null, null, null }, sweetOff);
            draft.sweetener = TreatyDraft.SweetenerOptions[sweetIndex];
            vy += 32f;
            DominionTheme.InkLabel(new Rect(10f, vy, 140f, 28f), "Dominion_Paper_Press".Translate(), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
            var press = new string[TreatyDraft.PressOptions.Length];
            for (int i = 0; i < press.Length; i++)
                press[i] = TreatyDraft.PressOptions[i] == 0 ? "Dominion_None".Translate().ToString() : "Dominion_Paper_Influence".Translate(TreatyDraft.PressOptions[i]).ToString();
            int pressIndex = System.Array.IndexOf(TreatyDraft.PressOptions, draft.press);
            pressIndex = DominionPaper.Choice(new Rect(150f, vy, Mathf.Min(w - 150f, 315f), 28f), press, Mathf.Max(0, pressIndex),
                new[] { "Dominion_Paper_Press_Desc".Translate().ToString(), null, null }, locked ? new[] { sent, sent, sent } : null);
            draft.press = TreatyDraft.PressOptions[pressIndex];
            vy += 36f;

            // The odds and the bill.
            DominionPaper.OddsMeter(new Rect(10f, vy, Mathf.Min(w - 10f, 420f), 26f), draft.Chance(), draft.Breakdown());
            vy += 30f;
            string bill = "Dominion_Paper_Bill".Translate(draft.InfluenceCost.ToString("F0"), draft.SilverOffered);
            vy += DominionPaper.Paragraph(10f, vy, w - 10f, bill, DominionTheme.InkFaded, GameFont.Tiny) + 4f;

            if (ceremony.stage == SealCeremony.Stage.Countered)
            {
                Rect box = new Rect(10f, vy + 4f, w - 20f, 64f);
                Widgets.DrawBoxSolid(box, DominionTheme.PaperShade);
                GUI.color = DominionTheme.InkAmber;
                Widgets.DrawBox(box);
                GUI.color = Color.white;
                DominionPaper.Paragraph(box.x + 8f, box.y + 6f, box.width - 16f, "Dominion_Paper_CounterText".Translate(f.Name, draft.counterSilver, draft.SilverOffered));
                if (DominionPaper.InkButton(new Rect(box.x + 8f, box.yMax - 28f, 180f, 24f), "Dominion_Paper_AcceptCounter".Translate(draft.SilverOffered), null,
                        DiplomacyHelpers.AvailableSilver() < draft.SilverOffered ? "Dominion_Action_NeedsSilver".Translate(draft.SilverOffered).ToString() : null, false, DominionTheme.InkGreen))
                {
                    if (draft.AcceptCounter()) ceremony.Set(SealCeremony.Stage.Accepted);
                }
                if (DominionPaper.InkButton(new Rect(box.x + 196f, box.yMax - 28f, 160f, 24f), "Dominion_Paper_DeclineCounter".Translate(), null, null, false, DominionTheme.InkRed))
                {
                    draft.DeclineCounter();
                    ceremony.Set(SealCeremony.Stage.Refused);
                }
                vy += 72f;
            }
            viewHeight = vy + 6f;
            Widgets.EndScrollView();

            DrawSignatures(new Rect(rect.x, rect.yMax - SignHeight, rect.width, SignHeight));
        }

        private void DrawSignatures(Rect rect)
        {
            Faction f = draft.faction;
            float half = (rect.width - 40f) / 2f;
            Rect ours = new Rect(rect.x, rect.y + 34f, half, 90f);
            Rect theirs = new Rect(rect.x + half + 40f, rect.y + 34f, half, 90f);
            string ourName = DominionPaper.PlayerSignatory(out string ourTitle);
            string theirName = DominionPaper.Signatory(f, out string theirTitle);
            DominionPaper.SignatureLine(ours, ourName, ourTitle);
            DominionPaper.SignatureLine(theirs, theirName, theirTitle);
            ceremony.DrawOurSignature(ours);
            ceremony.DrawTheirAnswer(theirs, f);

            Rect button = new Rect(rect.x, rect.y, half, 28f);
            switch (ceremony.stage)
            {
                case SealCeremony.Stage.Draft:
                    string why = draft.CannotSendReason();
                    if (DominionPaper.InkButton(button, "Dominion_Paper_SignAndSend".Translate(), "Dominion_Paper_SignAndSend_Desc".Translate(f.Name), why, false, DominionTheme.InkRed))
                        ceremony.Sign();
                    DominionTheme.DrawInk(new Rect(button.xMax - 26f, button.y + 2f, 24f, 24f), DominionPaper.Quill, why == null ? DominionTheme.InkRed : DominionTheme.InkFaded);
                    break;
                case SealCeremony.Stage.Accepted:
                case SealCeremony.Stage.Refused:
                    if (DominionPaper.InkButton(new Rect(theirs.x, rect.y, half, 28f), "Dominion_Paper_FileAway".Translate()))
                        Close();
                    break;
            }
            if (ceremony.stage == SealCeremony.Stage.Accepted && ceremony.T > 0.5f)
                DominionPaper.SlamStamp(new Rect(rect.center.x - 90f, rect.y - 8f, 180f, 70f), DominionPaper.StampSigned, DominionTheme.InkGreen, -6f, ceremony.T - 0.5f);
        }
    }

    /// <summary>
    /// A treaty in force, as the signed parchment: articles, term, both signatures and the partner's seal. Treaties with
    /// the player can be renewed or amended (back to the desk) or torn up.
    /// </summary>
    public class Dialog_TreatyDocument : PaperWindow
    {
        private readonly Treaty treaty;
        private Vector2 scroll;
        private float docHeight = 600f;

        public override Vector2 InitialSize => new Vector2(700f, 640f);

        public Dialog_TreatyDocument(Treaty treaty)
        {
            this.treaty = treaty;
            closeOnClickedOutside = true;
        }

        protected override void DoPaper(Rect rect)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Treaty t = treaty;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), t.def.LabelCap);
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), "Dominion_Paper_Between".Translate(t.a.Name, t.b.Name),
                DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
            y += 26f;
            bool inForce = comp.Treaties.All.Contains(t);

            const float SignHeight = 150f;
            Rect body = new Rect(rect.x, y, rect.width, rect.yMax - y - SignHeight);
            Rect view = new Rect(0f, 0f, body.width - 16f, docHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float vy = 0f, w = view.width;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article1".Translate());
            vy += 16f;
            foreach (string line in TreatyDraft.Provisions(t.def, t.a, t.b))
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, "· " + line) + 2f;
            vy += 8f;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article2".Translate());
            vy += 16f;
            string term = "Dominion_Paper_SignedOn".Translate(DominionPaper.Date(t.signedTick));
            if (t.expiresTick >= 0)
                term += "\n" + "Dominion_Paper_ExpiresOn".Translate(DominionPaper.Date(t.expiresTick),
                    Mathf.Max(0, t.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false));
            else term += "\n" + "Dominion_Paper_OpenEnded".Translate();
            if (t.renewals > 0) term += "\n" + "Dominion_Paper_Renewals".Translate(t.renewals);
            vy += DominionPaper.Paragraph(10f, vy, w - 10f, term) + 8f;
            if (!t.def.description.NullOrEmpty())
                vy += DominionPaper.Paragraph(0f, vy, w, t.def.description, DominionTheme.InkFaded, GameFont.Tiny) + 6f;

            if (t.Involves(Faction.OfPlayer) && inForce)
            {
                Faction other = t.Other(Faction.OfPlayer);
                Rect renew = new Rect(0f, vy + 4f, 200f, 28f);
                if (t.def.Expires && !t.def.imposed)
                {
                    string label = t.DaysLeft <= 15 ? "Dominion_Treaty_Renew".Translate() : "Dominion_Treaty_Amend".Translate();
                    if (DominionPaper.InkButton(renew, label, "Dominion_Treaty_Renew_Desc".Translate(other.Name), null, false, DominionTheme.InkGreen))
                    {
                        Close();
                        Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, other, t));
                    }
                }
                Rect tear = new Rect(renew.xMax + 10f, renew.y, 200f, 28f);
                if (DominionPaper.InkButton(tear, "Dominion_Treaty_TearUp".Translate(), "Dominion_Treaty_TearUp_Desc".Translate() + "\n\n" + TreatyDraft.TearUpCost(t, Faction.OfPlayer),
                        null, false, DominionTheme.InkRed))
                    ConfirmTearUp(t, () => Close());
                vy += 40f;
            }
            docHeight = vy + 10f;
            Widgets.EndScrollView();

            Rect sign = new Rect(rect.x, rect.yMax - SignHeight, rect.width, SignHeight);
            float half = (sign.width - 40f) / 2f;
            Rect left = new Rect(sign.x, sign.y + 34f, half, 90f), right = new Rect(sign.x + half + 40f, sign.y + 34f, half, 90f);
            DrawParty(left, t.a, t.id * 3);
            DrawParty(right, t.b, t.id * 3 + 1);
            Texture2D stamp = inForce ? DominionPaper.StampRatified : DominionPaper.StampVoid;
            DominionTheme.DrawStamp(new Rect(sign.center.x - 80f, sign.y - 6f, 160f, 60f), stamp, inForce ? DominionTheme.InkGreen : DominionTheme.InkRed, -5f);
        }

        private static void DrawParty(Rect line, Faction f, int seed)
        {
            string title;
            string name = f.IsPlayer ? DominionPaper.PlayerSignatory(out title) : DominionPaper.Signatory(f, out title);
            DominionPaper.SignatureLine(line, name, title);
            DominionPaper.DrawSignature(new Rect(line.x + 4f, line.yMax - 30f - line.height * 0.55f, line.width - 8f, line.height * 0.55f), seed, 1f,
                f.IsPlayer ? new Color(0.12f, 0.12f, 0.3f, 0.9f) : new Color(0.25f, 0.1f, 0.08f, 0.9f));
            DominionPaper.DrawSeal(new Rect(line.xMax - 46f, line.y - 22f, 60f, 60f), f);
        }

        /// <summary>Asks before tearing up a treaty with the player, listing what it costs.</summary>
        public static void ConfirmTearUp(Treaty t, System.Action after = null)
        {
            Faction other = t.Other(Faction.OfPlayer);
            string text = "Dominion_Dip_CancelConfirm".Translate(t.Label, other.Name) + "\n\n" + TreatyDraft.TearUpCost(t, Faction.OfPlayer);
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () =>
            {
                WorldComponent_Dominion.Instance.Treaties.Cancel(t, Faction.OfPlayer);
                WorldComponent_Dominion.Instance.Player.Log(other, "Dominion_Treaty_TornUp".Translate(t.Label), -1);
                after?.Invoke();
            }, destructive: true));
        }
    }
}
