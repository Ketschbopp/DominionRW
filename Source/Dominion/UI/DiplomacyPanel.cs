using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The player's diplomacy: their standing, a faction list, and everything they can do with the selected faction.</summary>
    public class DiplomacyPanel
    {
        private const float Line = 24f;
        private const float TopBarHeight = 34f;

        private Faction selected;
        private Vector2 listScroll;
        private Vector2 detailScroll;
        private float detailHeight = 600f;
        private readonly List<Treaty> treaties = new List<Treaty>();

        private static List<DiplomacyActionDef> actions;

        private static List<DiplomacyActionDef> Actions =>
            actions ??= DefDatabase<DiplomacyActionDef>.AllDefsListForReading.OrderBy(a => a.tier).ThenBy(a => a.order).ToList();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            DrawTopBar(new Rect(rect.x, rect.y, rect.width, TopBarHeight), comp);
            Rect body = new Rect(rect.x, rect.y + TopBarHeight + 6f, rect.width, rect.height - TopBarHeight - 6f);
            List<Faction> factions = comp.Cache.Simulated;
            if (selected != null && !factions.Contains(selected)) selected = null;
            if (selected == null && factions.Count > 0) selected = factions[0];

            DrawFactionList(body.LeftPart(0.28f), factions);
            Rect detail = body.RightPart(0.72f);
            detail.xMin += 8f;
            Widgets.DrawMenuSection(detail);
            if (selected != null)
                DrawDetail(detail.ContractedBy(10f), selected, comp);
        }

        private static void DrawTopBar(Rect rect, WorldComponent_Dominion comp)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f, 4f);
            float w = inner.width / 4f;

            Pawn hos = LeadershipUtility.HeadOfState();
            Rect r = new Rect(inner.x, inner.y, w, inner.height);
            Widgets.Label(r, "Dominion_Dip_HeadOfState".Translate(hos != null ? hos.LabelShortCap : "Dominion_None".Translate().ToString()).ToString().Truncate(r.width));
            TooltipHandler.TipRegion(r, hos != null ? (string)"Dominion_Dip_HeadOfState_Desc".Translate() : LeadershipUtility.NoHeadOfStateReason());

            r = new Rect(inner.x + w, inner.y + 2f, w - 10f, inner.height - 4f);
            float inf = comp.Player.influence;
            Widgets.FillableBar(r, inf / PlayerState.MaxInfluence);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, "Dominion_Dip_Influence".Translate(inf.ToString("F0"), PlayerState.MaxInfluence.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(r, "Dominion_Dip_Influence_Desc".Translate());

            Building_LeadershipDesk desk = LeadershipUtility.BestDesk();
            r = new Rect(inner.x + w * 2f, inner.y, w, inner.height);
            Widgets.Label(r, "Dominion_Dip_Desk".Translate(desk != null ? ("Dominion_DeskTier_" + desk.Tier).Translate().ToString() : "Dominion_None".Translate().ToString()).ToString().Truncate(r.width));
            r = new Rect(inner.x + w * 3f, inner.y, w, inner.height);
            Widgets.Label(r, "Dominion_Dip_Comms".Translate(LeadershipUtility.HasComms() ? "Dominion_Yes".Translate() : "Dominion_No".Translate()).ToString().Truncate(r.width));
        }

        private void DrawFactionList(Rect rect, List<Faction> factions)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, factions.Count * Line);
            Widgets.BeginScrollView(inner, ref listScroll, view);
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                Rect row = new Rect(0f, i * Line, view.width, Line);
                if (f == selected) Widgets.DrawHighlightSelected(row);
                Widgets.DrawHighlightIfMouseover(row);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 2f, row.y + 2f, 20f, 20f), f);
                GUI.color = f.RelationKindWith(Faction.OfPlayer).GetColor();
                Widgets.Label(new Rect(row.x + 26f, row.y, row.width - 26f, Line), f.Name.Truncate(row.width - 26f));
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row))
                {
                    selected = f;
                    detailScroll = Vector2.zero;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawDetail(Rect rect, Faction f, WorldComponent_Dominion comp)
        {
            Rect view = new Rect(0f, 0f, rect.width - 16f, detailHeight);
            Widgets.BeginScrollView(rect, ref detailScroll, view);
            var l = new Listing_Standard();
            l.Begin(view);

            Text.Font = GameFont.Medium;
            l.Label(f.Name);
            Text.Font = GameFont.Small;
            Faction player = Faction.OfPlayer;
            FactionRelationKind kind = f.RelationKindWith(player);
            RelationState view_ = comp.GetRelation(f, player);
            RelationState mine = comp.GetRelation(player, f);
            string opinion = IntelUtility.Knows(f, IntelUtility.RestrictedIntel)
                ? DominionUI.Colored(view_.opinion.ToString("F0"), DominionUI.OpinionColor(view_.opinion))
                : IntelUtility.Unknown(IntelUtility.RestrictedIntel);
            l.Label("Dominion_Dip_Standing".Translate(kind.GetLabelCap().Colorize(kind.GetColor()), f.PlayerGoodwill.ToStringWithSign(), opinion));

            // Treaties with the player
            l.GapLine();
            l.Label("Dominion_Dip_TreatiesWithYou".Translate());
            comp.Treaties.Between(player, f, treaties);
            if (treaties.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_None".Translate());
                GUI.color = Color.white;
            }
            foreach (Treaty t in treaties)
            {
                Rect row = l.GetRect(Line);
                string expires = t.expiresTick >= 0
                    ? "Dominion_Dip_Expires".Translate((t.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)).ToString()
                    : "Dominion_Dip_NoExpiry".Translate().ToString();
                Rect labelRect = row.LeftPart(0.75f);
                Widgets.Label(labelRect, ("  " + t.Label + "  (" + expires + ")").Truncate(labelRect.width));
                TooltipHandler.TipRegion(row, t.def.description);
                if (Widgets.ButtonText(row.RightPart(0.22f), "Dominion_Dip_Cancel".Translate()))
                {
                    Treaty captured = t;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_Dip_CancelConfirm".Translate(t.Label, f.Name),
                        () => comp.Treaties.Cancel(captured, player)));
                }
            }

            // Borders, embargoes, prices
            l.GapLine();
            l.Label("Dominion_Dip_Borders".Translate(DiplomacyAction_Borders.BorderLabel(mine.border), DiplomacyAction_Borders.BorderLabel(view_.border)));
            l.Label("Dominion_Dip_Embargo".Translate(mine.embargo ? "Dominion_Yes".Translate() : "Dominion_No".Translate(),
                view_.embargo ? "Dominion_Yes".Translate() : "Dominion_No".Translate()));
            DominionPricing.Compute(f, out float buy, out float sell);
            if (!Mathf.Approximately(buy, 1f) || !Mathf.Approximately(sell, 1f))
                l.Label("Dominion_Dip_Prices".Translate(buy.ToStringPercent(), sell.ToStringPercent()));

            // Actions
            l.GapLine();
            l.Label("Dominion_Dip_Actions".Translate());
            ActionTier? lastTier = null;
            foreach (DiplomacyActionDef def in Actions)
            {
                DiplomacyActionWorker worker = def.Worker;
                if (!worker.Visible(f)) continue;
                if (lastTier != def.tier)
                {
                    lastTier = def.tier;
                    GUI.color = Color.gray;
                    l.Label(("Dominion_ActionTier_" + def.tier).Translate());
                    GUI.color = Color.white;
                }
                DrawAction(l, def, worker, f);
            }

            detailHeight = Mathf.Max(l.CurHeight + 10f, rect.height);
            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawAction(Listing_Standard l, DiplomacyActionDef def, DiplomacyActionWorker worker, Faction f)
        {
            Rect row = l.GetRect(30f);
            string reason = DiplomacyActions.CannotUseReason(def, f);
            string label = def.LabelCap.ToString() + (def.influenceCost > 0f ? "  (" + "Dominion_Dip_Cost".Translate(def.influenceCost.ToString("F0")).ToString() + ")" : "");
            Rect button = row.LeftPart(0.62f);
            if (reason != null)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(button, label, active: false);
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(button, label))
                worker.Execute(f);

            float? chance = reason == null ? worker.AcceptanceChance(f) : null;
            Rect info = row.RightPart(0.36f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (reason != null)
            {
                GUI.color = new Color(1f, 0.6f, 0.4f);
                Widgets.Label(info, reason.Truncate(info.width));
                GUI.color = Color.white;
            }
            else if (chance.HasValue)
                Widgets.Label(info, "Dominion_Dip_Chance".Translate(chance.Value.ToStringPercent()));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(row, def.description + (reason != null ? "\n\n" + reason : ""));
        }
    }

    /// <summary>Every treaty the player knows about.</summary>
    public class TreatiesPanel
    {
        private const float Line = 26f;
        private Vector2 scroll;
        private readonly List<Treaty> known = new List<Treaty>();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            known.Clear();
            foreach (Treaty t in comp.Treaties.All)
                if (IsKnown(t)) known.Add(t);

            float[] widths = { 0.24f, 0.28f, 0.28f, 0.2f };
            string[] headers = { "Dominion_Col_Treaty".Translate(), "Dominion_Col_Party".Translate(), "Dominion_Col_Party".Translate(), "Dominion_Col_Expires".Translate() };
            GUI.color = Color.gray;
            float x = rect.x;
            for (int c = 0; c < headers.Length; c++)
            {
                Widgets.Label(new Rect(x, rect.y, (rect.width - 16f) * widths[c], Line), headers[c]);
                x += (rect.width - 16f) * widths[c];
            }
            GUI.color = Color.white;

            Rect outRect = new Rect(rect.x, rect.y + Line, rect.width, rect.height - Line);
            if (known.Count == 0)
            {
                Widgets.Label(outRect, "Dominion_Treaties_None".Translate());
                return;
            }
            Rect view = new Rect(0f, 0f, outRect.width - 16f, known.Count * Line);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < known.Count; i++)
            {
                Treaty t = known[i];
                Rect row = new Rect(0f, i * Line, view.width, Line);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                x = row.x;
                Widgets.Label(new Rect(x, row.y, row.width * widths[0], Line), t.Label);
                x += row.width * widths[0];
                Party(new Rect(x, row.y, row.width * widths[1], Line), t.a);
                x += row.width * widths[1];
                Party(new Rect(x, row.y, row.width * widths[2], Line), t.b);
                x += row.width * widths[2];
                Widgets.Label(new Rect(x, row.y, row.width * widths[3], Line),
                    t.expiresTick >= 0 ? (t.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false) : "Dominion_Dip_NoExpiry".Translate().ToString());
                TooltipHandler.TipRegion(row, t.def.description);
            }
            Widgets.EndScrollView();
        }

        private static void Party(Rect r, Faction f)
        {
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(r.x, r.y + 2f, 20f, 20f), f);
            Widgets.Label(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), f.Name.Truncate(r.width - 24f));
        }

        private static bool IsKnown(Treaty t) =>
            t.Involves(Faction.OfPlayer) || IntelUtility.IntelOn(t.a, t.b) >= IntelUtility.RequiredIntel(t.def.secrecy);
    }
}
