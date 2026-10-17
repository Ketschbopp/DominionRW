using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The Diplomacy tab, as a telegraph office: walnut panelling under a desk lamp, a ticker tape running across the
    /// top with the latest wires, a switchboard of factions on the left (search, sort, filter, pin), brass dials for the
    /// selected faction's goodwill and opinions, a telegram with where you stand and your treaties, a bank of telegraph
    /// keys for every action with its odds, and the record of what passed between you printed as tape.
    /// </summary>
    public class DiplomacyPanel
    {
        private const float TickerHeight = 30f;
        private const float StatusHeight = 34f;
        private const float BoardTools = 62f;
        private const float BoardRow = 30f;
        private const float DialHeight = 104f;
        private const float KeyRow = 40f;
        private const int HistoryShown = 8;

        private enum SortMode { Name, Goodwill, Strength, Distance, Treaties }
        private enum FilterMode { All, Pinned, Allies, Neutral, Hostile, Partners }

        private static SortMode sort = SortMode.Name;
        private static FilterMode filter = FilterMode.All;
        private static string search = "";
        private static bool fullHistory;

        private Vector2 boardScroll;
        private Vector2 telegramScroll;
        private Vector2 keysScroll;
        private float telegramHeight = 400f;
        private float keysHeight = 400f;
        private Faction lastSelected;
        private readonly List<Treaty> treaties = new List<Treaty>();
        private readonly List<Faction> shown = new List<Faction>();

        // The ticker scrolls on its own and stops while the mouse is over it.
        private float tickerOffset;
        private float lastTickerTime;

        // A key just pressed stays down a moment.
        private DiplomacyActionDef pressed;
        private float pressedAt;
        private TalkTopic? pressedTalk;

        // What the faction last said back, shown under the talk keys.
        private TalkResult lastTalk;
        private Faction lastTalkFaction;

        private static List<DiplomacyActionDef> actions;

        private static List<DiplomacyActionDef> Actions =>
            actions ??= DefDatabase<DiplomacyActionDef>.AllDefsListForReading.OrderBy(a => a.tier).ThenBy(a => a.order).ToList();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            // The office: panelled walls, a lamp over the desk, dark corners.
            DominionRooms.DrawTiled(rect, DominionRooms.Wood, 0.7f);
            DominionRooms.DrawGlow(new Vector2(rect.x + rect.width * 0.62f, rect.y + rect.height * 0.35f), rect.width * 0.55f, new Color(1f, 0.8f, 0.5f, 0.18f));
            DominionRooms.DrawVignette(rect, 0.7f);

            Rect inner = rect.ContractedBy(8f);
            DrawTicker(new Rect(inner.x, inner.y, inner.width, TickerHeight), comp);
            DrawStatus(new Rect(inner.x, inner.y + TickerHeight + 6f, inner.width, StatusHeight), comp);
            float top = inner.y + TickerHeight + StatusHeight + 14f;
            Rect body = new Rect(inner.x, top, inner.width, inner.yMax - top);

            List<Faction> factions = comp.Cache.Simulated;
            Faction selected = DominionUIState.SelectedIn(factions);
            if (selected != lastSelected)
            {
                lastSelected = selected;
                telegramScroll = keysScroll = Vector2.zero;
            }

            float boardW = Mathf.Clamp(body.width * 0.26f, 210f, 290f);
            DrawBoard(new Rect(body.x, body.y, boardW, body.height), factions, comp);
            Rect desk = new Rect(body.x + boardW + 10f, body.y, body.width - boardW - 10f, body.height);
            if (selected == null) return;
            DrawDials(new Rect(desk.x, desk.y, desk.width, DialHeight), selected, comp);
            Rect lower = new Rect(desk.x, desk.y + DialHeight + 8f, desk.width, desk.height - DialHeight - 8f);
            float telegramW = lower.width * 0.54f;
            DrawTelegram(new Rect(lower.x, lower.y, telegramW, lower.height), selected, comp);
            DrawKeys(new Rect(lower.x + telegramW + 10f, lower.y, lower.width - telegramW - 10f, lower.height), selected, comp);
        }

        // ------------------------------------------------------------------ ticker and status

        /// <summary>The tape: the latest wires from every faction, running right to left. Click one to open that faction.</summary>
        private void DrawTicker(Rect rect, WorldComponent_Dominion comp)
        {
            float now = Time.realtimeSinceStartup;
            bool over = Mouse.IsOver(rect);
            if (!over) tickerOffset += (now - lastTickerTime) * 45f;
            lastTickerTime = now;

            Widgets.DrawBoxSolid(rect.ExpandedBy(2f), new Color(0f, 0f, 0f, 0.5f));
            DominionRooms.DrawStrip(rect, DominionRooms.TickerTape, tickerOffset / (DominionRooms.TickerTape.width * rect.height / DominionRooms.TickerTape.height));
            List<DiplomacyLogEntry> log = comp.Player.diplomacyLog;
            if (log.Count == 0)
            {
                DominionTheme.InkLabel(rect.ContractedBy(10f, 0f), "Dominion_Telegraph_TapeEmpty".Translate(), DominionTheme.InkFaded, GameFont.Small, TextAnchor.MiddleLeft);
                return;
            }
            var pieces = new List<(string text, Faction f, int outcome)>();
            for (int i = log.Count - 1; i >= 0 && pieces.Count < 12; i--)
            {
                DiplomacyLogEntry e = log[i];
                Faction f = comp.Cache.FactionById(e.factionId);
                if (f == null) continue;
                pieces.Add(((e.byThem ? "◂ " : "▸ ") + f.Name.ToUpper() + ": " + DominionRooms.Telegraphese(e.text) + "      ", f, e.outcome));
            }
            Text.Font = GameFont.Small;
            float total = 0f;
            var widths = new List<float>();
            foreach (var p in pieces)
            {
                float w = Text.CalcSize(p.text).x;
                widths.Add(w);
                total += w;
            }
            if (total <= 0f) return;
            GUI.BeginGroup(rect);
            float x = -Mathf.Repeat(tickerOffset, total);
            // Draw the tape twice so it wraps without a gap.
            for (int pass = 0; pass < 2 + (int)(rect.width / total); pass++)
                for (int i = 0; i < pieces.Count; i++)
                {
                    Rect r = new Rect(x, 6f, widths[i], rect.height - 6f);
                    x += widths[i];
                    if (r.xMax < 0f || r.x > rect.width) continue;
                    Color ink = pieces[i].outcome > 0 ? DominionTheme.InkGreen : pieces[i].outcome < 0 ? DominionTheme.InkRed : DominionTheme.Ink;
                    if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, DominionTheme.PaperShade);
                    DominionTheme.InkLabel(r, pieces[i].text, ink, GameFont.Small, TextAnchor.MiddleLeft);
                    Faction f = pieces[i].f;
                    TooltipHandler.TipRegion(r, "Dominion_Telegraph_TapeTip".Translate(f.Name));
                    if (Widgets.ButtonInvisible(r)) DominionUIState.selected = f;
                }
            GUI.EndGroup();
        }

        /// <summary>The office's brass plates: who signs, how much influence is left, the desk and the comms console.</summary>
        private static void DrawStatus(Rect rect, WorldComponent_Dominion comp)
        {
            float w = rect.width / 4f;
            Pawn hos = LeadershipUtility.HeadOfState();
            Rect r = new Rect(rect.x, rect.y, w - 6f, rect.height);
            DominionRooms.BrassPlate(r, "Dominion_Dip_HeadOfState".Translate(hos != null ? hos.LabelShortCap : "Dominion_None".Translate().ToString()), GameFont.Tiny);
            TooltipHandler.TipRegion(r, hos != null ? (string)"Dominion_Dip_HeadOfState_Desc".Translate() : LeadershipUtility.NoHeadOfStateReason());

            // Influence as a column of brass filling up.
            r = new Rect(rect.x + w, rect.y, w - 6f, rect.height);
            float frac = comp.Player.influence / PlayerState.MaxInfluence;
            Widgets.DrawBoxSolid(r, new Color(0.1f, 0.07f, 0.04f, 0.8f));
            GUI.color = new Color(1f, 0.95f, 0.85f);
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, r.width * frac, r.height), DominionRooms.Brass, new Rect(0f, 0f, frac, 1f));
            GUI.color = Color.white;
            DominionRooms.DrawMoulding(r);
            DominionTheme.InkLabel(r, "Dominion_Dip_Influence".Translate(comp.Player.influence.ToString("F0"), PlayerState.MaxInfluence.ToString("F0")),
                frac > 0.5f ? DominionRooms.BrassInk : DominionRooms.BrassLight, GameFont.Tiny, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(r, "Dominion_Dip_Influence_Desc".Translate());

            Building_LeadershipDesk desk = LeadershipUtility.BestDesk();
            r = new Rect(rect.x + w * 2f, rect.y, w - 6f, rect.height);
            DominionRooms.BrassPlate(r, "Dominion_Dip_Desk".Translate(desk != null ? ("Dominion_DeskTier_" + desk.Tier).Translate().ToString() : "Dominion_None".Translate().ToString()), GameFont.Tiny);
            TooltipHandler.TipRegion(r, "Dominion_Dip_Desk_Desc".Translate());
            r = new Rect(rect.x + w * 3f, rect.y, w, rect.height);
            DominionRooms.BrassPlate(r, "Dominion_Dip_Comms".Translate(LeadershipUtility.HasComms() ? "Dominion_Yes".Translate() : "Dominion_No".Translate()), GameFont.Tiny);
            TooltipHandler.TipRegion(r, "Dominion_Dip_Comms_Desc".Translate());
        }

        // ------------------------------------------------------------------ the switchboard

        private void DrawBoard(Rect rect, List<Faction> factions, WorldComponent_Dominion comp)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.05f, 0.03f, 0.7f));
            DominionRooms.DrawMoulding(rect);
            Rect inner = rect.ContractedBy(6f);

            Rect searchRect = new Rect(inner.x, inner.y, inner.width, 26f);
            search = Widgets.TextField(searchRect, search ?? "");
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 6f, searchRect.y + 3f, searchRect.width, 22f), "Dominion_UI_Search".Translate());
                GUI.color = Color.white;
            }
            TooltipHandler.TipRegion(searchRect, "Dominion_UI_Search_Desc".Translate());
            Rect sortRect = new Rect(inner.x, inner.y + 30f, inner.width / 2f - 2f, 28f);
            if (DominionRooms.BrassButton(sortRect, "Dominion_UI_SortLabel".Translate(("Dominion_UI_Sort_" + sort).Translate()), "Dominion_UI_Sort_Desc".Translate()))
                Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(SortMode)).Cast<SortMode>()
                    .Select(m => new FloatMenuOption(("Dominion_UI_Sort_" + m).Translate(), () => sort = m)).ToList()));
            Rect filterRect = new Rect(sortRect.xMax + 4f, sortRect.y, inner.width / 2f - 2f, 28f);
            if (DominionRooms.BrassButton(filterRect, ("Dominion_UI_Filter_" + filter).Translate(), "Dominion_UI_Filter_Desc".Translate(), null, filter != FilterMode.All))
                Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(FilterMode)).Cast<FilterMode>()
                    .Select(m => new FloatMenuOption(("Dominion_UI_Filter_" + m).Translate(), () => filter = m)).ToList()));

            Collect(factions, comp);
            Rect listRect = new Rect(inner.x, inner.y + BoardTools + 4f, inner.width, inner.height - BoardTools - 4f);
            if (shown.Count == 0)
            {
                DominionTheme.InkLabel(listRect, "Dominion_UI_NoMatch".Translate(), DominionRooms.BrassLight);
                return;
            }
            Rect view = new Rect(0f, 0f, listRect.width - 16f, shown.Count * BoardRow);
            Widgets.BeginScrollView(listRect, ref boardScroll, view);
            for (int i = 0; i < shown.Count; i++)
                DrawBoardRow(new Rect(0f, i * BoardRow, view.width, BoardRow - 3f), shown[i], comp);
            Widgets.EndScrollView();
        }

        /// <summary>A line on the switchboard: a jack lit in the relation's colour, the faction's plate and goodwill.</summary>
        private static void DrawBoardRow(Rect row, Faction f, WorldComponent_Dominion comp)
        {
            bool sel = f == DominionUIState.selected;
            bool over = Mouse.IsOver(row);
            GUI.color = sel ? new Color(1f, 0.97f, 0.88f) : over ? new Color(0.92f, 0.88f, 0.8f) : new Color(0.72f, 0.66f, 0.58f);
            GUI.DrawTexture(row, DominionRooms.Brass, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            if (sel) DominionRooms.DrawGlow(new Vector2(row.x + 10f, row.center.y), 18f, new Color(1f, 0.85f, 0.4f, 0.5f));

            FactionRelationKind kind = f.RelationKindWith(Faction.OfPlayer);
            Rect jack = new Rect(row.x + 5f, row.center.y - 5f, 10f, 10f);
            Widgets.DrawBoxSolid(jack.ExpandedBy(1f), new Color(0.1f, 0.07f, 0.04f));
            Widgets.DrawBoxSolid(jack, kind.GetColor());

            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 20f, row.y + 3f, 20f, 20f), f);
            Rect star = new Rect(row.xMax - 22f, row.y + 4f, 18f, 18f);
            Rect name = new Rect(row.x + 44f, row.y, row.width - 44f - 66f, row.height);
            DominionTheme.InkLabel(name, f.Name.Truncate(name.width), DominionRooms.BrassInk, GameFont.Small, TextAnchor.MiddleLeft);
            Rect gw = new Rect(star.x - 40f, row.y, 38f, row.height);
            DominionTheme.InkLabel(gw, f.PlayerGoodwill.ToStringWithSign(), f.PlayerGoodwill >= 0 ? DominionTheme.InkGreen : DominionTheme.InkRed, GameFont.Tiny, TextAnchor.MiddleRight);

            // The pin star toggles without selecting the row.
            bool pinned = comp.Player.IsPinned(f);
            GUI.color = pinned ? new Color(0.75f, 0.4f, 0.05f) : new Color(0.2f, 0.14f, 0.06f, Mouse.IsOver(star) ? 0.8f : 0.3f);
            GUI.DrawTexture(star, pinned ? DominionPaper.Star : DominionPaper.StarOutline);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(star, pinned ? "Dominion_UI_Unpin".Translate() : "Dominion_UI_Pin".Translate());
            if (Widgets.ButtonInvisible(star))
            {
                comp.Player.TogglePin(f);
                return;
            }
            Rect main = new Rect(row.x, row.y, row.width - 24f, row.height);
            TooltipHandler.TipRegion(main, () => ListTip(f, comp), f.loadID ^ 0x51D);
            if (Widgets.ButtonInvisible(main))
            {
                DominionUIState.selected = f;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
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
            string text = search?.Trim();
            foreach (Faction f in factions)
            {
                if (!text.NullOrEmpty() && f.Name.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
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

        // ------------------------------------------------------------------ dials

        /// <summary>The faction's name plate and three gauges: goodwill, their opinion of you, and yours of them.</summary>
        private static void DrawDials(Rect rect, Faction f, WorldComponent_Dominion comp)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.05f, 0.03f, 0.6f));
            DominionRooms.DrawMoulding(rect);
            Rect inner = rect.ContractedBy(8f);
            float dialW = Mathf.Min(120f, inner.width * 0.16f);
            Rect plate = new Rect(inner.x, inner.y, inner.width - dialW * 3f - 16f, inner.height);

            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(plate.x, plate.y + 2f, 32f, 32f), f);
            DominionTheme.InkLabel(new Rect(plate.x + 38f, plate.y, plate.width - 38f, 34f), f.Name.Truncate(plate.width - 38f), DominionRooms.BrassLight, GameFont.Medium, TextAnchor.MiddleLeft);
            FactionRelationKind kind = f.RelationKindWith(Faction.OfPlayer);
            string leader = f.leader != null ? "  ·  " + f.leader.LabelShortCap : "";
            DominionTheme.InkLabel(new Rect(plate.x + 38f, plate.y + 32f, plate.width - 38f, 18f), (kind.GetLabelCap() + leader).Truncate(plate.width - 38f), kind.GetColor(), GameFont.Tiny);
            DominionUIState.JumpButtons(new Rect(plate.x, plate.yMax - 28f, plate.width, 26f), f, DominionTab.Diplomacy);

            bool known = IntelUtility.Knows(f, IntelUtility.RestrictedIntel);
            RelationState theirs = comp.GetRelation(f, Faction.OfPlayer);
            RelationState ours = comp.GetRelation(Faction.OfPlayer, f);
            float x = plate.xMax + 8f;
            DominionRooms.DrawDial(new Rect(x, inner.y, dialW, inner.height), f.PlayerGoodwill, "Dominion_Telegraph_Goodwill".Translate(f.PlayerGoodwill.ToStringWithSign()),
                null, "Dominion_Telegraph_Goodwill_Desc".Translate(f.Name));
            x += dialW + 4f;
            if (known)
                DominionRooms.DrawDial(new Rect(x, inner.y, dialW, inner.height), theirs.opinion, "Dominion_Telegraph_TheirOpinion".Translate(theirs.opinion.ToString("F0")),
                    ours.opinion, "Dominion_Telegraph_TheirOpinion_Desc".Translate(f.Name));
            else
            {
                DominionRooms.DrawDial(new Rect(x, inner.y, dialW, inner.height), 0f, "Dominion_Telegraph_TheirOpinionUnknown".Translate());
                TooltipHandler.TipRegion(new Rect(x, inner.y, dialW, inner.height), IntelUtility.Unknown(IntelUtility.RestrictedIntel));
            }
            x += dialW + 4f;
            DominionRooms.DrawDial(new Rect(x, inner.y, dialW, inner.height), ours.opinion, "Dominion_Telegraph_YourOpinion".Translate(ours.opinion.ToString("F0")),
                null, "Dominion_Telegraph_YourOpinion_Desc".Translate(f.Name));
        }

        // ------------------------------------------------------------------ the telegram

        private void DrawTelegram(Rect rect, Faction f, WorldComponent_Dominion comp)
        {
            Rect write = DominionRooms.DrawTelegram(rect, "Dominion_Telegraph_Head".Translate(f.Name.ToUpper()));
            Rect view = new Rect(0f, 0f, write.width - 16f, Mathf.Max(write.height, telegramHeight));
            Widgets.BeginScrollView(write, ref telegramScroll, view);
            float y = 0f, w = view.width;
            Faction player = Faction.OfPlayer;

            // Where you stand, in telegraphese.
            FactionRelationKind kind = f.RelationKindWith(player);
            RelationState theirs = comp.GetRelation(f, player);
            RelationState mine = comp.GetRelation(player, f);
            string opinion = IntelUtility.Knows(f, IntelUtility.RestrictedIntel) ? theirs.opinion.ToString("F0") : "?";
            string standing = DominionRooms.Telegraphese("Dominion_Dip_Standing".Translate(kind.GetLabelCap(), f.PlayerGoodwill.ToStringWithSign(), opinion) + ". "
                + "Dominion_Dip_Borders".Translate(DiplomacyAction_Borders.BorderLabel(mine.border), DiplomacyAction_Borders.BorderLabel(theirs.border)) + ". "
                + "Dominion_Dip_Embargo".Translate(mine.embargo ? "Dominion_Yes".Translate() : "Dominion_No".Translate(), theirs.embargo ? "Dominion_Yes".Translate() : "Dominion_No".Translate()));
            DominionPricing.Compute(f, out float buy, out float sell);
            if (!Mathf.Approximately(buy, 1f) || !Mathf.Approximately(sell, 1f))
                standing += " " + DominionRooms.Telegraphese("Dominion_Dip_Prices".Translate(buy.ToStringPercent(), sell.ToStringPercent()));
            y += DominionPaper.Paragraph(0f, y, w, standing, DominionTheme.Ink, GameFont.Tiny) + 8f;

            // Treaties, each with renew and tear up.
            Rect head = new Rect(0f, y, w, 24f);
            DominionPaper.Caption(new Rect(0f, y + 4f, w * 0.55f, 18f), "Dominion_Dip_TreatiesWithYou".Translate());
            DiplomacyActionDef propose = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposeTreaty");
            if (propose != null)
            {
                string why = DiplomacyActions.CannotUseReason(propose, f);
                if (DominionPaper.InkButton(new Rect(w * 0.58f, y, w * 0.42f, 24f), "Dominion_Treaties_Draft".Translate(), propose.description, why, false, DominionTheme.InkGreen))
                    Dialog_TreatyDraft.ProposeMenu(f);
            }
            y = head.yMax + 4f;
            comp.Treaties.Between(player, f, treaties);
            if (treaties.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(8f, y, w, 20f), "Dominion_None".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                y += 22f;
            }
            foreach (Treaty t in treaties)
            {
                Rect row = new Rect(0f, y, w, 26f);
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, DominionTheme.PaperShade);
                DominionPaper.DrawSeal(new Rect(row.x, row.y + 1f, 24f, 24f), f);
                string expires = t.expiresTick >= 0 ? "Dominion_Treaties_DaysLeft".Translate(t.DaysLeft).ToString() : "Dominion_Dip_NoExpiry".Translate().ToString();
                Rect labelRect = new Rect(row.x + 28f, row.y, w * 0.56f - 28f, row.height);
                DominionTheme.InkLabel(labelRect, (t.Label + "  ·  " + expires).Truncate(labelRect.width), t.DaysLeft <= 15 && t.expiresTick >= 0 ? DominionTheme.InkRed : DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                TooltipHandler.TipRegion(labelRect, t.def.description + "\n\n" + "Dominion_Treaties_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(labelRect)) Find.WindowStack.Add(new Dialog_TreatyDocument(t));
                if (t.def.Expires && !t.def.imposed
                    && DominionPaper.InkButton(new Rect(w * 0.57f, row.y + 1f, w * 0.21f, 24f), t.DaysLeft <= 15 ? "Dominion_Treaty_Renew".Translate() : "Dominion_Treaty_Amend".Translate(),
                        "Dominion_Treaty_Renew_Desc".Translate(f.Name), null, false, DominionTheme.InkGreen))
                    Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, f, t));
                if (DominionPaper.InkButton(new Rect(w * 0.79f, row.y + 1f, w * 0.21f, 24f), "Dominion_Treaty_TearUp".Translate(),
                        "Dominion_Tip_CancelTreaty".Translate() + "\n\n" + TreatyDraft.TearUpCost(t, player), null, false, DominionTheme.InkRed))
                    Dialog_TreatyDocument.ConfirmTearUp(t);
                y += 28f;
            }
            y += 8f;

            // Standing deals with them.
            List<TradeAgreement> deals = TradeDeals.OpenWith(f).ToList();
            if (deals.Count > 0)
            {
                DominionPaper.Caption(new Rect(0f, y + 4f, w, 18f), "Dominion_Deal_WithYou".Translate());
                y += 24f;
                foreach (TradeAgreement d in deals) y = Dialog_TradeLedger.DealRow(y, w, d, false);
                y += 8f;
            }

            // The record, printed as strips of tape.
            y = DrawHistory(y, w, f, comp);
            telegramHeight = y + 10f;
            Widgets.EndScrollView();
        }

        private static float DrawHistory(float y, float w, Faction f, WorldComponent_Dominion comp)
        {
            DominionPaper.Caption(new Rect(0f, y + 4f, w * 0.6f, 18f), "Dominion_Dip_History".Translate());
            List<DiplomacyLogEntry> log = comp.Player.diplomacyLog;
            var entries = new List<DiplomacyLogEntry>();
            for (int i = log.Count - 1; i >= 0; i--)
                if (log[i].factionId == f.loadID) entries.Add(log[i]);
            if (entries.Count > HistoryShown
                && DominionPaper.InkButton(new Rect(w * 0.62f, y, w * 0.38f, 22f), fullHistory ? "Dominion_Dip_HistoryLess".Translate() : "Dominion_Dip_HistoryAll".Translate(entries.Count),
                    "Dominion_Dip_History_Desc".Translate()))
                fullHistory = !fullHistory;
            y += 26f;
            if (entries.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(8f, y, w, 20f), "Dominion_Dip_HistoryNone".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                return y + 22f;
            }
            int now = Find.TickManager.TicksGame;
            int n = fullHistory ? entries.Count : Mathf.Min(HistoryShown, entries.Count);
            for (int i = 0; i < n; i++)
            {
                DiplomacyLogEntry e = entries[i];
                string line = (e.byThem ? "◂ " : "▸ ") + DominionRooms.Telegraphese(e.text);
                Text.Font = GameFont.Tiny;
                float h = Mathf.Max(20f, Text.CalcHeight(line, w - 100f) + 4f);
                Text.Font = GameFont.Small;
                Rect strip = new Rect(e.byThem ? 0f : 12f, y, w - 12f, h);
                DominionRooms.DrawStrip(strip, DominionRooms.TickerTape, e.tick * 0.0001f);
                Color ink = e.outcome > 0 ? DominionTheme.InkGreen : e.outcome < 0 ? DominionTheme.InkRed : DominionTheme.Ink;
                DominionTheme.InkLabel(new Rect(strip.x + 6f, strip.y + 2f, strip.width - 92f, strip.height - 2f), line, ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                DominionTheme.InkLabel(new Rect(strip.xMax - 86f, strip.y, 82f, strip.height), "Dominion_News_Ago".Translate((now - e.tick).ToStringTicksToPeriod(allowSeconds: false)),
                    DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(strip, (e.byThem ? "Dominion_Dip_HistoryThem" : "Dominion_Dip_HistoryYou").Translate() + " " + e.text);
                y += h + 3f;
            }
            return y;
        }

        // ------------------------------------------------------------------ the keys

        /// <summary>A bank of telegraph keys, one per action, grouped by tier; each with its cost, odds and cooldown.</summary>
        private void DrawKeys(Rect rect, Faction f, WorldComponent_Dominion comp)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.05f, 0.03f, 0.7f));
            DominionRooms.DrawMoulding(rect);
            Rect inner = rect.ContractedBy(8f);
            DominionTheme.InkLabel(new Rect(inner.x, inner.y, inner.width, 22f), "Dominion_Telegraph_Keys".Translate().ToString().ToUpper(), DominionRooms.BrassLight, GameFont.Tiny);
            DrawDealButtons(new Rect(inner.x, inner.y + 20f, inner.width, 28f), f);
            Rect list = new Rect(inner.x, inner.y + 54f, inner.width, inner.height - 54f);
            Rect view = new Rect(0f, 0f, list.width - 16f, Mathf.Max(list.height, keysHeight));
            Widgets.BeginScrollView(list, ref keysScroll, view);
            float y = DrawTalk(view.width, f);
            ActionTier? lastTier = null;
            foreach (DiplomacyActionDef def in Actions)
            {
                DiplomacyActionWorker worker = def.Worker;
                if (!worker.Visible(f)) continue;
                if (lastTier != def.tier)
                {
                    lastTier = def.tier;
                    y += 4f;
                    DominionTheme.InkLabel(new Rect(0f, y, view.width, 18f), ("Dominion_ActionTier_" + def.tier).Translate().ToString().ToUpper(), new Color(0.75f, 0.65f, 0.45f), GameFont.Tiny);
                    Widgets.DrawBoxSolid(new Rect(0f, y + 17f, view.width, 1f), new Color(0.75f, 0.65f, 0.45f, 0.3f));
                    y += 20f;
                }
                DrawKey(new Rect(0f, y, view.width, KeyRow - 4f), def, worker, f, comp);
                y += KeyRow;
            }
            keysHeight = y + 6f;
            Widgets.EndScrollView();
        }

        /// <summary>The big switches over the keys: an audience with their leader, trade over the wire, and the ledger of standing deals.</summary>
        private static void DrawDealButtons(Rect rect, Faction f)
        {
            float w = (rect.width - 8f) / 3f;
            if (DominionRooms.BrassButton(new Rect(rect.x, rect.y, w, rect.height), "Dominion_Audience_Button".Translate(),
                    "Dominion_Audience_Button_Desc".Translate(f.leader?.LabelShortCap ?? f.Name, DominionSettings.PlayerCooldownDays(Dialog_Audience.CooldownDays)), Dialog_Audience.CannotHoldReason(f)))
                Dialog_Audience.Request(f);
            if (DominionRooms.BrassButton(new Rect(rect.x + w + 4f, rect.y, w, rect.height), "Dominion_Deal_Trade".Translate(),
                    "Dominion_Deal_Trade_Desc".Translate(f.Name, TradeDeals.OfficeDays), TradeDeals.CannotDealReason(f)))
                TradeDeals.OpenTrade(f);
            if (DominionRooms.BrassButton(new Rect(rect.x + (w + 4f) * 2f, rect.y, w, rect.height), "Dominion_Ledger_Button".Translate(TradeDeals.Open.Count()),
                    "Dominion_Ledger_Button_Desc".Translate()))
                Find.WindowStack.Add(new Dialog_TradeLedger());
        }

        // ------------------------------------------------------------------ talk

        /// <summary>
        /// The talk keys at the top of the bank: quick words over the wire, each answered in the faction's own voice.
        /// The last answer stays printed under them. Returns the y below.
        /// </summary>
        private float DrawTalk(float width, Faction f)
        {
            float y = 0f;
            DominionTheme.InkLabel(new Rect(0f, y, width, 18f), "Dominion_Talk_Heading".Translate().ToString().ToUpper(), new Color(0.75f, 0.65f, 0.45f), GameFont.Tiny);
            Widgets.DrawBoxSolid(new Rect(0f, y + 17f, width, 1f), new Color(0.75f, 0.65f, 0.45f, 0.3f));
            y += 20f;
            // Two keys to a row: talk is quick.
            float half = (width - 6f) / 2f;
            for (int i = 0; i < DiplomacyTalk.Topics.Length; i++)
            {
                Rect r = new Rect(i % 2 == 0 ? 0f : half + 6f, y + i / 2 * (KeyRow - 2f), half, KeyRow - 6f);
                DrawTalkKey(r, DiplomacyTalk.Topics[i], f);
            }
            y += Mathf.CeilToInt(DiplomacyTalk.Topics.Length / 2f) * (KeyRow - 2f);

            // Their last answer, on a strip of tape.
            if (lastTalk != null && lastTalkFaction == f)
            {
                string text = "“" + lastTalk.reply + "”" + (lastTalk.fact.NullOrEmpty() ? "" : "\n" + lastTalk.fact);
                Text.Font = GameFont.Tiny;
                float h = Text.CalcHeight(text, width - 12f) + 8f;
                Text.Font = GameFont.Small;
                Rect strip = new Rect(0f, y, width, h);
                DominionRooms.DrawStrip(strip, DominionRooms.TickerTape, 0.3f);
                Color ink = lastTalk.outcome > 0 ? DominionTheme.InkGreen : lastTalk.outcome < 0 ? DominionTheme.InkRed : DominionTheme.Ink;
                DominionTheme.InkLabel(new Rect(6f, y + 4f, width - 12f, h - 4f), text, ink, GameFont.Tiny);
                y += h + 4f;
            }
            return y + 4f;
        }

        private void DrawTalkKey(Rect row, TalkTopic t, Faction f)
        {
            string reason = DiplomacyTalk.CannotTalkReason(f, t);
            bool down = pressedTalk == t && pressed == null && Time.realtimeSinceStartup - pressedAt < 0.25f;
            bool over = reason == null && Mouse.IsOver(row);
            if (over) Widgets.DrawBoxSolid(row, new Color(1f, 0.85f, 0.5f, 0.07f));
            Rect cap = new Rect(row.x + 2f, row.y + (down ? 6f : 3f), 24f, 24f);
            GUI.color = reason != null ? new Color(0.5f, 0.48f, 0.44f) : over ? Color.white : new Color(0.9f, 0.88f, 0.84f);
            GUI.DrawTexture(cap, DominionRooms.Knob);
            GUI.color = Color.white;
            float cost = DiplomacyTalk.InfluenceCost(t);
            string label = DiplomacyTalk.Label(t) + (cost > 0f ? "  ·  " + cost.ToString("F0") : "");
            Rect text = new Rect(row.x + 30f, row.y, row.width - 30f, 18f);
            DominionTheme.InkLabel(text, label.Truncate(text.width), reason != null ? new Color(0.6f, 0.55f, 0.48f) : DominionRooms.BrassLight, GameFont.Small);
            Rect info = new Rect(row.x + 30f, row.y + 18f, row.width - 30f, 15f);
            float? chance = reason == null ? DiplomacyTalk.Chance(f, t) : null;
            if (reason != null) DominionTheme.InkLabel(info, reason.Truncate(info.width), new Color(1f, 0.55f, 0.4f, 0.85f), GameFont.Tiny);
            else if (chance.HasValue)
            {
                Rect bar = new Rect(info.x, info.y + 2f, Mathf.Min(info.width - 36f, 90f), 10f);
                Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.5f));
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * chance.Value, bar.height), DominionPaper.OddsFill(chance.Value));
                DominionTheme.InkLabel(new Rect(bar.xMax + 4f, info.y - 1f, 40f, 16f), chance.Value.ToStringPercent(), new Color(0.95f, 0.9f, 0.8f), GameFont.Tiny);
                var sb = new System.Text.StringBuilder();
                DiplomacyTalk.Chance(f, t, null, sb);
                TooltipHandler.TipRegion(bar, "Dominion_Odds_Title".Translate(f.Name) + "\n" + sb + "Dominion_Odds_Total".Translate(chance.Value.ToStringPercent()));
            }
            else DominionTheme.InkLabel(info, (t == TalkTopic.ShareRumour ? "Dominion_Talk_PickTarget" : "Dominion_Talk_Sure").Translate(), new Color(0.8f, 0.75f, 0.65f), GameFont.Tiny);
            string tip = DiplomacyTalk.Desc(t) + "\n\n" + "Dominion_Talk_Cooldown".Translate(DominionSettings.PlayerCooldownDays(DiplomacyTalk.CooldownDays(t)))
                         + (reason != null ? "\n\n" + reason.Colorize(ColorLibrary.RedReadable) : "");
            TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width, 18f), tip);
            TooltipHandler.TipRegion(cap, tip);
            if (reason != null || !(Widgets.ButtonInvisible(cap) || Widgets.ButtonInvisible(text))) return;
            if (t == TalkTopic.ShareRumour)
            {
                Find.WindowStack.Add(new FloatMenu(DiplomacyTalk.RumourMenu(f, about => Say(f, t, about)), "Dominion_Talk_RumourTitle".Translate()));
                return;
            }
            Say(f, t, null);
        }

        private void Say(Faction f, TalkTopic t, Faction about)
        {
            pressed = null;
            pressedTalk = t;
            pressedAt = Time.realtimeSinceStartup;
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            lastTalk = DiplomacyTalk.Talk(f, t, about);
            lastTalkFaction = f;
        }

        private void DrawKey(Rect row, DiplomacyActionDef def, DiplomacyActionWorker worker, Faction f, WorldComponent_Dominion comp)
        {
            string reason = DiplomacyActions.CannotUseReason(def, f);
            bool down = pressed == def && Time.realtimeSinceStartup - pressedAt < 0.25f;
            bool over = reason == null && Mouse.IsOver(row);
            if (over) Widgets.DrawBoxSolid(row, new Color(1f, 0.85f, 0.5f, 0.07f));

            // The key itself: a brass cap on a lever that dips when pressed.
            Rect cap = new Rect(row.x + 2f, row.y + (down ? 6f : 2f), 30f, 30f);
            Widgets.DrawBoxSolid(new Rect(row.x + 14f, row.y + 18f, 30f, 4f), new Color(0.3f, 0.22f, 0.1f));
            GUI.color = reason != null ? new Color(0.5f, 0.48f, 0.44f) : over ? Color.white : new Color(0.9f, 0.88f, 0.84f);
            GUI.DrawTexture(cap, DominionRooms.Knob);
            GUI.color = Color.white;
            if (worker.ViaLetter) DominionTheme.DrawInk(new Rect(cap.x + 8f, cap.y + 9f, 14f, 12f), DominionTheme.CategoryIcon(EventCategory.Diplomacy), DominionRooms.BrassInk);

            string label = def.LabelCap.ToString() + (def.influenceCost > 0f ? "  ·  " + "Dominion_Dip_Cost".Translate(def.influenceCost.ToString("F0")) : "");
            Rect text = new Rect(row.x + 40f, row.y, row.width - 40f, 18f);
            DominionTheme.InkLabel(text, label.Truncate(text.width), reason != null ? new Color(0.6f, 0.55f, 0.48f) : DominionRooms.BrassLight, GameFont.Small, TextAnchor.UpperLeft);

            // Under the name: why not, or the odds.
            Rect info = new Rect(row.x + 40f, row.y + 19f, row.width - 40f, 15f);
            int until = comp.Player.CooldownUntil(def.defName, f.loadID);
            int now = Find.TickManager.TicksGame;
            if (reason != null)
                DominionTheme.InkLabel(info, reason.Truncate(info.width), new Color(1f, 0.55f, 0.4f, 0.85f), GameFont.Tiny);
            else
            {
                float? chance = worker.AcceptanceChance(f);
                if (chance.HasValue)
                {
                    Rect bar = new Rect(info.x, info.y + 2f, Mathf.Min(info.width, 150f), 11f);
                    Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.5f));
                    GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * chance.Value, bar.height), DominionPaper.OddsFill(chance.Value));
                    DominionTheme.InkLabel(new Rect(bar.xMax + 6f, info.y - 1f, info.width - bar.width - 6f, 16f), chance.Value.ToStringPercent(), new Color(0.95f, 0.9f, 0.8f), GameFont.Tiny);
                    string breakdown = worker.ChanceBreakdown(f);
                    TooltipHandler.TipRegion(bar, "Dominion_Odds_Title".Translate(f.Name) + "\n" + (breakdown.NullOrEmpty() ? "" : breakdown + "\n")
                                                  + "Dominion_Odds_Total".Translate(chance.Value.ToStringPercent()));
                }
            }
            // A cooldown drains along the bottom of the row.
            if (until > now && def.cooldownDays > 0)
            {
                float frac = Mathf.Clamp01((until - now) / (float)Mathf.Max(1, DominionSettings.PlayerCooldownTicks(def.cooldownDays)));
                Widgets.DrawBoxSolid(new Rect(row.x + 40f, row.yMax - 2f, (row.width - 40f) * frac, 2f), new Color(1f, 0.6f, 0.3f, 0.8f));
            }

            string outcomeKey = "Dominion_Outcome_" + def.defName;
            string tip = def.description + (outcomeKey.CanTranslate() ? "\n\n" + outcomeKey.Translate() : "")
                         + (worker.ViaLetter ? "\n\n" + "Dominion_Letter_Tip".Translate() : "") + (reason != null ? "\n\n" + reason.Colorize(ColorLibrary.RedReadable) : "");
            TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width, 18f), tip);
            TooltipHandler.TipRegion(cap, tip);
            if (reason != null) return;
            if (Widgets.ButtonInvisible(cap) || Widgets.ButtonInvisible(text))
            {
                pressed = def;
                pressedTalk = null;
                pressedAt = Time.realtimeSinceStartup;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                if (worker.ViaLetter) Find.WindowStack.Add(new Dialog_EnvoyLetter(def, f));
                else worker.Execute(f);
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
            // Secret moves against you stay secret until your intelligence uncovers them.
            if (IntelUtility.IntelOn(ev.primary) < IntelUtility.RequiredIntel(ev.secrecy)) return;
            int outcome = ev.def.category == EventCategory.War || ev.def.category == EventCategory.Espionage ? -1 : 0;
            comp.Player.Log(ev.primary, ev.Headline, outcome, byThem: true);
        }
    }
}
