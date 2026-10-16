using System.Collections.Generic;
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
        private const float StopPressNewsworthiness = 0.9f;

        private Vector2 listScroll;
        private Vector2 storyScroll;
        private NewsItem selected;

        private EventCategory? categoryFilter;
        private Faction factionFilter;
        private bool showRumors = true;
        private bool unreadOnly;

        private readonly List<NewsItem> shown = new List<NewsItem>();

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
            Rect listRect = body.LeftPart(0.42f);
            Rect storyRect = body.RightPart(0.58f);
            storyRect.xMin += 10f;

            Filter(log);
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
            int upset = RadioOrderUtility.ColonistsWithBadNews();
            if (upset > 0)
                DominionTheme.InkLabel(new Rect(left.x, left.y + 43f, left.width, 16f), "Dominion_Radio_Upset".Translate(upset), DominionTheme.InkRed, GameFont.Tiny);

            Rect stamp = new Rect(rect.xMax - StampW - 4f, rect.y + 10f, StampW, StampH);
            string tip = "Dominion_Radio_IssueAdvisory_Desc".Translate(RadioOrders.AdvisoryDays, upset);
            if (orders.AdvisoryQueued)
            {
                if (DominionTheme.StampButton(stamp, DominionTheme.StampQueued, DominionTheme.InkAmber, 4f, tip + "\n\n" + "Dominion_Radio_ClickToCancel".Translate(), null, true))
                    RadioOrderUtility.OrderAdvisory();
            }
            else if (orders.AdvisoryInForce)
            {
                string left2 = (orders.advisoryUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
                DominionTheme.DrawStamp(stamp, DominionTheme.StampInForce, DominionTheme.InkGreen, -5f);
                TooltipHandler.TipRegion(stamp, "Dominion_Radio_AdvisoryInForce".Translate(left2));
            }
            else if (DominionTheme.StampButton(stamp, DominionTheme.StampAdvisory, DominionTheme.InkRed, -4f, tip, cannot))
                RadioOrderUtility.OrderAdvisory();
            DominionTheme.InkLabel(new Rect(stamp.x - 20f, stamp.yMax + 2f, stamp.width + 24f, 16f), "Dominion_Radio_AdvisoryCaption".Translate(), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
        }

        // ------------------------------------------------------------------ filters

        private void DrawFilters(Rect rect, NewsLog log)
        {
            float x = rect.x;
            Rect r = new Rect(x, rect.y, 160f, rect.height);
            string catLabel = categoryFilter.HasValue ? ("Dominion_EventCategory_" + categoryFilter.Value).Translate() : "Dominion_News_AllCategories".Translate();
            if (Widgets.ButtonText(r, catLabel))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_News_AllCategories".Translate(), () => categoryFilter = null) };
                foreach (EventCategory c in System.Enum.GetValues(typeof(EventCategory)))
                {
                    EventCategory captured = c;
                    options.Add(new FloatMenuOption(("Dominion_EventCategory_" + c).Translate(), () => categoryFilter = captured,
                        DominionTheme.CategoryIcon(c), new Color(0.9f, 0.85f, 0.7f)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            x = r.xMax + 6f;

            r = new Rect(x, rect.y, 190f, rect.height);
            if (Widgets.ButtonText(r, factionFilter?.Name.Truncate(180f) ?? "Dominion_News_AllFactions".Translate()))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_News_AllFactions".Translate(), () => factionFilter = null) };
                foreach (Faction f in Find.FactionManager.AllFactionsVisibleInViewOrder)
                {
                    Faction captured = f;
                    options.Add(new FloatMenuOption(f.Name, () => factionFilter = captured, f.def.FactionIcon, f.Color));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            x = r.xMax + 12f;

            // Checkboxes: ink labels on paper.
            x = InkCheckbox(new Rect(x, rect.y + 2f, 140f, 24f), "Dominion_News_ShowRumors".Translate(), ref showRumors) + 10f;
            InkCheckbox(new Rect(x, rect.y + 2f, 130f, 24f), "Dominion_News_UnreadOnly".Translate(), ref unreadOnly);

            r = new Rect(rect.xMax - 140f, rect.y, 140f, rect.height);
            if (Widgets.ButtonText(r, "Dominion_News_MarkAllRead".Translate()))
                log.MarkAllRead();
        }

        private static float InkCheckbox(Rect r, string label, ref bool value)
        {
            Widgets.Checkbox(new Vector2(r.x, r.y), ref value, 24f);
            DominionTheme.InkLabel(new Rect(r.x + 28f, r.y, r.width - 28f, r.height), label, null, GameFont.Small, TextAnchor.MiddleLeft);
            if (Widgets.ButtonInvisible(r)) value = !value;
            return r.xMax;
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
                shown.Add(n);
            }
            if (selected != null && !shown.Contains(selected)) selected = null;
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

                Rect icon = new Rect(row.x + 4f, row.y + 8f, 26f, 26f);
                DominionTheme.DrawInk(icon, DominionTheme.CategoryIcon(n.Category), n.read ? DominionTheme.InkFaded : DominionTheme.Ink);

                float right = row.xMax - 4f;
                if (!n.read)
                {
                    GUI.DrawTexture(new Rect(right - 20f, row.y + 4f, 20f, 20f), DominionTheme.Pin);
                    right -= 22f;
                }
                if (n.def != null && n.def.newsworthiness >= StopPressNewsworthiness && n.confidence == NewsConfidence.Confirmed)
                {
                    DominionTheme.InkLabel(new Rect(right - 74f, row.y + 2f, 74f, 16f), "Dominion_News_StopPressTag".Translate(),
                        DominionTheme.InkRed, GameFont.Tiny, TextAnchor.UpperRight);
                    right -= 78f; // the headline stops short of the tag
                }

                Rect text = new Rect(icon.xMax + 8f, row.y + 3f, right - icon.xMax - 10f, 24f);
                DominionTheme.InkLabel(text, n.headline.Truncate(text.width), n.read ? DominionTheme.InkFaded : DominionTheme.Ink);
                DominionTheme.InkLabel(new Rect(text.x, text.yMax, row.xMax - text.x - 4f, 18f), Meta(n, now).Truncate(row.xMax - text.x - 4f),
                    DominionTheme.InkFaded, GameFont.Tiny);
                TooltipHandler.TipRegion(row, n.headline);

                if (Widgets.ButtonInvisible(row))
                {
                    selected = n;
                    storyScroll = Vector2.zero;
                    log.MarkRead(n);
                }
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
            DominionTheme.InkLabel(new Rect(22f, y, w - 150f, 18f), Desk(n.Category).ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
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
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f), "Dominion_News_Dateline".Translate(Place(n), when), DominionTheme.InkFaded, GameFont.Tiny);
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
                DominionTheme.InkLabel(new Rect(x + 26f, y + 1f, nameW + 4f, 22f), f.Name.Truncate(nameW + 4f));
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
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f),
                "Dominion_News_LetterFooter".Translate(NewsText.SourceLabel(n.source), NewsNotifier.ConfidenceLabel(n.confidence)),
                DominionTheme.InkFaded, GameFont.Tiny);
            y += 26f;
            y = DrawInvestigation(n, y, w);

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

        /// <summary>For unverified rumours: a stamp to send the story to the radio desk for checking.</summary>
        private static float DrawInvestigation(NewsItem n, float y, float w)
        {
            if (!RadioOrderUtility.CanInvestigate(n)) return y;
            RadioOrders orders = WorldComponent_Dominion.Instance.RadioOrders;
            Rect stamp = new Rect(4f, y, 128f, 46f);
            bool queued = orders.Investigating(n);
            string cannot = queued ? null : RadioOrderUtility.CannotOrderReason();
            string tip = "Dominion_Radio_Investigate_Desc".Translate();
            if (queued)
            {
                if (DominionTheme.StampButton(stamp, DominionTheme.StampInquiry, DominionTheme.InkAmber, 3f, tip + "\n\n" + "Dominion_Radio_ClickToCancel".Translate(), null, true))
                    RadioOrderUtility.OrderInvestigation(n);
            }
            else if (DominionTheme.StampButton(stamp, DominionTheme.StampInvestigate, DominionTheme.InkRed, -3f, tip, cannot))
                RadioOrderUtility.OrderInvestigation(n);
            RadioOrder order = null;
            foreach (RadioOrder o in orders.Orders)
                if (o.kind == RadioOrderKind.Investigate && o.newsId == n.id) order = o;
            string note = queued
                ? "Dominion_Radio_InquiryNote".Translate((order.progress / order.WorkNeeded).ToStringPercent())
                : "Dominion_Radio_InvestigateNote".Translate();
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
                if (n.def != null && n.def.newsworthiness >= StopPressNewsworthiness)
                {
                    color = DominionTheme.InkRed;
                    return DominionTheme.StampStopPress;
                }
                color = DominionTheme.InkGreen;
                return DominionTheme.StampConfirmed;
            }
            color = DominionTheme.InkAmber;
            return DominionTheme.StampRumor;
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
