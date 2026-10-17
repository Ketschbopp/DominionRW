using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The player's diplomacy: their standing, a faction list you can search, sort, filter and pin, and for the selected
    /// faction its treaties, policies, every action with its odds and what it does, and the record of what passed between you.
    /// </summary>
    public class DiplomacyPanel
    {
        private const float Line = 24f;
        private const float TopBarHeight = 34f;
        private const float ListToolsHeight = 58f;
        private const int HistoryShown = 8;

        private enum SortMode { Name, Goodwill, Strength, Distance, Treaties }
        private enum FilterMode { All, Pinned, Allies, Neutral, Hostile, Partners }

        private static SortMode sort = SortMode.Name;
        private static FilterMode filter = FilterMode.All;
        private static string search = "";
        private static bool fullHistory;

        private Vector2 listScroll;
        private Vector2 detailScroll;
        private float detailHeight = 600f;
        private Faction lastSelected;
        private readonly List<Treaty> treaties = new List<Treaty>();
        private readonly List<Faction> shown = new List<Faction>();

        private static List<DiplomacyActionDef> actions;

        private static List<DiplomacyActionDef> Actions =>
            actions ??= DefDatabase<DiplomacyActionDef>.AllDefsListForReading.OrderBy(a => a.tier).ThenBy(a => a.order).ToList();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            DrawTopBar(new Rect(rect.x, rect.y, rect.width, TopBarHeight), comp);
            Rect body = new Rect(rect.x, rect.y + TopBarHeight + 6f, rect.width, rect.height - TopBarHeight - 6f);
            List<Faction> factions = comp.Cache.Simulated;
            Faction selected = DominionUIState.SelectedIn(factions);
            if (selected != lastSelected)
            {
                lastSelected = selected;
                detailScroll = Vector2.zero;
            }

            DrawFactionList(body.LeftPart(0.28f), factions, comp);
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
            TooltipHandler.TipRegion(r, "Dominion_Dip_Desk_Desc".Translate());
            r = new Rect(inner.x + w * 3f, inner.y, w, inner.height);
            Widgets.Label(r, "Dominion_Dip_Comms".Translate(LeadershipUtility.HasComms() ? "Dominion_Yes".Translate() : "Dominion_No".Translate()).ToString().Truncate(r.width));
            TooltipHandler.TipRegion(r, "Dominion_Dip_Comms_Desc".Translate());
        }

        // ------------------------------------------------------------------ faction list

        private void DrawFactionList(Rect rect, List<Faction> factions, WorldComponent_Dominion comp)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            // Search, sort and filter.
            Rect searchRect = new Rect(inner.x, inner.y, inner.width, 24f);
            search = Widgets.TextField(searchRect, search ?? "");
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 6f, searchRect.y + 2f, searchRect.width, 22f), "Dominion_UI_Search".Translate());
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(searchRect, "Dominion_UI_Search_Desc".Translate());
            Rect sortRect = new Rect(inner.x, inner.y + 28f, inner.width / 2f - 2f, 26f);
            if (DominionUIState.ButtonWithTip(sortRect, "Dominion_UI_SortLabel".Translate(("Dominion_UI_Sort_" + sort).Translate()), "Dominion_UI_Sort_Desc".Translate()))
                Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(SortMode)).Cast<SortMode>()
                    .Select(m => new FloatMenuOption(("Dominion_UI_Sort_" + m).Translate(), () => sort = m)).ToList()));
            Rect filterRect = new Rect(sortRect.xMax + 4f, sortRect.y, inner.width / 2f - 2f, 26f);
            if (DominionUIState.ButtonWithTip(filterRect, ("Dominion_UI_Filter_" + filter).Translate(), "Dominion_UI_Filter_Desc".Translate()))
                Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(FilterMode)).Cast<FilterMode>()
                    .Select(m => new FloatMenuOption(("Dominion_UI_Filter_" + m).Translate(), () => filter = m)).ToList()));

            Collect(factions, comp);
            Rect listRect = new Rect(inner.x, inner.y + ListToolsHeight + 2f, inner.width, inner.height - ListToolsHeight - 2f);
            if (shown.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(listRect, "Dominion_UI_NoMatch".Translate());
                GUI.color = Color.white;
                return;
            }
            Rect view = new Rect(0f, 0f, listRect.width - 16f, shown.Count * Line);
            Widgets.BeginScrollView(listRect, ref listScroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                Faction f = shown[i];
                Rect row = new Rect(0f, i * Line, view.width, Line);
                if (f == DominionUIState.selected) Widgets.DrawHighlightSelected(row);
                Widgets.DrawHighlightIfMouseover(row);

                // The pin star toggles without selecting the row.
                Rect star = new Rect(row.xMax - 20f, row.y + 3f, 18f, 18f);
                bool pinned = comp.Player.IsPinned(f);
                GUI.color = pinned ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, Mouse.IsOver(star) ? 0.8f : 0.25f);
                GUI.DrawTexture(star, pinned ? DominionPaper.Star : DominionPaper.StarOutline);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(star, pinned ? "Dominion_UI_Unpin".Translate() : "Dominion_UI_Pin".Translate());
                if (Widgets.ButtonInvisible(star)) comp.Player.TogglePin(f);

                Rect main = new Rect(row.x, row.y, row.width - 22f, row.height);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(main.x + 2f, main.y + 2f, 20f, 20f), f);
                GUI.color = f.RelationKindWith(Faction.OfPlayer).GetColor();
                Widgets.Label(new Rect(main.x + 26f, main.y, main.width - 26f, Line), f.Name.Truncate(main.width - 26f));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(main, () => ListTip(f, comp), f.loadID ^ 0x51D);
                if (Widgets.ButtonInvisible(main)) DominionUIState.selected = f;
            }
            Widgets.EndScrollView();
        }

        private static string ListTip(Faction f, WorldComponent_Dominion comp)
        {
            string tip = "Dominion_Tip_SelectFaction".Translate(f.Name) + "\n\n" + f.RelationKindWith(Faction.OfPlayer).GetLabelCap() + " (" + f.PlayerGoodwill.ToStringWithSign() + ")";
            RelationState r = comp.GetRelation(Faction.OfPlayer, f);
            if (r.distance >= 0f) tip += "\n" + "Dominion_UI_Distance".Translate(r.distance.ToString("F0"));
            return tip;
        }

        private void Collect(List<Faction> factions, WorldComponent_Dominion comp)
        {
            shown.Clear();
            Faction player = Faction.OfPlayer;
            foreach (Faction f in factions)
            {
                if (!search.NullOrEmpty() && f.Name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                FactionRelationKind kind = f.RelationKindWith(player);
                switch (filter)
                {
                    case FilterMode.Pinned when !comp.Player.IsPinned(f): continue;
                    case FilterMode.Allies when kind != FactionRelationKind.Ally: continue;
                    case FilterMode.Neutral when kind != FactionRelationKind.Neutral: continue;
                    case FilterMode.Hostile when kind != FactionRelationKind.Hostile: continue;
                    case FilterMode.Partners when !comp.Treaties.All.Any(t => t.Between(player, f)): continue;
                }
                shown.Add(f);
            }
            System.Func<Faction, float> key;
            switch (sort)
            {
                case SortMode.Goodwill: key = f => -f.PlayerGoodwill; break;
                case SortMode.Strength:
                    key = f => IntelUtility.Knows(f, IntelUtility.RestrictedIntel) && comp.TryGetState(f, out FactionState s) ? -s.militaryStrength : 0f;
                    break;
                case SortMode.Distance:
                    key = f =>
                    {
                        float d = comp.GetRelation(player, f).distance;
                        return d < 0f ? float.MaxValue : d;
                    };
                    break;
                case SortMode.Treaties: key = f => -comp.Treaties.All.Count(t => t.Between(player, f)); break;
                default: key = f => 0f; break;
            }
            // Pinned factions first, then the chosen order, then by name.
            List<Faction> ordered = shown.OrderBy(f => comp.Player.IsPinned(f) ? 0 : 1).ThenBy(key).ThenBy(f => f.Name).ToList();
            shown.Clear();
            shown.AddRange(ordered);
        }

        // ------------------------------------------------------------------ detail

        private void DrawDetail(Rect rect, Faction f, WorldComponent_Dominion comp)
        {
            Rect view = new Rect(0f, 0f, rect.width - 16f, detailHeight);
            Widgets.BeginScrollView(rect, ref detailScroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            Rect head = l.GetRect(34f);
            Text.Font = GameFont.Medium;
            Widgets.Label(head.LeftPart(0.45f), f.Name.Truncate(head.width * 0.45f));
            Text.Font = GameFont.Small;
            DominionUIState.JumpButtons(new Rect(head.x + head.width * 0.47f, head.y + 3f, head.width * 0.53f, 26f), f, DominionTab.Diplomacy);

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
            Rect treatyHead = l.GetRect(26f);
            Widgets.Label(treatyHead.LeftPart(0.6f), "Dominion_Dip_TreatiesWithYou".Translate());
            DiplomacyActionDef propose = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposeTreaty");
            if (propose != null)
            {
                string why = DiplomacyActions.CannotUseReason(propose, f);
                if (DominionUIState.ButtonWithTip(treatyHead.RightPart(0.38f), "Dominion_Treaties_Draft".Translate(),
                        propose.description + (why != null ? "\n\n" + why.Colorize(ColorLibrary.RedReadable) : ""), why == null))
                    Dialog_TreatyDraft.ProposeMenu(f);
            }
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
                Widgets.DrawHighlightIfMouseover(row);
                string expires = t.expiresTick >= 0
                    ? "Dominion_Dip_Expires".Translate((t.expiresTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)).ToString()
                    : "Dominion_Dip_NoExpiry".Translate().ToString();
                Rect labelRect = row.LeftPart(0.56f);
                Widgets.Label(labelRect, ("  " + t.Label + "  (" + expires + ")").Truncate(labelRect.width));
                TooltipHandler.TipRegion(labelRect, t.def.description + "\n\n" + "Dominion_Treaties_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(labelRect)) Find.WindowStack.Add(new Dialog_TreatyDocument(t));
                Rect renew = new Rect(row.x + row.width * 0.57f, row.y, row.width * 0.2f, row.height);
                if (t.def.Expires && !t.def.imposed
                    && DominionUIState.ButtonWithTip(renew, t.DaysLeft <= 15 ? "Dominion_Treaty_Renew".Translate() : "Dominion_Treaty_Amend".Translate(), "Dominion_Treaty_Renew_Desc".Translate(f.Name)))
                    Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, f, t));
                if (DominionUIState.ButtonWithTip(row.RightPart(0.21f), "Dominion_Treaty_TearUp".Translate(), "Dominion_Tip_CancelTreaty".Translate() + "\n\n" + TreatyDraft.TearUpCost(t, player)))
                    Dialog_TreatyDocument.ConfirmTearUp(t);
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
                DrawAction(l, def, worker, f, comp);
            }

            // The record
            l.GapLine();
            DrawHistory(l, f, comp);

            detailHeight = Mathf.Max(l.CurHeight + 10f, rect.height);
            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawAction(Listing_Standard l, DiplomacyActionDef def, DiplomacyActionWorker worker, Faction f, WorldComponent_Dominion comp)
        {
            Rect row = l.GetRect(30f);
            string reason = DiplomacyActions.CannotUseReason(def, f);
            string label = def.LabelCap.ToString() + (def.influenceCost > 0f ? "  (" + "Dominion_Dip_Cost".Translate(def.influenceCost.ToString("F0")).ToString() + ")" : "")
                           + (worker.ViaLetter ? "  ✉" : "");
            Rect button = row.LeftPart(0.55f);
            string outcomeKey = "Dominion_Outcome_" + def.defName;
            string tip = def.description + (outcomeKey.CanTranslate() ? "\n\n" + outcomeKey.Translate() : "")
                         + (worker.ViaLetter ? "\n\n" + "Dominion_Letter_Tip".Translate() : "") + (reason != null ? "\n\n" + reason.Colorize(ColorLibrary.RedReadable) : "");
            TooltipHandler.TipRegion(button, tip);
            if (reason != null)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(button, label, active: false);
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(button, label))
            {
                if (worker.ViaLetter) Find.WindowStack.Add(new Dialog_EnvoyLetter(def, f));
                else worker.Execute(f);
            }

            // A cooldown shows as a bar draining under the button.
            int until = comp.Player.CooldownUntil(def.defName, f.loadID);
            int now = Find.TickManager.TicksGame;
            if (until > now && def.cooldownDays > 0)
            {
                float frac = Mathf.Clamp01((until - now) / (float)(def.cooldownDays * GenDate.TicksPerDay));
                Widgets.DrawBoxSolid(new Rect(button.x + 2f, button.yMax - 4f, (button.width - 4f) * frac, 3f), new Color(1f, 0.6f, 0.3f, 0.8f));
            }

            float? chance = reason == null ? worker.AcceptanceChance(f) : null;
            Rect info = row.RightPart(0.43f);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (reason != null)
            {
                GUI.color = new Color(1f, 0.6f, 0.4f);
                Widgets.Label(info, reason.Truncate(info.width));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(info, reason);
            }
            else if (chance.HasValue)
            {
                Rect bar = new Rect(info.x, info.y + 6f, Mathf.Min(info.width, 160f), info.height - 12f);
                Widgets.FillableBar(bar, chance.Value, DominionPaper.OddsFill(chance.Value));
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(bar, "Dominion_Dip_Chance".Translate(chance.Value.ToStringPercent()));
                Text.Font = GameFont.Small;
                string breakdown = worker.ChanceBreakdown(f);
                TooltipHandler.TipRegion(bar, "Dominion_Odds_Title".Translate(f.Name) + "\n" + (breakdown.NullOrEmpty() ? "" : breakdown + "\n")
                                              + "Dominion_Odds_Total".Translate(chance.Value.ToStringPercent()));
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawHistory(Listing_Standard l, Faction f, WorldComponent_Dominion comp)
        {
            Rect head = l.GetRect(26f);
            Widgets.Label(head.LeftPart(0.6f), "Dominion_Dip_History".Translate());
            List<DiplomacyLogEntry> log = comp.Player.diplomacyLog;
            var entries = new List<DiplomacyLogEntry>();
            for (int i = log.Count - 1; i >= 0; i--)
                if (log[i].factionId == f.loadID) entries.Add(log[i]);
            if (entries.Count > HistoryShown
                && DominionUIState.ButtonWithTip(head.RightPart(0.3f), fullHistory ? "Dominion_Dip_HistoryLess".Translate() : "Dominion_Dip_HistoryAll".Translate(entries.Count),
                    "Dominion_Dip_History_Desc".Translate()))
                fullHistory = !fullHistory;
            if (entries.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_Dip_HistoryNone".Translate());
                GUI.color = Color.white;
                return;
            }
            int now = Find.TickManager.TicksGame;
            int n = fullHistory ? entries.Count : Mathf.Min(HistoryShown, entries.Count);
            for (int i = 0; i < n; i++)
            {
                DiplomacyLogEntry e = entries[i];
                string who = e.byThem ? "Dominion_Dip_HistoryThem".Translate() : "Dominion_Dip_HistoryYou".Translate();
                string when = (now - e.tick).ToStringTicksToPeriod(allowSeconds: false);
                string line = "  " + who + "  " + e.text;
                Text.Font = GameFont.Tiny;
                Rect r = l.GetRect(Mathf.Max(18f, Text.CalcHeight(line, l.ColumnWidth - 90f)));
                GUI.color = e.outcome > 0 ? new Color(0.6f, 0.9f, 0.6f) : e.outcome < 0 ? new Color(1f, 0.6f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
                Widgets.Label(new Rect(r.x, r.y, r.width - 90f, r.height), line);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.xMax - 88f, r.y, 88f, r.height), "Dominion_News_Ago".Translate(when));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }
    }

    /// <summary>Writes what other factions do to the player into the diplomatic record.</summary>
    public class DiplomacyLogSystem : SimSystem
    {
        public override int Order => 96;

        public override void OnEvent(DominionEvent ev)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || ev.primary == null || ev.primary.IsPlayer || ev.secondary != player) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || !DominionScheduler.IsSimulated(ev.primary)) return;
            int outcome = ev.def.category == EventCategory.War || ev.def.category == EventCategory.Espionage ? -1 : 0;
            comp.Player.Log(ev.primary, ev.Headline, outcome, byThem: true);
        }
    }
}
