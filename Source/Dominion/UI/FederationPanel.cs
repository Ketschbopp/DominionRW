using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The Federation tab: the unification tracker, charter status, and where every major faction stands.</summary>
    public class FederationPanel
    {
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
            var l = new Listing_Standard();
            l.Begin(view);

            DominionTheme.Header(l, "Dominion_Federation_Title".Translate());
            if (!DominionMod.Settings.endingEnabled)
            {
                GUI.color = Color.gray;
                l.Label("Dominion_Federation_EndingOff".Translate());
                GUI.color = Color.white;
            }

            // Steps toward the peaceful path.
            l.Label(Check(FederationUtility.ResearchDone) + "Dominion_Federation_StepResearch".Translate(DominionFederationDefOf.Dominion_PlanetaryFederation.label));
            l.Label(Check(FederationUtility.CharterInForce) + (DominionMod.Settings.councilEnabled
                ? "Dominion_Federation_StepCharter".Translate()
                : "Dominion_Federation_StepCharterNoCouncil".Translate()));
            l.Gap(6f);

            Rect bar = l.GetRect(26f);
            DominionUI.Bar(bar, "Dominion_Federation_Progress".Translate(), progress.total > 0 ? Mathf.Clamp01((float)progress.unified / Mathf.Max(1, progress.required)) : 0f,
                progress.unified + " / " + progress.required);
            l.Label("Dominion_Federation_Breakdown".Translate(progress.federation, progress.subjugated, progress.conquered, progress.total));
            l.Label("Dominion_Federation_Rules".Translate());

            if (st.proclaimed)
                l.Label("Dominion_Federation_Proclaimed".Translate((Find.TickManager.TicksGame - st.proclaimedTick).ToStringTicksToPeriod(allowSeconds: false)));
            else if (DominionMod.Settings.endingEnabled && progress.Complete && l.ButtonText("Dominion_Federation_Proclaim".Translate()))
                UnificationSystem.OfferProclamation(progress);

            l.GapLine();
            foreach ((Faction f, UnifyPath status) in rows)
            {
                Rect r = l.GetRect(24f);
                Widgets.Label(r.LeftPart(0.45f), f.Name.Truncate(r.width * 0.45f));
                string text = ("Dominion_UnifyStatus_" + status).Translate();
                if (status == UnifyPath.None)
                {
                    float op = comp.GetRelation(f, Faction.OfPlayer).opinion;
                    text += "  " + "Dominion_Federation_Opinion".Translate(op.ToString("F0")).ToString().Colorize(DominionUI.OpinionColor(op));
                }
                Widgets.Label(r.RightPart(0.55f), text);
                Widgets.DrawHighlightIfMouseover(r);
                TooltipHandler.TipRegion(r, ("Dominion_UnifyStatus_" + status + "_Desc").Translate());
            }
            foreach (KeyValuePair<int, UnifyPath> kv in st.fallen)
            {
                if (kv.Value == UnifyPath.None) continue;
                Rect r = l.GetRect(24f);
                GUI.color = Color.gray;
                Widgets.Label(r.LeftPart(0.45f), st.fallenNames.TryGetValue(kv.Key, out string n) ? n : "?");
                Widgets.Label(r.RightPart(0.55f), ("Dominion_UnifyStatus_" + kv.Value).Translate());
                GUI.color = Color.white;
            }

            height = l.CurHeight + 20f;
            l.End();
            Widgets.EndScrollView();
        }

        private static string Check(bool done) => done ? "[x] " : "[  ] ";

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
}
