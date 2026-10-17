using HarmonyLib;
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
        private const float RowHeight = 26f;
        private static readonly AccessTools.FieldRef<Window, WindowResizer> ResizerRef = AccessTools.FieldRefAccess<Window, WindowResizer>("resizer");
        private static readonly Vector2 MinSize = new Vector2(900f, 620f);

        private readonly NewsTabPanel newsPanel = new NewsTabPanel();
        private readonly OverviewPanel overviewPanel = new OverviewPanel();
        private readonly DiplomacyPanel diplomacyPanel = new DiplomacyPanel();
        private readonly TreatiesPanel treatiesPanel = new TreatiesPanel();
        private readonly CouncilPanel councilPanel = new CouncilPanel();
        private readonly IntelligencePanel intelligencePanel = new IntelligencePanel();
        private readonly FederationPanel federationPanel = new FederationPanel();
        private readonly WarPanel warPanel = new WarPanel();
        private Vector2 scroll;

        private static DominionTab Current
        {
            get => DominionUIState.tab;
            set => DominionUIState.tab = value;
        }

        public override Vector2 RequestedTabSize => new Vector2(1000f, 720f + DominionHeader.Height + 6f);

        public MainTabWindow_Dominion()
        {
            resizeable = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            // Window.resizer is private; the window makes one lazily, so give it ours with a sensible minimum size.
            ref WindowResizer resizer = ref ResizerRef(this);
            resizer ??= new WindowResizer();
            resizer.minWindowSize = MinSize;
            if (DominionUIState.windowSize is Vector2 size)
            {
                // Keep the bottom edge where the main tabs put it; grow upwards and rightwards.
                float w = Mathf.Clamp(size.x, MinSize.x, UI.screenWidth);
                float h = Mathf.Clamp(size.y, MinSize.y, UI.screenHeight - 35f);
                windowRect = new Rect(Mathf.Min(windowRect.x, UI.screenWidth - w), windowRect.yMax - h, w, h);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Remember the size once the player has resized the window.
            if (DominionUIState.windowSize.HasValue || (windowRect.size - InitialSize).sqrMagnitude > 4f) DominionUIState.windowSize = windowRect.size;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (DominionUIState.pendingTab is DominionTab wanted)
            {
                DominionUIState.pendingTab = null;
                Select(wanted);
            }
            if (DominionUIState.pendingNews != null && comp != null)
            {
                newsPanel.Open(DominionUIState.pendingNews, comp.News);
                DominionUIState.pendingNews = null;
            }
            // The raw event log shows everything, including events the player has no intel on, so it is dev-only.
            if (Current == DominionTab.Events && !Prefs.DevMode) Current = DominionTab.News;
            int unread = comp?.News.UnreadCount ?? 0;
            string newsLabel = "Dominion_Tab_News".Translate() + (unread > 0 ? " (" + unread + ")" : "");
            var tabs = new List<TabRecord>
            {
                Record(newsLabel, DominionTab.News),
                Record("Dominion_Tab_Overview".Translate(), DominionTab.Overview),
                Record("Dominion_Tab_Diplomacy".Translate(), DominionTab.Diplomacy),
                Record("Dominion_Tab_Treaties".Translate(), DominionTab.Treaties),
                Record("Dominion_Tab_War".Translate(), DominionTab.War),
                Record("Dominion_Tab_Council".Translate(), DominionTab.Council),
                Record("Dominion_Tab_Intelligence".Translate(), DominionTab.Intelligence),
                Record("Dominion_Tab_Federation".Translate(), DominionTab.Federation),
            };
            if (Prefs.DevMode) tabs.Add(Record("Dominion_Tab_Events".Translate(), DominionTab.Events));
            tabs.Add(Record("Dominion_Tab_Integrations".Translate(), DominionTab.Integrations));

            DominionHeader.Draw(new Rect(inRect.x, inRect.y, inRect.width, DominionHeader.Height), comp, n =>
            {
                Select(DominionTab.News);
                newsPanel.Open(n, comp.News);
            });
            Rect body = inRect;
            body.yMin += DominionHeader.Height + 6f + TabDrawer.TabHeight;
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);
            body = body.ContractedBy(10f);
            if (comp == null) return;

            switch (Current)
            {
                case DominionTab.News: newsPanel.Draw(body, comp); break;
                case DominionTab.Overview: overviewPanel.Draw(body, comp); break;
                case DominionTab.Diplomacy: diplomacyPanel.Draw(body, comp); break;
                case DominionTab.Intelligence: intelligencePanel.Draw(body, comp); break;
                case DominionTab.Council: councilPanel.Draw(body, comp); break;
                case DominionTab.Treaties: treatiesPanel.Draw(body, comp); break;
                case DominionTab.War: warPanel.Draw(body, comp); break;
                case DominionTab.Federation: federationPanel.Draw(body, comp); break;
                case DominionTab.Events: DrawEvents(body); break;
                case DominionTab.Integrations: DrawIntegrations(body); break;
            }
        }

        private TabRecord Record(string label, DominionTab tab) => new TabRecord(label, () => Select(tab), Current == tab);

        private void Select(DominionTab tab)
        {
            if (Current != tab) scroll = Vector2.zero;
            Current = tab;
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
