using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The news feed: a filterable list of reports on the left and the full story on the right.</summary>
    public class NewsTabPanel
    {
        private const float RowHeight = 46f;
        private const float FilterHeight = 30f;

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
            Rect filters = new Rect(rect.x, rect.y, rect.width, FilterHeight);
            DrawFilters(filters, log);

            Rect body = new Rect(rect.x, rect.y + FilterHeight + 6f, rect.width, rect.height - FilterHeight - 6f);
            Rect listRect = body.LeftPart(0.45f);
            Rect storyRect = body.RightPart(0.55f);
            storyRect.xMin += 8f;

            Filter(log);
            DrawList(listRect, log);
            Widgets.DrawMenuSection(storyRect);
            if (selected != null)
                DrawStory(storyRect.ContractedBy(10f), selected, log);
            else
                Widgets.Label(storyRect.ContractedBy(10f), shown.Count == 0 ? "Dominion_News_Empty".Translate() : "Dominion_News_SelectReport".Translate());
        }

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
                    options.Add(new FloatMenuOption(("Dominion_EventCategory_" + c).Translate(), () => categoryFilter = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            x = r.xMax + 6f;

            r = new Rect(x, rect.y, 200f, rect.height);
            if (Widgets.ButtonText(r, factionFilter?.Name.Truncate(190f) ?? "Dominion_News_AllFactions".Translate()))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_News_AllFactions".Translate(), () => factionFilter = null) };
                foreach (Faction f in Find.FactionManager.AllFactionsVisibleInViewOrder)
                {
                    Faction captured = f;
                    options.Add(new FloatMenuOption(f.Name, () => factionFilter = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            x = r.xMax + 10f;

            r = new Rect(x, rect.y + 3f, 150f, 24f);
            Widgets.CheckboxLabeled(r, "Dominion_News_ShowRumors".Translate(), ref showRumors);
            x = r.xMax + 10f;
            r = new Rect(x, rect.y + 3f, 130f, 24f);
            Widgets.CheckboxLabeled(r, "Dominion_News_UnreadOnly".Translate(), ref unreadOnly);

            r = new Rect(rect.xMax - 140f, rect.y, 140f, rect.height);
            if (Widgets.ButtonText(r, "Dominion_News_MarkAllRead".Translate()))
                log.MarkAllRead();
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
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (n == selected) Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);

                if (!n.read)
                {
                    GUI.color = new Color(0.9f, 0.75f, 0.3f);
                    Widgets.DrawBoxSolid(new Rect(row.x + 2f, row.y + 8f, 4f, RowHeight - 16f), GUI.color);
                    GUI.color = Color.white;
                }

                Rect text = new Rect(row.x + 10f, row.y + 2f, row.width - 12f, 22f);
                Widgets.Label(text, n.headline.Truncate(text.width));
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(text.x, text.yMax, text.width, 18f), Meta(n, now).Truncate(text.width));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row))
                {
                    selected = n;
                    storyScroll = Vector2.zero;
                    log.MarkRead(n);
                }
            }
            Widgets.EndScrollView();
        }

        private static string Meta(NewsItem n, int now)
        {
            string conf = NewsNotifier.ConfidenceLabel(n.confidence);
            if (n.confidence == NewsConfidence.Rumor) conf = conf.Colorize(new Color(1f, 0.75f, 0.35f));
            return (now - n.arrivesTick).ToStringTicksToPeriod(allowSeconds: false) + " · " +
                   ("Dominion_EventCategory_" + n.Category).Translate() + " · " + conf + " · " + NewsText.SourceLabel(n.source);
        }

        private void DrawStory(Rect rect, NewsItem n, NewsLog log)
        {
            Text.Font = GameFont.Medium;
            float headH = Text.CalcHeight(n.headline, rect.width);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, headH), n.headline);
            Text.Font = GameFont.Small;
            float y = rect.y + headH + 4f;

            GUI.color = Color.gray;
            string when = "Dominion_News_Reported".Translate(
                GenDate.DateFullStringAt(GenDate.TickGameToAbs(n.arrivesTick), Find.WorldGrid.LongLatOf(Find.CurrentMap?.Tile ?? 0)));
            Widgets.Label(new Rect(rect.x, y, rect.width, 22f), when);
            y += 22f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
                "Dominion_News_LetterFooter".Translate(NewsText.SourceLabel(n.source), NewsNotifier.ConfidenceLabel(n.confidence)));
            y += 26f;
            GUI.color = Color.white;

            // Involved factions
            float x = rect.x;
            foreach (Faction f in new[] { n.primary, n.secondary })
            {
                if (f == null) continue;
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(x, y, 22f, 22f), f);
                float w = Text.CalcSize(f.Name).x;
                Widgets.Label(new Rect(x + 26f, y, w + 4f, 22f), f.Name);
                x += 26f + w + 16f;
            }
            y += 28f;
            Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            y += 6f;

            string body = n.story ?? "";
            if (n.corrected)
                body += "\n\n" + "Dominion_News_LaterFalse".Translate().Colorize(new Color(1f, 0.6f, 0.4f));
            if (n.correctsId >= 0)
            {
                NewsItem original = log.FindDelivered(n.correctsId);
                if (original != null)
                    body += "\n\n" + "Dominion_News_Corrects".Translate(original.headline).Colorize(Color.gray);
            }

            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
            float h = Text.CalcHeight(body, outRect.width - 16f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, h);
            Widgets.BeginScrollView(outRect, ref storyScroll, view);
            Widgets.Label(view, body);
            Widgets.EndScrollView();
        }
    }
}
