using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Federation tab: the road to unification as a map of steps (each with a button to where it is done), the
    /// charter with the members' seals, members with their dues, contribution and loyalty, and where every major
    /// faction stands.
    /// </summary>
    public class FederationPanel
    {
        private const float Line = 26f;

        private Vector2 scroll;
        private float height = 600f;

        // The tracker walks every treaty and faction: refreshed twice a second of real time, also while paused.
        private float nextRefresh = -1f;
        private FederationUtility.Progress progress;
        private readonly List<(Faction faction, UnifyPath status)> rows = new List<(Faction, UnifyPath)>();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            Refresh();
            UnificationState st = comp.Unification;
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            DominionTheme.Header(l, "Dominion_Federation_Title".Translate());
            if (!DominionMod.Settings.endingEnabled)
            {
                GUI.color = Color.gray;
                l.Label("Dominion_Federation_EndingOff".Translate());
                GUI.color = Color.white;
            }

            DrawRoadmap(l.GetRect(124f), st);
            l.Gap(6f);
            Rect bar = l.GetRect(26f);
            DominionUI.Bar(bar, "Dominion_Federation_Progress".Translate(), progress.required > 0 ? Mathf.Clamp01((float)progress.unified / progress.required) : 0f,
                progress.unified + " / " + progress.required);
            TooltipHandler.TipRegion(bar, "Dominion_Federation_Rules".Translate());
            l.Label("Dominion_Federation_Breakdown".Translate(progress.federation, progress.subjugated, progress.conquered, progress.total));
            if (st.proclaimed)
                l.Label("Dominion_Federation_Proclaimed".Translate((Find.TickManager.TicksGame - st.proclaimedTick).ToStringTicksToPeriod(allowSeconds: false)));

            Rect buttons = l.GetRect(30f);
            if (DominionUIState.ButtonWithTip(buttons.LeftPart(0.32f), "Dominion_Federation_ReadCharter".Translate(), "Dominion_Federation_ReadCharter_Desc".Translate()))
                Find.WindowStack.Add(new Dialog_FederationCharter());
            string inviteWhy = !FederationUtility.ResearchDone ? "Dominion_Action_NeedsResearch".Translate(DominionFederationDefOf.Dominion_PlanetaryFederation.label).ToString()
                : !FederationUtility.CharterInForce ? "Dominion_Federation_NoCharter".Translate().ToString() : null;
            if (DominionUIState.ButtonWithTip(new Rect(buttons.x + buttons.width * 0.34f, buttons.y, buttons.width * 0.32f, buttons.height), "Dominion_Federation_Invite".Translate(),
                    "Dominion_Federation_Invite_Desc".Translate() + (inviteWhy != null ? "\n\n" + inviteWhy.Colorize(ColorLibrary.RedReadable) : ""), inviteWhy == null))
                InviteMenu();

            // Members
            DominionTheme.Header(l, "Dominion_Federation_Members".Translate(progress.federation));
            List<Treaty> members = comp.Treaties.All.Where(t => t.def.federation && t.Involves(Faction.OfPlayer)).ToList();
            if (members.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_Federation_NoMembers".Translate());
                GUI.color = Color.white;
            }
            foreach (Treaty t in members) DrawMember(l, t, comp);

            // Everyone else
            DominionTheme.Header(l, "Dominion_Federation_Standing".Translate());
            foreach ((Faction f, UnifyPath status) in rows)
            {
                if (status == UnifyPath.Federation) continue;
                Rect r = l.GetRect(24f);
                Widgets.DrawHighlightIfMouseover(r);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(r.x, r.y + 2f, 20f, 20f), f);
                Widgets.Label(new Rect(r.x + 24f, r.y, r.width * 0.42f - 24f, 24f), f.Name.Truncate(r.width * 0.42f - 24f));
                string text = ("Dominion_UnifyStatus_" + status).Translate();
                if (status == UnifyPath.None)
                {
                    float op = comp.GetRelation(f, Faction.OfPlayer).opinion;
                    text += "  " + "Dominion_Federation_Opinion".Translate(op.ToString("F0")).ToString().Colorize(DominionUI.OpinionColor(op));
                    TreatyDef def = DominionTreatyDefOf.Dominion_FederationMembership;
                    if (inviteWhy == null)
                        text += "  " + "Dominion_Federation_JoinOdds".Translate(DiplomacyHelpers.TreatyAcceptance(def, f).ToStringPercent());
                }
                Widgets.Label(new Rect(r.x + r.width * 0.42f, r.y, r.width * 0.58f, 24f), text);
                TooltipHandler.TipRegion(r, ("Dominion_UnifyStatus_" + status + "_Desc").Translate());
            }
            foreach (KeyValuePair<int, UnifyPath> kv in st.fallen)
            {
                if (kv.Value == UnifyPath.None) continue;
                Rect r = l.GetRect(24f);
                GUI.color = Color.gray;
                Widgets.Label(r.LeftPart(0.42f), "  " + (st.fallenNames.TryGetValue(kv.Key, out string n) ? n : "?"));
                Widgets.Label(r.RightPart(0.58f), ("Dominion_UnifyStatus_" + kv.Value).Translate());
                GUI.color = Color.white;
            }

            height = l.CurHeight + 20f;
            l.End();
            Widgets.EndScrollView();
        }

        /// <summary>The steps to unification as a row of seals joined by a line; each says what is missing and jumps there.</summary>
        private void DrawRoadmap(Rect rect, UnificationState st)
        {
            DominionPaper.DrawCard(rect);
            ResearchProjectDef research = DominionFederationDefOf.Dominion_PlanetaryFederation;
            var steps = new List<(string label, string detail, bool done, string button, System.Action act)>
            {
                ("Dominion_Road_Research".Translate(), research.IsFinished ? "Dominion_Road_Done".Translate().ToString() : research.ProgressPercent.ToStringPercent(),
                    research.IsFinished, "Dominion_Road_ToResearch".Translate(), () => Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research)),
                ("Dominion_Road_Charter".Translate(), FederationUtility.CharterInForce ? "Dominion_Road_Done".Translate().ToString() : "Dominion_Road_CharterTodo".Translate().ToString(),
                    FederationUtility.CharterInForce, "Dominion_Road_ToCouncil".Translate(), () => DominionUIState.Show(DominionTab.Council)),
                ("Dominion_Road_Members".Translate(), progress.unified + " / " + progress.required, progress.Complete,
                    "Dominion_Road_ToDiplomacy".Translate(), () => DominionUIState.Show(DominionTab.Diplomacy)),
                ("Dominion_Road_Proclaim".Translate(), st.proclaimed ? "Dominion_Road_Done".Translate().ToString() : progress.Complete ? "Dominion_Road_Ready".Translate().ToString() : "-",
                    st.proclaimed, "Dominion_Federation_Proclaim".Translate(), null),
            };
            float stepW = (rect.width - 20f) / steps.Count;
            float cy = rect.y + 34f;
            for (int i = 0; i < steps.Count - 1; i++)
            {
                Color lc = steps[i].done ? DominionTheme.InkGreen : DominionTheme.InkFaded;
                Widgets.DrawLine(new Vector2(rect.x + 10f + stepW * (i + 0.5f) + 22f, cy), new Vector2(rect.x + 10f + stepW * (i + 1.5f) - 22f, cy), lc, 3f);
            }
            for (int i = 0; i < steps.Count; i++)
            {
                (string label, string detail, bool done, string button, System.Action act) = steps[i];
                float cx = rect.x + 10f + stepW * (i + 0.5f);
                Rect seal = new Rect(cx - 22f, cy - 22f, 44f, 44f);
                bool current = !done && (i == 0 || steps[i - 1].done);
                DominionTheme.DrawInk(seal, DominionPaper.WaxSeal, done ? DominionTheme.InkGreen : current ? DominionTheme.InkAmber : new Color(0.5f, 0.45f, 0.4f, 0.6f));
                DominionTheme.InkLabel(seal, done ? "✓" : (i + 1).ToString(), Color.white, GameFont.Medium, TextAnchor.MiddleCenter);
                DominionTheme.InkLabel(new Rect(cx - stepW / 2f, cy + 24f, stepW, 20f), label, DominionTheme.Ink, GameFont.Small, TextAnchor.UpperCenter);
                DominionTheme.InkLabel(new Rect(cx - stepW / 2f, cy + 42f, stepW, 18f), detail, DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
                Rect b = new Rect(cx - Mathf.Min(70f, stepW / 2f - 4f), rect.yMax - 30f, Mathf.Min(140f, stepW - 8f), 24f);
                if (i == steps.Count - 1)
                {
                    string why = st.proclaimed ? "Dominion_Road_Done".Translate().ToString() : !DominionMod.Settings.endingEnabled ? "Dominion_Federation_EndingOff".Translate().ToString()
                        : !progress.Complete ? "Dominion_Road_NotYet".Translate(progress.unified, progress.required).ToString() : null;
                    if (DominionPaper.InkButton(b, button, "Dominion_Tip_Proclaim".Translate(), why, false, DominionTheme.InkRed))
                        UnificationSystem.OfferProclamation(progress);
                }
                else if (!done && DominionPaper.InkButton(b, button, ("Dominion_Road_Step" + i + "_Desc").Translate()))
                    act();
            }
        }

        private static void DrawMember(Listing_Standard l, Treaty t, WorldComponent_Dominion comp)
        {
            Faction f = t.Other(Faction.OfPlayer);
            PlayerState player = comp.Player;
            Rect row = l.GetRect(48f);
            Widgets.DrawLightHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);
            DominionPaper.DrawSeal(new Rect(row.x + 2f, row.y + 2f, 44f, 44f), f);
            Widgets.Label(new Rect(row.x + 52f, row.y + 2f, row.width * 0.32f, 24f), f.Name.Truncate(row.width * 0.32f));
            float op = comp.GetRelation(f, Faction.OfPlayer).opinion;
            int paid = player.duesPaid.TryGetValue(f.loadID, out int p) ? p : 0;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(row.x + 52f, row.y + 24f, row.width * 0.4f, 20f),
                "Dominion_Federation_MemberLine".Translate((Find.TickManager.TicksGame - t.signedTick).ToStringTicksToPeriod(allowSeconds: false), paid));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect loyalty = new Rect(row.x + row.width * 0.44f, row.y + 12f, row.width * 0.18f, 22f);
            Widgets.FillableBar(loyalty, Mathf.InverseLerp(-100f, 100f, op), DominionPaper.OddsFill(Mathf.InverseLerp(-100f, 100f, op)));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(loyalty, "Dominion_Federation_Loyalty".Translate(op.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(loyalty, "Dominion_Federation_Loyalty_Desc".Translate());

            int dues = player.DuesOf(f);
            Rect duesRect = new Rect(row.x + row.width * 0.64f, row.y + 10f, row.width * 0.18f, 26f);
            if (DominionUIState.ButtonWithTip(duesRect, ("Dominion_Dues_" + dues).Translate(), "Dominion_Dues_Desc".Translate(FederationDuesSystem.IntervalDays)))
            {
                var options = new List<FloatMenuOption>();
                for (int level = 0; level <= 2; level++)
                {
                    int lv = level;
                    options.Add(new FloatMenuOption(("Dominion_Dues_" + level).Translate() + " — " + ("Dominion_Dues_" + level + "_Desc").Translate(FederationDuesSystem.Amount(level)),
                        () => player.federationDues[f.loadID] = lv));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            Rect expel = new Rect(row.x + row.width * 0.84f, row.y + 10f, row.width * 0.16f - 4f, 26f);
            if (DominionUIState.ButtonWithTip(expel, "Dominion_Federation_Expel".Translate(), "Dominion_Federation_Expel_Desc".Translate(f.Name) + "\n\n" + TreatyDraft.TearUpCost(t, Faction.OfPlayer)))
                Dialog_TreatyDocument.ConfirmTearUp(t);
            l.Gap(4f);
        }

        private static void InviteMenu()
        {
            var options = new List<FloatMenuOption>();
            TreatyDef def = DominionTreatyDefOf.Dominion_FederationMembership;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in FederationUtility.Majors())
            {
                if (FederationUtility.IsMember(f) || f.HostileTo(Faction.OfPlayer)) continue;
                string why = comp.Treaties.CannotSignReason(def, Faction.OfPlayer, f);
                Faction captured = f;
                string label = f.Name + "  (" + "Dominion_Paper_OddsShort".Translate(DiplomacyHelpers.TreatyAcceptance(def, f).ToStringPercent()) + ")";
                options.Add(why == null ? new FloatMenuOption(label, () => Find.WindowStack.Add(new Dialog_TreatyDraft(def, captured)), f.def.FactionIcon, f.Color)
                    : new FloatMenuOption(label + " — " + why, null));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Refresh()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextRefresh) return;
            nextRefresh = now + 0.5f;
            progress = FederationUtility.Compute();
            rows.Clear();
            foreach (Faction f in FederationUtility.Majors())
                rows.Add((f, FederationUtility.StatusOf(f)));
            rows.SortBy(r => r.status == UnifyPath.None ? 1 : 0, r => r.faction.Name);
        }
    }

    /// <summary>The Federation's charter as an illuminated parchment, signed and sealed by every member as it joined.</summary>
    public class Dialog_FederationCharter : PaperWindow
    {
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(760f, 760f);

        public Dialog_FederationCharter()
        {
            closeOnClickedOutside = true;
        }

        protected override void DoPaper(Rect rect)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Charter_Title".Translate(), DominionTheme.InkRed);
            Rect view = new Rect(0f, 0f, rect.width - 16f, 1200f);
            Widgets.BeginScrollView(new Rect(rect.x, y, rect.width, rect.yMax - y), ref scroll, view);
            float vy = 0f, w = view.width;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Charter_Preamble".Translate(Faction.OfPlayer.Name)) + 10f;
            for (int i = 1; i <= 5; i++)
            {
                DominionPaper.Caption(new Rect(0f, vy, w, 16f), ("Dominion_Charter_Article" + i + "_Title").Translate());
                vy += 16f;
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, ("Dominion_Charter_Article" + i).Translate()) + 8f;
            }
            if (!FederationUtility.CharterInForce)
            {
                DominionTheme.DrawStamp(new Rect(w / 2f - 120f, vy, 240f, 70f), DominionTheme.StampRumor, DominionTheme.InkAmber, -6f);
                vy += 74f;
                vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Charter_NotInForce".Translate(), DominionTheme.InkFaded) + 8f;
            }
            DominionTheme.DrawRule(new Rect(w * 0.2f, vy, w * 0.6f, 14f));
            vy += 20f;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Charter_Signatories".Translate());
            vy += 22f;

            // Signatures: the founder, then each member in the order it joined, then the majors still outside.
            var signed = new List<(Faction f, int tick)> { (Faction.OfPlayer, -1) };
            signed.AddRange(comp.Treaties.All.Where(t => t.def.federation && t.Involves(Faction.OfPlayer)).OrderBy(t => t.signedTick)
                .Select(t => (t.Other(Faction.OfPlayer), t.signedTick)));
            var outside = FederationUtility.Majors().Where(f => !FederationUtility.IsMember(f)).ToList();
            int perRow = 3;
            float cw = w / perRow;
            int idx = 0;
            foreach ((Faction f, int tick) in signed)
            {
                Rect cell = new Rect(idx % perRow * cw, vy + idx / perRow * 110f, cw - 10f, 104f);
                string title;
                string name = f.IsPlayer ? DominionPaper.PlayerSignatory(out title) : DominionPaper.Signatory(f, out title);
                DominionPaper.SignatureLine(new Rect(cell.x, cell.y, cell.width, 90f), name, tick >= 0 ? DominionPaper.Date(tick) : "Dominion_Charter_Founder".Translate().ToString());
                DominionPaper.DrawSignature(new Rect(cell.x + 4f, cell.y + 20f, cell.width - 50f, 40f), f.loadID, 1f, new Color(0.15f, 0.12f, 0.3f, 0.9f));
                DominionPaper.DrawSeal(new Rect(cell.xMax - 48f, cell.y + 6f, 46f, 46f), f);
                TooltipHandler.TipRegion(cell, f.Name);
                idx++;
            }
            foreach (Faction f in outside)
            {
                Rect cell = new Rect(idx % perRow * cw, vy + idx / perRow * 110f, cw - 10f, 104f);
                DominionPaper.SignatureLine(new Rect(cell.x, cell.y, cell.width, 90f), f.Name, "Dominion_Charter_Awaiting".Translate());
                DominionPaper.DrawSeal(new Rect(cell.xMax - 48f, cell.y + 6f, 46f, 46f), f, 0.18f);
                idx++;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>Members pay the dues the player set, every <see cref="IntervalDays"/> days; heavy dues wear their loyalty down.</summary>
    public class FederationDuesSystem : SimSystem
    {
        public const int IntervalDays = 15;

        public override int Order => 98;

        public static int Amount(int level) => level == 1 ? 100 : level == 2 ? 250 : 0;

        public override void DailyGlobal()
        {
            if (GenDate.DaysPassed % IntervalDays != 0) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            PlayerState player = comp.Player;
            foreach (Treaty t in comp.Treaties.All.Where(t => t.def.federation && t.Involves(Faction.OfPlayer)).ToList())
            {
                Faction f = t.Other(Faction.OfPlayer);
                int level = player.DuesOf(f);
                if (level == 0 || !DominionScheduler.IsSimulated(f)) continue;
                int moved = EconomyEffects.Transfer(f, Faction.OfPlayer, Amount(level));
                player.duesPaid[f.loadID] = (player.duesPaid.TryGetValue(f.loadID, out int p) ? p : 0) + moved;
                RelationState r = comp.GetRelation(f, Faction.OfPlayer);
                r.favour -= level == 2 ? 3f : 1f;
                if (moved > 0) player.Log(f, "Dominion_Dues_Paid".Translate(f.Name, moved), 1, byThem: true);
                // Heavy dues on a member that already resents you: it may walk out.
                if (level == 2 && r.opinion < -30f && Rand.Chance(0.2f))
                {
                    comp.Treaties.Cancel(t, f);
                    Find.LetterStack.ReceiveLetter("Dominion_Dues_LeftLabel".Translate(f.Name), "Dominion_Dues_LeftText".Translate(f.Name), LetterDefOf.NegativeEvent, null, f);
                }
            }
        }
    }
}
