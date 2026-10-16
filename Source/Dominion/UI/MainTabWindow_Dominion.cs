using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Dominion main tab: news, faction dossiers, diplomacy, treaties, wars, the council, intelligence, the
    /// Federation, the event log (dev mode) and integration status.
    /// </summary>
    public class MainTabWindow_Dominion : MainTabWindow
    {
        private enum Tab { News, Overview, Diplomacy, Treaties, War, Council, Intelligence, Federation, Events, Integrations }

        private const float RowHeight = 26f;

        private Tab current = Tab.News;
        private readonly NewsTabPanel newsPanel = new NewsTabPanel();
        private readonly DiplomacyPanel diplomacyPanel = new DiplomacyPanel();
        private readonly TreatiesPanel treatiesPanel = new TreatiesPanel();
        private readonly CouncilPanel councilPanel = new CouncilPanel();
        private readonly IntelligencePanel intelligencePanel = new IntelligencePanel();
        private readonly FederationPanel federationPanel = new FederationPanel();
        private readonly WarPanel warPanel = new WarPanel();
        private Vector2 scroll;
        private Vector2 dossierScroll;
        private Faction selected;

        public override Vector2 RequestedTabSize => new Vector2(1000f, 720f + DominionHeader.Height + 6f);

        public override void DoWindowContents(Rect inRect)
        {
            // The raw event log shows everything, including events the player has no intel on, so it is dev-only.
            if (current == Tab.Events && !Prefs.DevMode) current = Tab.News;
            int unread = WorldComponent_Dominion.Instance?.News.UnreadCount ?? 0;
            string newsLabel = "Dominion_Tab_News".Translate() + (unread > 0 ? " (" + unread + ")" : "");
            var tabs = new List<TabRecord>
            {
                new TabRecord(newsLabel, () => Select(Tab.News), current == Tab.News),
                new TabRecord("Dominion_Tab_Overview".Translate(), () => Select(Tab.Overview), current == Tab.Overview),
                new TabRecord("Dominion_Tab_Diplomacy".Translate(), () => Select(Tab.Diplomacy), current == Tab.Diplomacy),
                new TabRecord("Dominion_Tab_Treaties".Translate(), () => Select(Tab.Treaties), current == Tab.Treaties),
                new TabRecord("Dominion_Tab_War".Translate(), () => Select(Tab.War), current == Tab.War),
                new TabRecord("Dominion_Tab_Council".Translate(), () => Select(Tab.Council), current == Tab.Council),
                new TabRecord("Dominion_Tab_Intelligence".Translate(), () => Select(Tab.Intelligence), current == Tab.Intelligence),
                new TabRecord("Dominion_Tab_Federation".Translate(), () => Select(Tab.Federation), current == Tab.Federation),
            };
            if (Prefs.DevMode)
                tabs.Add(new TabRecord("Dominion_Tab_Events".Translate(), () => Select(Tab.Events), current == Tab.Events));
            tabs.Add(new TabRecord("Dominion_Tab_Integrations".Translate(), () => Select(Tab.Integrations), current == Tab.Integrations));

            DominionHeader.Draw(new Rect(inRect.x, inRect.y, inRect.width, DominionHeader.Height), WorldComponent_Dominion.Instance, n =>
            {
                Select(Tab.News);
                newsPanel.Open(n, WorldComponent_Dominion.Instance.News);
            });
            Rect body = inRect;
            body.yMin += DominionHeader.Height + 6f + TabDrawer.TabHeight;
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);
            body = body.ContractedBy(10f);

            switch (current)
            {
                case Tab.News:
                    if (WorldComponent_Dominion.Instance != null) newsPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Overview: DrawOverview(body); break;
                case Tab.Diplomacy:
                    if (WorldComponent_Dominion.Instance != null) diplomacyPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Intelligence:
                    if (WorldComponent_Dominion.Instance != null) intelligencePanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Council:
                    if (WorldComponent_Dominion.Instance != null) councilPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Treaties:
                    if (WorldComponent_Dominion.Instance != null) treatiesPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.War:
                    if (WorldComponent_Dominion.Instance != null) warPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Federation:
                    if (WorldComponent_Dominion.Instance != null) federationPanel.Draw(body, WorldComponent_Dominion.Instance);
                    break;
                case Tab.Events: DrawEvents(body); break;
                case Tab.Integrations: DrawIntegrations(body); break;
            }
        }

        private void Select(Tab tab)
        {
            current = tab;
            scroll = Vector2.zero;
        }

        private void DrawOverview(Rect rect)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            List<Faction> shown = comp.Cache.Simulated;
            if (selected != null && !shown.Contains(selected)) selected = null;
            if (selected == null && shown.Count > 0) selected = shown[0];

            Rect tableRect = rect.TopPart(0.38f);
            Rect dossierRect = rect.BottomPart(0.62f);
            dossierRect.yMin += 6f;

            // Columns: faction, government, stability, economy, military, relation to player
            float[] widths = { 0.30f, 0.14f, 0.12f, 0.12f, 0.12f, 0.20f };
            Text.Font = GameFont.Small;
            Rect header = new Rect(tableRect.x, tableRect.y, tableRect.width - 16f, RowHeight);
            GUI.color = Color.gray;
            string[] headers =
            {
                "Dominion_Col_Faction".Translate(), "Dominion_Col_Government".Translate(), "Dominion_Col_Stability".Translate(),
                "Dominion_Col_Economy".Translate(), "Dominion_Col_Military".Translate(), "Dominion_Col_Relation".Translate(),
            };
            float x = header.x;
            for (int c = 0; c < headers.Length; c++)
            {
                Widgets.Label(new Rect(x, header.y, header.width * widths[c], RowHeight), headers[c]);
                x += header.width * widths[c];
            }
            GUI.color = Color.white;

            Rect outRect = new Rect(tableRect.x, tableRect.y + RowHeight, tableRect.width, tableRect.height - RowHeight);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, shown.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                Faction f = shown[i];
                comp.TryGetState(f, out FactionState s);
                bool known = IntelUtility.Knows(f, IntelUtility.RestrictedIntel);
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (f == selected) Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);
                if (Widgets.ButtonInvisible(row))
                {
                    selected = f;
                    dossierScroll = Vector2.zero;
                }

                x = row.x;
                Rect cell = new Rect(x, row.y, row.width * widths[0], RowHeight);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(cell.x, cell.y + 1f, 24f, 24f), f);
                cell.xMin += 30f;
                Widgets.Label(cell, f.Name.Truncate(cell.width));
                x += row.width * widths[0];
                Widgets.Label(new Rect(x, row.y, row.width * widths[1], RowHeight), s != null && s.governmentInitialized ? DominionUI.GovernmentLabel(s.government) : "-");
                x += row.width * widths[1];
                Widgets.Label(new Rect(x, row.y, row.width * widths[2], RowHeight), s == null ? "-" : known ? DominionUI.Colored(s.stability.ToString("F0"), DominionUI.StabilityColor(s.stability)) : "?");
                x += row.width * widths[2];
                Widgets.Label(new Rect(x, row.y, row.width * widths[3], RowHeight), s == null ? "-" : known ? s.economyIndex.ToString("F0") : "?");
                x += row.width * widths[3];
                Widgets.Label(new Rect(x, row.y, row.width * widths[4], RowHeight), s == null ? "-" : known ? s.militaryStrength.ToString("F0") : "?");
                x += row.width * widths[4];
                FactionRelationKind kind = f.RelationKindWith(Faction.OfPlayer);
                GUI.color = kind.GetColor();
                Widgets.Label(new Rect(x, row.y, row.width * widths[5], RowHeight), kind.GetLabelCap() + " (" + f.PlayerGoodwill.ToStringWithSign() + ")");
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();

            if (selected != null)
                FactionDossierPanel.Draw(dossierRect, selected, comp, ref dossierScroll);
        }

        private void DrawEvents(Rect rect)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            IReadOnlyList<DominionEvent> events = comp?.EventLog.Events;
            if (events == null || events.Count == 0)
            {
                Widgets.Label(rect, "Dominion_NoEvents".Translate());
                return;
            }

            int now = Find.TickManager.TicksGame;
            Rect view = new Rect(0f, 0f, rect.width - 16f, events.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref scroll, view);
            int firstVisible = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int lastVisible = Mathf.Min(events.Count - 1, firstVisible + (int)(rect.height / RowHeight) + 1);
            for (int i = firstVisible; i <= lastVisible; i++)
            {
                // Newest first.
                DominionEvent ev = events[events.Count - 1 - i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                GUI.color = Color.gray;
                Widgets.Label(row.LeftPartPixels(110f), (now - ev.tick).ToStringTicksToPeriod(allowSeconds: false) );
                Widgets.Label(new Rect(row.x + 110f, row.y, 110f, RowHeight), ("Dominion_EventCategory_" + ev.def.category).Translate());
                GUI.color = Color.white;
                Widgets.Label(new Rect(row.x + 225f, row.y, row.width - 225f, RowHeight), ev.Headline.Truncate(row.width - 225f));
                TooltipHandler.TipRegion(row, ev.def.description.NullOrEmpty() ? ev.ToString() : ev.def.description);
            }
            Widgets.EndScrollView();
        }

        private void DrawIntegrations(Rect rect)
        {
            IReadOnlyList<DominionIntegration> all = IntegrationRegistry.All;
            Rect view = new Rect(0f, 0f, rect.width - 16f, all.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref scroll, view);
            for (int i = 0; i < all.Count; i++)
            {
                DominionIntegration integration = all[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.Label(row.LeftPart(0.5f), integration.Label + (integration.Required ? " *" : ""));
                GUI.color = StatusColor(integration.Status);
                Widgets.Label(row.RightPart(0.5f), ("Dominion_IntegrationStatus_" + integration.Status).Translate());
                GUI.color = Color.white;
                TooltipHandler.TipRegion(row, integration.PackageId + "\n" + string.Join("\n", integration.BindingReport));
            }
            Widgets.EndScrollView();
        }

        private static Color StatusColor(IntegrationStatus status)
        {
            switch (status)
            {
                case IntegrationStatus.Active: return ColorLibrary.Green;
                case IntegrationStatus.Failed: return ColorLibrary.RedReadable;
                case IntegrationStatus.Disabled: return Color.yellow;
                default: return Color.gray;
            }
        }
    }
}
