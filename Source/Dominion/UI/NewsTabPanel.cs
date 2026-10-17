using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The news feed, printed as a colony newspaper (The Rimward Courier): a masthead with today's edition, clippings on
    /// the left and the full article on the right, with a byline, a dateline, rubber stamps for how sure we are, and a
    /// classified ad at the bottom because every paper needs filler.
    /// </summary>
    public class NewsTabPanel
    {
        private const float RowHeight = 54f;
        private const float FilterHeight = 28f;
        private const float MastheadHeight = 96f;
        private const float FrontPageNewsworthiness = 0.9f;

        private Vector2 listScroll;
        private Vector2 storyScroll;
        private NewsItem selected;

        private const float BatchBarHeight = 32f;
        private const float DayBarHeight = 26f;
        private const float ClippingW = 250f;
        private const float ClippingH = 150f;
        private const int FollowUpDays = 30;

        private enum View { Latest, Bookmarks, Archive, Scrapbook }

        private EventCategory? categoryFilter;
        private Faction factionFilter;
        private bool showRumors = true;
        private bool unreadOnly;
        private View view = View.Latest;
        private string search = "";
        private int archiveDay = -1;

        private readonly List<NewsItem> shown = new List<NewsItem>();

        /// <summary>Stories ticked in the list, for stamping several at once.</summary>
        private readonly HashSet<NewsItem> picked = new HashSet<NewsItem>();
        private Vector2 scrapScroll;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            NewsLog log = comp.News;
            DominionTheme.DrawPaper(rect);
            Rect inner = rect.ContractedBy(10f);

            DrawMasthead(new Rect(inner.x, inner.y, inner.width, MastheadHeight), comp);
            float y = inner.y + MastheadHeight + 4f;
            DrawFilters(new Rect(inner.x, y, inner.width, FilterHeight), log);
            y += FilterHeight + 8f;

            Rect body = new Rect(inner.x, y, inner.width, inner.yMax - y);
            Filter(log);
            if (view == View.Scrapbook)
            {
                DrawScrapbook(body, log);
                return;
            }
            Rect listRect = body.LeftPart(0.42f);
            Rect storyRect = body.RightPart(0.58f);
            storyRect.xMin += 10f;

            if (view == View.Archive)
            {
                DrawDayBar(new Rect(listRect.x, listRect.y, listRect.width, DayBarHeight), log);
                listRect.yMin += DayBarHeight + 4f;
            }
            picked.RemoveWhere(n => !log.Delivered.Contains(n));
            if (picked.Count > 0)
            {
                DrawBatchBar(new Rect(listRect.x, listRect.yMax - BatchBarHeight, listRect.width, BatchBarHeight), log);
                listRect.yMax -= BatchBarHeight + 4f;
            }
            DrawList(listRect, log);
            // A column rule between clippings and the article.
            GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.5f);
            Widgets.DrawLineVertical(storyRect.x - 5f, storyRect.y, storyRect.height);
            GUI.color = Color.white;

            if (selected != null)
                DrawStory(storyRect, selected, log);
            else
                DrawEmpty(storyRect, log);
        }

        // ------------------------------------------------------------------ masthead

        private static void DrawMasthead(Rect rect, WorldComponent_Dominion comp)
        {
            DrawRadioDesk(rect, comp);
            float titleH = rect.height - 30f;
            float titleW = Mathf.Min(rect.width, titleH * (1024f / 150f));
            DominionTheme.DrawInk(new Rect(rect.center.x - titleW / 2f, rect.y, titleW, titleH), DominionTheme.Masthead, DominionTheme.Ink);

            int edition = DominionTheme.Edition;
            string meta = "Dominion_News_EditionLine".Translate(edition, DominionTheme.TodayLong(), DominionTheme.WeatherNow(), DominionTheme.Price);
            DominionTheme.InkLabel(new Rect(rect.x, rect.y + titleH, rect.width, 16f), meta, DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.MiddleCenter);
            string motto = "“" + DominionTheme.Flavour("motto", edition) + "”";
            DominionTheme.InkLabel(new Rect(rect.x, rect.y + titleH + 14f, rect.width, 16f), motto, DominionTheme.InkRed, GameFont.Tiny, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// The radio desk in the masthead's corners: on the left what is queued and how good the reception is,
        /// on the right the stamp for a colony-wide advisory.
        /// </summary>
        private static void DrawRadioDesk(Rect rect, WorldComponent_Dominion comp)
        {
            const float StampW = 118f, StampH = 46f;
            RadioOrders orders = comp.RadioOrders;
            string cannot = RadioOrderUtility.CannotOrderReason();

            Rect left = new Rect(rect.x, rect.y + 6f, 170f, rect.height - 34f);
            DominionTheme.InkLabel(new Rect(left.x, left.y, left.width, 16f), "Dominion_Radio_DeskTitle".Translate().ToString().ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(left.x, left.y + 15f, left.width, 16f), "Dominion_Radio_Coverage".Translate(comp.radioCoverage.ToString("F0")), DominionTheme.InkFaded, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(left.x, left.y + 29f, left.width, 16f), "Dominion_Radio_OrdersQueued".Translate(orders.Orders.Count), DominionTheme.InkFaded, GameFont.Tiny);
            int upset = RadioOrderUtility.UpsetColonists.Value;
            if (upset > 0)
                DominionTheme.InkLabel(new Rect(left.x, left.y + 43f, left.width, 16f), "Dominion_Radio_Upset".Translate(upset), DominionTheme.InkRed, GameFont.Tiny);

            // The ADVISORY stamp opens the menu of colony advisories; below it, what is queued or in force.
            Rect stamp = new Rect(rect.xMax - StampW - 4f, rect.y + 10f, StampW, StampH);
            bool anyQueued = RadioOrderSpec.Advisories.Any(k => orders.Queued(k));
            if (DominionTheme.StampButton(stamp, anyQueued ? DominionTheme.StampQueued : DominionTheme.StampAdvisory,
                    anyQueued ? DominionTheme.InkAmber : DominionTheme.InkRed, anyQueued ? 4f : -4f,
                    "Dominion_Radio_AdvisoryMenu_Desc".Translate(upset), cannot, anyQueued))
                Find.WindowStack.Add(new FloatMenu(RadioOrderUtility.AdvisoryMenu()));
            DominionTheme.InkLabel(new Rect(stamp.x - 30f, stamp.yMax + 2f, stamp.width + 34f, 16f), AdvisoryStatus(orders), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
        }

        private static string AdvisoryStatus(RadioOrders orders)
        {
            int now = Find.TickManager.TicksGame;
            if (orders.AdvisoryInForce)
                return "Dominion_Radio_Status_CalmInForce".Translate((orders.advisoryUntilTick - now).ToStringTicksToPeriod());
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp.Broadcast.CounterProgrammingActive)
                return "Dominion_Radio_Status_CounterInForce".Translate((comp.Broadcast.counterProgrammingUntil - now).ToStringTicksToPeriod());
            return "Dominion_Radio_AdvisoryCaption".Translate();
        }

        // ------------------------------------------------------------------ filters

        private void DrawFilters(Rect rect, NewsLog log)
        {
            // Widths shrink together when the window is narrow.
            float[] widths = { 130f, 150f, 150f, 130f, 120f, 110f, 110f };
            float need = 0f;
            foreach (float w in widths) need += w + 6f;
            float k = Mathf.Min(1f, rect.width / need);
            float x = rect.x;
            Rect Next(int i)
            {
                Rect r = new Rect(x, rect.y, widths[i] * k, rect.height);
                x = r.xMax + 6f * k;
                return r;
            }

            Rect cat = Next(0);
            string catLabel = categoryFilter.HasValue ? ("Dominion_EventCategory_" + categoryFilter.Value).Translate() : "Dominion_News_AllCategories".Translate();
            TooltipHandler.TipRegion(cat, "Dominion_Tip_NewsCategory".Translate());
            if (Widgets.ButtonText(cat, catLabel.Truncate(cat.width - 6f)))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_News_AllCategories".Translate() + Unread(log, null), () => categoryFilter = null) };
                foreach (EventCategory c in System.Enum.GetValues(typeof(EventCategory)))
                {
                    EventCategory captured = c;
                    options.Add(new FloatMenuOption(("Dominion_EventCategory_" + c).Translate() + Unread(log, c), () => categoryFilter = captured,
                        DominionTheme.CategoryIcon(c), new Color(0.9f, 0.85f, 0.7f)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Rect fac = Next(1);
            TooltipHandler.TipRegion(fac, "Dominion_Tip_NewsFaction".Translate());
            if (Widgets.ButtonText(fac, (factionFilter?.Name ?? "Dominion_News_AllFactions".Translate().ToString()).Truncate(fac.width - 6f)))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_News_AllFactions".Translate(), () => factionFilter = null) };
                foreach (Faction f in Find.FactionManager.AllFactionsVisibleInViewOrder)
                {
                    Faction captured = f;
                    options.Add(new FloatMenuOption(f.Name, () => factionFilter = captured, f.def.FactionIcon, f.Color));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Rect find = Next(2);
            search = Widgets.TextField(find, search ?? "");
            if (search.NullOrEmpty())
                DominionTheme.InkLabel(new Rect(find.x + 6f, find.y, find.width, find.height), "Dominion_UI_Search".Translate(), DominionTheme.InkFaded, GameFont.Small, TextAnchor.MiddleLeft);
            TooltipHandler.TipRegion(find, "Dominion_News_Search_Desc".Translate());

            Rect mode = Next(3);
            TooltipHandler.TipRegion(mode, "Dominion_News_View_Desc".Translate());
            if (Widgets.ButtonText(mode, ViewLabel(view, log).Truncate(mode.width - 6f)))
            {
                var options = new List<FloatMenuOption>();
                foreach (View v in System.Enum.GetValues(typeof(View)))
                {
                    View captured = v;
                    options.Add(new FloatMenuOption(ViewLabel(v, log) + ": " + ("Dominion_News_View_" + v + "_Desc").Translate(), () => SetView(captured, log)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Rect rum = Next(4);
            TooltipHandler.TipRegion(rum, "Dominion_Tip_ShowRumors".Translate());
            InkCheckbox(new Rect(rum.x, rum.y + 2f, rum.width, 24f), "Dominion_News_ShowRumors".Translate(), ref showRumors);
            Rect unr = Next(5);
            TooltipHandler.TipRegion(unr, "Dominion_Tip_UnreadOnly".Translate());
            InkCheckbox(new Rect(unr.x, unr.y + 2f, unr.width, 24f), "Dominion_News_UnreadOnly".Translate(), ref unreadOnly);

            Rect all = new Rect(rect.xMax - widths[6] * k, rect.y, widths[6] * k, rect.height);
            TooltipHandler.TipRegion(all, "Dominion_Tip_MarkAllRead".Translate());
            if (Widgets.ButtonText(all, "Dominion_News_MarkAllRead".Translate().Truncate(all.width - 6f)))
                log.MarkAllRead();
        }

        private static string Unread(NewsLog log, EventCategory? c)
        {
            int n = 0;
            foreach (NewsItem item in log.Delivered)
                if (!item.read && (!c.HasValue || item.Category == c.Value)) n++;
            return n > 0 ? "  (" + "Dominion_News_UnreadCount".Translate(n) + ")" : "";
        }

        private static string ViewLabel(View v, NewsLog log)
        {
            int n = 0;
            if (v == View.Bookmarks) foreach (NewsItem i in log.Delivered) if (i.bookmarked) n++;
            if (v == View.Scrapbook) foreach (NewsItem i in log.Delivered) if (i.clipped) n++;
            return ("Dominion_News_View_" + v).Translate() + (n > 0 ? " (" + n + ")" : "");
        }

        private void SetView(View v, NewsLog log)
        {
            view = v;
            listScroll = Vector2.zero;
            if (v == View.Archive && archiveDay < 0) archiveDay = GenDate.DaysPassed;
        }

        private static float InkCheckbox(Rect r, string label, ref bool value)
        {
            Widgets.Checkbox(new Vector2(r.x, r.y), ref value, 24f);
            DominionTheme.InkLabel(new Rect(r.x + 28f, r.y, r.width - 28f, r.height), label, null, GameFont.Small, TextAnchor.MiddleLeft);
            if (Widgets.ButtonInvisible(r)) value = !value;
            return r.xMax;
        }

        /// <summary>Shows a story picked from the list, keeping the search, filters and view as they are.</summary>
        private void Select(NewsItem n, NewsLog log)
        {
            selected = n;
            storyScroll = Vector2.zero;
            log.MarkRead(n);
        }

        /// <summary>Shows a story in the article column and marks it read.</summary>
        public void Open(NewsItem n, NewsLog log)
        {
            if (n == null) return;
            selected = n;
            storyScroll = Vector2.zero;
            log.MarkRead(n);
            // Make sure the filters do not hide it straight away.
            categoryFilter = null;
            factionFilter = null;
            unreadOnly = false;
            search = "";
            if (view != View.Latest && !(view == View.Bookmarks && n.bookmarked)) view = View.Latest;
            if (n.confidence == NewsConfidence.Rumor) showRumors = true;
        }

        private void Filter(NewsLog log)
        {
            shown.Clear();
            IReadOnlyList<NewsItem> all = log.Delivered;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                NewsItem n = all[i];
                if (categoryFilter.HasValue && n.Category != categoryFilter.Value) continue;
                if (factionFilter != null && n.primary != factionFilter && n.secondary != factionFilter) continue;
                if (!showRumors && n.confidence == NewsConfidence.Rumor) continue;
                if (unreadOnly && n.read && n != selected) continue;
                if (view == View.Bookmarks && !n.bookmarked) continue;
                if (view == View.Scrapbook && !n.clipped) continue;
                if (view == View.Archive && n.arrivesTick / GenDate.TicksPerDay != archiveDay) continue;
                if (!search.NullOrEmpty() && !Matches(n, search)) continue;
                shown.Add(n);
            }
            // Bookmarked stories stay at the top of the latest news.
            if (view == View.Latest) shown.SortStable((a, b) => (b.bookmarked ? 1 : 0).CompareTo(a.bookmarked ? 1 : 0));
            if (selected != null && !shown.Contains(selected)) selected = null;
        }

        /// <summary>Every word of the search must appear somewhere in the story; spaces alone match everything.</summary>
        private static bool Matches(NewsItem n, string text)
        {
            foreach (string word in text.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
                if (!Contains(n.headline, word) && !Contains(n.story, word) && !Contains(n.primary?.Name, word) && !Contains(n.secondary?.Name, word))
                    return false;
            return true;
        }

        private static bool Contains(string s, string text) => s != null && s.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ archive, batch stamping, scrapbook

        /// <summary>The archive's day: flip back and forth through past editions.</summary>
        private void DrawDayBar(Rect rect, NewsLog log)
        {
            int first = log.Delivered.Count > 0 ? log.Delivered[0].arrivesTick / GenDate.TicksPerDay : GenDate.DaysPassed;
            int last = GenDate.DaysPassed;
            archiveDay = Mathf.Clamp(archiveDay, first, last);
            Rect prev = new Rect(rect.x, rect.y, 32f, rect.height), next = new Rect(rect.xMax - 32f, rect.y, 32f, rect.height);
            if (DominionPaper.InkButton(prev, "◀", "Dominion_News_PrevDay".Translate(), archiveDay <= first ? "Dominion_News_NoEarlier".Translate().ToString() : null))
                archiveDay--;
            if (DominionPaper.InkButton(next, "▶", "Dominion_News_NextDay".Translate(), archiveDay >= last ? "Dominion_News_NoLater".Translate().ToString() : null))
                archiveDay++;
            string label = "Dominion_News_EditionOf".Translate(archiveDay + 1, DominionPaper.Date(archiveDay * GenDate.TicksPerDay + GenDate.TicksPerHour * 12));
            DominionTheme.InkLabel(new Rect(prev.xMax, rect.y, next.x - prev.xMax, rect.height), label, DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleCenter);
        }

        /// <summary>The bar under the list when stories are ticked: stamp them all at once, or mark, bookmark or clip them.</summary>
        private void DrawBatchBar(Rect rect, NewsLog log)
        {
            Widgets.DrawBoxSolid(rect, DominionTheme.PaperShade);
            GUI.color = DominionPaper.Edge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            Rect inner = rect.ContractedBy(3f);
            float w = (inner.width - 90f) / 4f;
            DominionTheme.InkLabel(new Rect(inner.x, inner.y, 88f, inner.height), "Dominion_News_Picked".Translate(picked.Count), DominionTheme.InkRed, GameFont.Tiny, TextAnchor.MiddleLeft);
            float x = inner.x + 90f;
            if (DominionPaper.InkButton(new Rect(x, inner.y, w - 3f, inner.height), "Dominion_News_BatchRespond".Translate(), "Dominion_News_BatchRespond_Desc".Translate(RadioOrders.Capacity),
                    RadioOrderUtility.CannotOrderReason(), false, DominionTheme.InkRed))
                BatchMenu();
            x += w;
            if (DominionPaper.InkButton(new Rect(x, inner.y, w - 3f, inner.height), "Dominion_News_BatchRead".Translate(), "Dominion_News_BatchRead_Desc".Translate()))
                foreach (NewsItem n in picked) log.MarkRead(n);
            x += w;
            bool allMarked = picked.All(n => n.bookmarked);
            if (DominionPaper.InkButton(new Rect(x, inner.y, w - 3f, inner.height), allMarked ? "Dominion_News_Unbookmark".Translate() : "Dominion_News_Bookmark".Translate(),
                    "Dominion_News_Bookmark_Desc".Translate()))
                foreach (NewsItem n in picked) n.bookmarked = !allMarked;
            x += w;
            if (DominionPaper.InkButton(new Rect(x, inner.y, w - 3f, inner.height), "Dominion_News_BatchClear".Translate(), "Dominion_News_BatchClear_Desc".Translate()))
                picked.Clear();
        }

        private void BatchMenu()
        {
            RadioOrders orders = WorldComponent_Dominion.Instance.RadioOrders;
            var options = new List<FloatMenuOption>();
            foreach (RadioOrderKind kind in RadioOrderSpec.StoryResponses)
            {
                List<NewsItem> eligible = picked.Where(n => RadioOrderUtility.CannotRespondReason(kind, n) == null).ToList();
                RadioOrderSpec spec = RadioOrderSpec.Of(kind);
                string label = "Dominion_News_BatchOption".Translate(spec.Label, eligible.Count, picked.Count);
                if (eligible.Count == 0)
                {
                    options.Add(new FloatMenuOption(label + " (" + "Dominion_News_BatchNone".Translate() + ")", null, spec.icon, Color.white));
                    continue;
                }
                RadioOrderKind captured = kind;
                options.Add(new FloatMenuOption(label, () =>
                {
                    int room = orders.Free, queued = 0;
                    foreach (NewsItem n in eligible)
                    {
                        if (queued >= room) break;
                        orders.Queue(captured, n.id);
                        queued++;
                    }
                    int left = eligible.Count - queued;
                    Messages.Message(left > 0 ? "Dominion_News_BatchQueuedFull".Translate(queued, spec.Label, left) : "Dominion_News_BatchQueued".Translate(queued, spec.Label),
                        left > 0 ? MessageTypeDefOf.CautionInput : MessageTypeDefOf.TaskCompletion, false);
                    picked.Clear();
                }, spec.icon, Color.white) { tooltip = spec.Desc });
            }
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_News_BatchTitle".Translate(orders.Free)));
        }

        /// <summary>The scrapbook: clipped stories pasted in at slight angles. Click one to read it in full.</summary>
        private void DrawScrapbook(Rect rect, NewsLog log)
        {
            if (shown.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(rect.x, rect.y + 40f, rect.width, 60f), "Dominion_News_ScrapbookEmpty".Translate(), DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
                return;
            }
            int perRow = Mathf.Max(1, (int)((rect.width - 16f) / (ClippingW + 16f)));
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.CeilToInt(shown.Count / (float)perRow) * (ClippingH + 20f) + 10f);
            Widgets.BeginScrollView(rect, ref scrapScroll, viewRect);
            for (int i = 0; i < shown.Count; i++)
            {
                NewsItem n = shown[i];
                Rect card = new Rect(10f + i % perRow * (ClippingW + 16f), 10f + i / perRow * (ClippingH + 20f), ClippingW, ClippingH);
                float angle = (n.id * 37 % 7) - 3f;
                Matrix4x4 m = GUI.matrix;
                UI.RotateAroundPivot(angle, card.center);
                Widgets.DrawBoxSolid(new Rect(card.x + 3f, card.y + 3f, card.width, card.height), new Color(0f, 0f, 0f, 0.18f));
                DominionTheme.DrawPaper(card);
                Rect inner = card.ContractedBy(8f);
                Text.Font = GameFont.Small;
                float hh = Mathf.Min(44f, Text.CalcHeight(n.headline, inner.width));
                DominionTheme.InkLabel(new Rect(inner.x, inner.y, inner.width, hh), n.headline);
                string snippet = (n.story ?? "").Length > 220 ? n.story.Substring(0, 220) + "…" : n.story ?? "";
                DominionTheme.InkLabel(new Rect(inner.x, inner.y + hh + 2f, inner.width, inner.height - hh - 20f), snippet, DominionTheme.InkFaded, GameFont.Tiny);
                DominionTheme.InkLabel(new Rect(inner.x, inner.yMax - 16f, inner.width, 16f), DominionPaper.Date(n.arrivesTick), DominionTheme.InkRed, GameFont.Tiny);
                GUI.matrix = m;
                Rect unclip = new Rect(card.xMax - 24f, card.y + 2f, 20f, 20f);
                DominionTheme.DrawInk(unclip, DominionPaper.Scissors, Mouse.IsOver(unclip) ? DominionTheme.InkRed : DominionTheme.InkFaded);
                TooltipHandler.TipRegion(unclip, "Dominion_News_Unclip".Translate());
                if (Widgets.ButtonInvisible(unclip))
                {
                    n.clipped = false;
                    continue;
                }
                TooltipHandler.TipRegion(card, n.headline + "\n\n" + "Dominion_News_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(card))
                {
                    view = View.Latest;
                    Open(n, log);
                }
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ clippings

        private void DrawList(Rect rect, NewsLog log)
        {
            Rect view = new Rect(0f, 0f, rect.width - 16f, shown.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref listScroll, view);
            int first = Mathf.Max(0, (int)(listScroll.y / RowHeight));
            int last = Mathf.Min(shown.Count - 1, first + (int)(rect.height / RowHeight) + 1);
            int now = Find.TickManager.TicksGame;
            for (int i = first; i <= last; i++)
            {
                NewsItem n = shown[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight - 4f);
                if (n == selected) Widgets.DrawBoxSolid(row, DominionTheme.PaperHighlight);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, DominionTheme.PaperShade);
                // Dashed-looking cut line under each clipping.
                GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.25f);
                Widgets.DrawLineHorizontal(row.x + 4f, row.yMax + 1f, row.width - 8f);
                GUI.color = Color.white;

                // A tick box for stamping several stories at once.
                Rect tick = new Rect(row.x + 3f, row.y + 16f, 16f, 16f);
                bool on = picked.Contains(n);
                GUI.color = DominionTheme.InkFaded;
                Widgets.DrawBox(tick, 1);
                GUI.color = Color.white;
                if (on) Widgets.DrawBoxSolid(tick.ContractedBy(3f), DominionTheme.InkRed);
                TooltipHandler.TipRegion(tick, "Dominion_News_Pick".Translate());
                if (Widgets.ButtonInvisible(tick.ExpandedBy(3f)))
                {
                    if (on) picked.Remove(n);
                    else picked.Add(n);
                }

                Rect icon = new Rect(row.x + 24f, row.y + 8f, 26f, 26f);
                DominionTheme.DrawInk(icon, DominionTheme.CategoryIcon(n.Category), n.read ? DominionTheme.InkFaded : DominionTheme.Ink);

                float right = row.xMax - 4f;
                if (n.bookmarked)
                {
                    DominionTheme.DrawInk(new Rect(right - 18f, row.y + 5f, 18f, 18f), DominionPaper.Star, DominionTheme.InkAmber);
                    right -= 20f;
                }
                if (!n.read)
                {
                    GUI.DrawTexture(new Rect(right - 20f, row.y + 4f, 20f, 20f), DominionTheme.Pin);
                    right -= 22f;
                }
                if (n.def != null && n.def.newsworthiness >= FrontPageNewsworthiness && n.confidence == NewsConfidence.Confirmed)
                {
                    DominionTheme.InkLabel(new Rect(right - 74f, row.y + 2f, 74f, 16f), "Dominion_News_FrontPageTag".Translate(),
                        DominionTheme.InkRed, GameFont.Tiny, TextAnchor.UpperRight);
                    right -= 78f; // the headline stops short of the tag
                }

                Rect text = new Rect(icon.xMax + 8f, row.y + 3f, right - icon.xMax - 10f, 24f);
                DominionTheme.InkLabel(text, n.headline.Truncate(text.width), n.read ? DominionTheme.InkFaded : DominionTheme.Ink);
                DominionTheme.InkLabel(new Rect(text.x, text.yMax, row.xMax - text.x - 4f, 18f), Meta(n, now).Truncate(row.xMax - text.x - 4f),
                    DominionTheme.InkFaded, GameFont.Tiny);
                TooltipHandler.TipRegion(row, n.headline + "\n\n" + "Dominion_Tip_Clipping".Translate());

                if (Widgets.ButtonInvisible(new Rect(row.x + 22f, row.y, row.width - 22f, row.height)))
                    Select(n, log);
            }
            Widgets.EndScrollView();
        }

        private static string Meta(NewsItem n, int now) =>
            "Dominion_News_Ago".Translate((now - n.arrivesTick).ToStringTicksToPeriod(allowSeconds: false)) + "  ·  "
            + Desk(n.Category) + "  ·  " + NewsNotifier.ConfidenceLabel(n.confidence);

        private static string Desk(EventCategory c) => ("Dominion_NewsDesk_" + c).Translate();

        // ------------------------------------------------------------------ article

        private void DrawStory(Rect rect, NewsItem n, NewsLog log)
        {
            Rect view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, storyHeight));
            Widgets.BeginScrollView(rect, ref storyScroll, view);
            float w = view.width;
            float y = 0f;

            // Kicker: which desk filed it.
            DominionTheme.DrawInk(new Rect(0f, y, 18f, 18f), DominionTheme.CategoryIcon(n.Category), DominionTheme.InkRed);
            DominionTheme.InkLabel(new Rect(22f, y, w - 200f, 18f), Desk(n.Category).ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            Rect star = new Rect(w - 176f, y, 18f, 18f), clip = new Rect(w - 154f, y, 18f, 18f);
            DominionTheme.DrawInk(star, n.bookmarked ? DominionPaper.Star : DominionPaper.StarOutline, n.bookmarked ? DominionTheme.InkAmber : DominionTheme.InkFaded);
            TooltipHandler.TipRegion(star, n.bookmarked ? "Dominion_News_Unbookmark".Translate() : "Dominion_News_Bookmark_Desc".Translate());
            if (Widgets.ButtonInvisible(star)) n.bookmarked = !n.bookmarked;
            DominionTheme.DrawInk(clip, DominionPaper.Scissors, n.clipped ? DominionTheme.InkRed : DominionTheme.InkFaded);
            TooltipHandler.TipRegion(clip, n.clipped ? "Dominion_News_Unclip".Translate() : "Dominion_News_Clip_Desc".Translate());
            if (Widgets.ButtonInvisible(clip)) n.clipped = !n.clipped;
            y += 20f;

            // The stamp sits in the top-right corner, a little crooked.
            Texture2D stamp = StampFor(n, out Color stampColor);
            float headW = w - (stamp != null ? 130f : 0f);
            if (stamp != null)
                DominionTheme.DrawStamp(new Rect(w - 128f, 0f, 124f, 52f), stamp, stampColor, (n.id % 7) - 9f);

            Text.Font = GameFont.Medium;
            float headH = Text.CalcHeight(n.headline, headW);
            Text.Font = GameFont.Small;
            DominionTheme.InkLabel(new Rect(0f, y, headW, headH), n.headline, null, GameFont.Medium);
            y += Mathf.Max(headH, stamp != null ? 36f : 0f) + 4f;

            // Byline and dateline.
            string reporter = DominionTheme.Flavour("reporter", n.id * 7919 + 13);
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f), ("Dominion_News_Byline_" + n.source).Translate(reporter), DominionTheme.InkFaded, GameFont.Tiny);
            y += 18f;
            string when = GenDate.DateFullStringAt(GenDate.TickGameToAbs(n.arrivesTick), Find.WorldGrid.LongLatOf(Find.CurrentMap?.Tile ?? 0));
            Rect dateline = new Rect(0f, y, w, 18f);
            DominionTheme.InkLabel(dateline, "Dominion_News_Dateline".Translate(Place(n), when), DominionTheme.InkFaded, GameFont.Tiny);
            RimWorld.Planet.Settlement place = PlaceSettlement(n);
            if (place != null)
            {
                TooltipHandler.TipRegion(dateline, "Dominion_News_JumpPlace".Translate(place.Label));
                if (Widgets.ButtonInvisible(dateline)) CameraJumper.TryJumpAndSelect(place);
            }
            y += 22f;

            // Who it is about.
            float x = 0f;
            foreach (Faction f in new[] { n.primary, n.secondary })
            {
                if (f == null) continue;
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(x, y, 22f, 22f), f);
                float room = w - (x + 26f);
                if (room < 40f) break;
                Text.Font = GameFont.Small;
                float nameW = Mathf.Min(Text.CalcSize(f.Name).x, room - 4f);
                Rect nameRect = new Rect(x + 26f, y + 1f, nameW + 4f, 22f);
                bool over = Mouse.IsOver(nameRect);
                DominionTheme.InkLabel(nameRect, f.Name.Truncate(nameW + 4f), over ? DominionTheme.InkRed : DominionTheme.Ink);
                if (over) Widgets.DrawLineHorizontal(nameRect.x, nameRect.yMax - 3f, nameW, DominionTheme.InkRed);
                if (!f.IsPlayer)
                {
                    TooltipHandler.TipRegion(nameRect, "Dominion_News_FactionLink".Translate(f.Name));
                    if (Widgets.ButtonInvisible(nameRect))
                    {
                        Faction captured = f;
                        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                        {
                            new FloatMenuOption("Dominion_UI_Jump_Dossier".Translate(), () => DominionUIState.Show(DominionTab.Overview, captured)),
                            new FloatMenuOption("Dominion_UI_Jump_Diplomacy".Translate(), () => DominionUIState.Show(DominionTab.Diplomacy, captured)),
                            new FloatMenuOption("Dominion_UI_Jump_Intel".Translate(), () => DominionUIState.Show(DominionTab.Intelligence, captured)),
                            new FloatMenuOption("Dominion_UI_Jump_Map".Translate(), () => DominionUIState.JumpToCapital(captured)),
                            new FloatMenuOption("Dominion_News_OnlyThisFaction".Translate(), () => factionFilter = captured),
                        }));
                    }
                }
                x += 26f + nameW + 18f;
            }
            if (x > 0f) y += 26f;
            DominionTheme.DrawRule(new Rect(w * 0.15f, y, w * 0.7f, 14f));
            y += 18f;

            // The story itself.
            string body = n.story ?? "";
            Text.Font = GameFont.Small;
            float bodyH = Text.CalcHeight(body, w);
            DominionTheme.InkLabel(new Rect(0f, y, w, bodyH), body);
            y += bodyH + 6f;
            if (n.corrected)
            {
                string note = "Dominion_News_LaterFalse".Translate();
                float h = Text.CalcHeight(note, w);
                DominionTheme.InkLabel(new Rect(0f, y, w, h), note, DominionTheme.InkRed);
                y += h + 4f;
            }
            if (n.correctsId >= 0)
            {
                NewsItem original = log.FindDelivered(n.correctsId);
                if (original != null)
                {
                    string note = "Dominion_News_Corrects".Translate(original.headline);
                    float h = Text.CalcHeight(note, w);
                    DominionTheme.InkLabel(new Rect(0f, y, w, h), note, DominionTheme.InkFaded);
                    y += h + 4f;
                }
            }
            y = DrawAdvice(n, y, w);
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f),
                "Dominion_News_LetterFooter".Translate(NewsText.SourceLabel(n.source), NewsNotifier.ConfidenceLabel(n.confidence)),
                DominionTheme.InkFaded, GameFont.Tiny);
            y += 26f;
            y = DrawInvestigation(n, y, w);
            y = DrawFollowUps(n, log, y, w);

            // Filler: every paper has a classified ad at the bottom of the page.
            y = Mathf.Max(y, rect.height - 78f);
            Rect ad = new Rect(w * 0.08f, y, w * 0.84f, 64f);
            GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.6f);
            Widgets.DrawBox(ad);
            GUI.color = Color.white;
            DominionTheme.InkLabel(new Rect(ad.x, ad.y + 3f, ad.width, 16f), "Dominion_News_Classifieds".Translate(), DominionTheme.InkRed, GameFont.Tiny, TextAnchor.UpperCenter);
            DominionTheme.InkLabel(new Rect(ad.x + 8f, ad.y + 19f, ad.width - 16f, ad.height - 21f), DominionTheme.Flavour("classified", n.id * 104729 + 7),
                DominionTheme.Ink, GameFont.Tiny, TextAnchor.UpperCenter);
            y = ad.yMax + 4f;

            storyHeight = y;
            Widgets.EndScrollView();
        }

        private float storyHeight;

        /// <summary>Other stories about the same factions from around the same time: the thread of the story.</summary>
        private float DrawFollowUps(NewsItem n, NewsLog log, float y, float w)
        {
            if (n.primary == null) return y;
            var related = new List<NewsItem>();
            IReadOnlyList<NewsItem> all = log.Delivered;
            int window = FollowUpDays * GenDate.TicksPerDay;
            for (int i = all.Count - 1; i >= 0 && related.Count < 5; i--)
            {
                NewsItem o = all[i];
                if (o == n || Mathf.Abs(o.occurredTick - n.occurredTick) > window) continue;
                bool samePair = n.secondary != null
                    ? (o.primary == n.primary && o.secondary == n.secondary) || (o.primary == n.secondary && o.secondary == n.primary)
                    : o.primary == n.primary && o.secondary == null;
                if (samePair) related.Add(o);
            }
            if (related.Count == 0) return y;
            DominionPaper.Caption(new Rect(0f, y, w, 16f), "Dominion_News_FollowUps".Translate());
            y += 16f;
            int now = Find.TickManager.TicksGame;
            foreach (NewsItem o in related)
            {
                Rect r = new Rect(8f, y, w - 8f, 20f);
                bool over = Mouse.IsOver(r);
                if (over) Widgets.DrawBoxSolid(r, DominionTheme.PaperShade);
                DominionTheme.InkLabel(r, ("› " + o.headline).Truncate(w - 110f), over ? DominionTheme.InkRed : DominionTheme.Ink, GameFont.Tiny);
                DominionTheme.InkLabel(new Rect(r.xMax - 100f, r.y, 100f, r.height), "Dominion_News_Ago".Translate((now - o.arrivesTick).ToStringTicksToPeriod(allowSeconds: false)),
                    DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperRight);
                TooltipHandler.TipRegion(r, o.headline + "\n\n" + "Dominion_News_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(r)) Open(o, log);
                y += 20f;
            }
            return y + 8f;
        }

        /// <summary>A boxed "what it means for us" panel, like a sidebar in a real paper.</summary>
        private static float DrawAdvice(NewsItem n, float y, float w)
        {
            if (n.advice.NullOrEmpty()) return y;
            Text.Font = GameFont.Small;
            float textH = Text.CalcHeight(n.advice, w - 24f);
            Rect box = new Rect(4f, y + 4f, w - 8f, textH + 30f);
            Widgets.DrawBoxSolid(box, DominionTheme.PaperShade);
            GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.7f);
            Widgets.DrawBox(box);
            GUI.color = Color.white;
            DominionTheme.InkLabel(new Rect(box.x + 10f, box.y + 5f, box.width - 20f, 18f),
                "Dominion_News_AdviceTitle".Translate(NewsFlavor.ColonyName()).ToString().ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(box.x + 10f, box.y + 23f, box.width - 20f, textH), n.advice);
            return box.yMax + 10f;
        }

        /// <summary>
        /// The RESPOND stamp: a menu of what the radio desk can do about this story (investigate, debunk, spread the
        /// word, keep it off the air). While an order for it is queued, the stamp shows that instead.
        /// </summary>
        private static float DrawInvestigation(NewsItem n, float y, float w)
        {
            RadioOrder order = WorldComponent_Dominion.Instance.RadioOrders.For(n);
            Rect stamp = new Rect(4f, y, 128f, 46f);
            string note;
            if (order != null)
            {
                Texture2D tex = order.kind == RadioOrderKind.Investigate ? DominionTheme.StampInquiry : DominionTheme.StampQueued;
                if (DominionTheme.StampButton(stamp, tex, DominionTheme.InkAmber, 3f, order.Spec.Desc + "\n\n" + "Dominion_Radio_ClickToCancel".Translate(), null, true))
                    Find.WindowStack.Add(new FloatMenu(RadioOrderUtility.StoryMenu(n)));
                note = "Dominion_Radio_OrderNote".Translate(order.Spec.Label, (order.progress / order.WorkNeeded).ToStringPercent());
            }
            else
            {
                if (DominionTheme.StampButton(stamp, DominionTheme.StampRespond, DominionTheme.InkRed, -3f, "Dominion_Radio_RespondMenu_Desc".Translate(), RadioOrderUtility.CannotOrderReason()))
                    Find.WindowStack.Add(new FloatMenu(RadioOrderUtility.StoryMenu(n)));
                string options = string.Join(", ", RadioOrderSpec.StoryResponses
                    .Where(k => RadioOrderUtility.CannotRespondReason(k, n) == null).Select(k => RadioOrderSpec.Of(k).Label.ToLower()));
                note = options.NullOrEmpty() ? "Dominion_Radio_RespondNoneNote".Translate().ToString() : "Dominion_Radio_RespondNote".Translate(options).ToString();
            }
            DominionTheme.InkLabel(new Rect(stamp.xMax + 10f, y + 4f, w - stamp.xMax - 12f, 40f), note, DominionTheme.InkFaded, GameFont.Tiny);
            return y + 54f;
        }

        private static Texture2D StampFor(NewsItem n, out Color color)
        {
            if (n.corrected)
            {
                color = DominionTheme.InkRed;
                return DominionTheme.StampDisproved;
            }
            if (n.source == NewsSource.IntelReport && n.secrecy >= Secrecy.Secret)
            {
                color = DominionTheme.InkRed;
                return DominionTheme.StampClassified;
            }
            if (n.confidence == NewsConfidence.Confirmed && n.investigated)
            {
                color = DominionTheme.InkGreen;
                return DominionTheme.StampVerified;
            }
            if (n.confidence == NewsConfidence.Confirmed)
            {
                if (n.def != null && n.def.newsworthiness >= FrontPageNewsworthiness)
                {
                    color = DominionTheme.InkRed;
                    return DominionTheme.StampFrontPage;
                }
                color = DominionTheme.InkGreen;
                return DominionTheme.StampConfirmed;
            }
            color = DominionTheme.InkAmber;
            return DominionTheme.StampRumor;
        }

        private static RimWorld.Planet.Settlement PlaceSettlement(NewsItem n)
        {
            if (n.tile >= 0)
            {
                RimWorld.Planet.Settlement s = Find.WorldObjects.SettlementAt(new RimWorld.Planet.PlanetTile(n.tile));
                if (s != null) return s;
            }
            return n.primary != null ? DominionUIState.Capital(n.primary) : null;
        }

        /// <summary>Where the story is datelined: the settlement it happened at, else the main faction's capital.</summary>
        private static string Place(NewsItem n)
        {
            if (n.tile >= 0)
            {
                RimWorld.Planet.Settlement s = Find.WorldObjects.SettlementAt(new RimWorld.Planet.PlanetTile(n.tile));
                if (s != null) return s.Label.ToUpper();
            }
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (n.primary != null && comp.TryGetState(n.primary, out FactionState fs) && fs.capital != null && !fs.capital.Destroyed)
                return fs.capital.Label.ToUpper();
            return "Dominion_News_PlaceRim".Translate().ToString().ToUpper();
        }

        private void DrawEmpty(Rect rect, NewsLog log)
        {
            string text = shown.Count == 0
                ? (log.Delivered.Count > 0 ? "Dominion_News_NoMatch" : "Dominion_News_Empty").Translate()
                : "Dominion_News_SelectReport".Translate();
            DominionTheme.DrawInk(new Rect(rect.center.x - 48f, rect.y + rect.height * 0.25f, 96f, 96f), DominionTheme.Seal, new Color(1f, 1f, 1f, 0.35f));
            DominionTheme.InkLabel(new Rect(rect.x + 20f, rect.y + rect.height * 0.25f + 104f, rect.width - 40f, 80f), text, DominionTheme.InkFaded,
                GameFont.Small, TextAnchor.UpperCenter);
            DominionTheme.InkLabel(new Rect(rect.x + 20f, rect.yMax - 40f, rect.width - 40f, 30f), "Dominion_News_Static".Translate(), DominionTheme.InkFaded,
                GameFont.Tiny, TextAnchor.LowerCenter);
        }
    }
}
