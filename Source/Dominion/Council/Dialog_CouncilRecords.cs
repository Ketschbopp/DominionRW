using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A decree's compliance ledger, as a sheet from the council's register: who complied, who is in breach, who has
    /// not acted yet, and every breach with the penalty it drew.
    /// </summary>
    public class Dialog_CouncilLedger : PaperWindow
    {
        private readonly Decree decree;
        private Vector2 scroll;
        private float height = 400f;

        public override Vector2 InitialSize => new Vector2(620f, 640f);

        public Dialog_CouncilLedger(Decree decree)
        {
            this.decree = decree;
            doCloseX = true;
            closeOnClickedOutside = true;
        }

        protected override void DoPaper(Rect rect)
        {
            Decree d = decree;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Ledger_Title".Translate(d.Summary));
            string dates = "Dominion_Ledger_Passed".Translate(DominionPaper.Date(d.passedTick)).ToString()
                           + (d.expiresTick >= 0 ? "  ·  " + "Dominion_Dip_Expires".Translate((d.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)).ToString() : "");
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 18f), dates, DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
            y += 22f;

            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y - 40f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, height));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float vy = 0f, w = view.width;
            if (d.penalty)
            {
                vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Ledger_Penalty".Translate(), DominionTheme.InkRed, GameFont.Tiny) + 6f;
            }
            if (!d.def.ifPassed.NullOrEmpty())
            {
                string a = d.targets.Count > 0 ? d.targets[0].Name : "", b = d.targets.Count > 1 ? d.targets[1].Name : "";
                vy += DominionPaper.Paragraph(0f, vy, w, string.Format(d.def.ifPassed, a, b), DominionTheme.Ink, GameFont.Tiny) + 8f;
            }

            List<Faction> members = CouncilUtility.Members().ToList();
            List<Faction> pending = members.Where(m => !d.complied.Contains(m) && !d.violators.Contains(m) && !d.targets.Contains(m)).ToList();
            vy = Column(vy, w, "Dominion_Ledger_Complied".Translate(d.complied.Count), d.complied, DominionTheme.InkGreen);
            vy = Column(vy, w, "Dominion_Ledger_Violators".Translate(d.violators.Count), d.violators, DominionTheme.InkRed);
            vy = Column(vy, w, "Dominion_Ledger_Pending".Translate(pending.Count), pending, DominionTheme.InkFaded);

            vy += 6f;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Ledger_Entries".Translate());
            vy += 18f;
            if (d.ledger.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(8f, vy, w, 18f), "Dominion_Ledger_None".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                vy += 20f;
            }
            foreach (LedgerEntry e in d.ledger)
            {
                string line = e.fine > 0
                    ? "Dominion_Ledger_EntryFine".Translate(DominionPaper.Date(e.tick), e.faction.Name, CouncilSystem.PenaltyLabel(e.penalty), e.fine)
                    : "Dominion_Ledger_Entry".Translate(DominionPaper.Date(e.tick), e.faction.Name, CouncilSystem.PenaltyLabel(e.penalty));
                DominionPaper.DrawSeal(new Rect(0f, vy + 1f, 18f, 18f), e.faction);
                DominionTheme.InkLabel(new Rect(24f, vy, w - 24f, 20f), line, e.penalty >= Penalty.Sanctions ? DominionTheme.InkRed : DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                vy += 22f;
            }
            vy += 8f;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Ledger_Escalation".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
            height = vy + 10f;
            Widgets.EndScrollView();

            if (DominionPaper.InkButton(new Rect(rect.center.x - 70f, rect.yMax - 32f, 140f, 30f), "Dominion_Ledger_Close".Translate()))
                Close();
        }

        private static float Column(float y, float w, string title, List<Faction> factions, Color ink)
        {
            DominionPaper.Caption(new Rect(0f, y, w, 16f), title);
            y += 18f;
            if (factions.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(8f, y, w, 18f), "Dominion_None".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                return y + 22f;
            }
            float x = 0f;
            foreach (Faction f in factions)
            {
                Text.Font = GameFont.Tiny;
                float tw = Text.CalcSize(f.Name).x + 30f;
                Text.Font = GameFont.Small;
                if (x + tw > w)
                {
                    x = 0f;
                    y += 22f;
                }
                DominionPaper.DrawSeal(new Rect(x, y + 1f, 18f, 18f), f);
                DominionTheme.InkLabel(new Rect(x + 22f, y, tw - 22f, 20f), f.Name, ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                x += tw + 6f;
            }
            return y + 28f;
        }
    }

    /// <summary>The council's record of past sessions: every motion, how each member voted, and how the decrees turned out.</summary>
    public class Dialog_CouncilRecord : PaperWindow
    {
        private Vector2 scroll;
        private float height = 600f;

        public override Vector2 InitialSize => new Vector2(760f, 700f);

        public Dialog_CouncilRecord()
        {
            doCloseX = true;
            closeOnClickedOutside = true;
        }

        protected override void DoPaper(Rect rect)
        {
            CouncilState c = WorldComponent_Dominion.Instance.Council;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Record_Title".Translate());
            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y - 40f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, height));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float vy = 0f, w = view.width;
            if (c.records.Count == 0)
                DominionTheme.InkLabel(new Rect(0f, vy, w, 40f), "Dominion_Record_None".Translate(), DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
            // Newest first.
            for (int i = c.records.Count - 1; i >= 0; i--)
            {
                SessionRecord r = c.records[i];
                DominionPaper.Caption(new Rect(0f, vy, w, 16f),
                    "Dominion_Record_Session".Translate(r.number, DominionPaper.Date(r.tick), r.host?.Name ?? "?", r.attended.Count));
                vy += 18f;
                if (r.motions.Count == 0)
                {
                    DominionTheme.InkLabel(new Rect(8f, vy, w, 18f), "Dominion_Council_NoMotions".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                    vy += 22f;
                }
                foreach (MotionRecord m in r.motions) vy = DrawMotion(vy, w, m, c);
                GUI.color = DominionPaper.EdgeFaint;
                Widgets.DrawLineHorizontal(0f, vy + 3f, w);
                GUI.color = Color.white;
                vy += 10f;
            }
            height = vy + 10f;
            Widgets.EndScrollView();
            if (DominionPaper.InkButton(new Rect(rect.center.x - 70f, rect.yMax - 32f, 140f, 30f), "Dominion_Ledger_Close".Translate()))
                Close();
        }

        private static float DrawMotion(float y, float w, MotionRecord m, CouncilState c)
        {
            Rect head = new Rect(8f, y, w - 8f, 22f);
            DominionTheme.DrawInk(new Rect(head.x, head.y + 2f, 18f, 18f), m.passed ? DominionPaper.Star : DominionPaper.StarOutline, m.passed ? DominionTheme.InkGreen : DominionTheme.InkRed);
            string title = m.summary + (m.vetoed ? " " + "Dominion_Record_Vetoed".Translate() : "") + "  ·  " + "Dominion_Record_Tally".Translate(m.yes.ToString("F1"), m.no.ToString("F1"));
            DominionTheme.InkLabel(new Rect(head.x + 24f, head.y, head.width - 120f, head.height), title.Truncate(head.width - 120f), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
            DominionTheme.InkLabel(new Rect(head.xMax - 92f, head.y, 92f, head.height),
                (m.passed ? "Dominion_Council_Passed" : "Dominion_Council_Rejected").Translate(), m.passed ? DominionTheme.InkGreen : DominionTheme.InkRed, GameFont.Tiny, TextAnchor.MiddleRight);
            if (m.def != null) TooltipHandler.TipRegion(head, m.def.description);
            y += 22f;
            string Names(List<Faction> fs) => fs.Count == 0 ? "-" : string.Join(", ", fs.Select(f => f.IsPlayer ? f.Name + " ★" : f.Name));
            foreach ((string key, List<Faction> list, Color ink) in new[]
                     {
                         ("Dominion_Record_Ayes", m.ayes, DominionTheme.InkGreen), ("Dominion_Record_Nays", m.nays, DominionTheme.InkRed),
                         ("Dominion_Record_Abstained", m.abstained, DominionTheme.InkFaded),
                     })
            {
                if (list.Count == 0) continue;
                y += DominionPaper.Paragraph(32f, y, w - 32f, key.Translate(Names(list)), ink, GameFont.Tiny) + 1f;
            }
            string outcome = m.outcome;
            if (outcome.NullOrEmpty() && m.passed && m.decreeId >= 0 && c.decrees.Any(d => d.id == m.decreeId)) outcome = "Dominion_Record_InForce".Translate();
            if (!outcome.NullOrEmpty()) y += DominionPaper.Paragraph(32f, y, w - 32f, outcome, DominionTheme.InkAmber, GameFont.Tiny) + 1f;
            return y + 6f;
        }
    }
}
