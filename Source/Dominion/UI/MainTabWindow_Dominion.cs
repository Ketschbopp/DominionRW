using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Dominion main tab. M0 shell: overview, event log and integration status.
    /// Diplomacy, treaties, intelligence, council and news tabs arrive in later milestones.
    /// </summary>
    public class MainTabWindow_Dominion : MainTabWindow
    {
        private enum Tab { Overview, Events, Integrations }

        private const float RowHeight = 26f;

        private Tab current = Tab.Overview;
        private Vector2 scroll;

        public override Vector2 RequestedTabSize => new Vector2(900f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            var tabs = new List<TabRecord>
            {
                new TabRecord("Dominion_Tab_Overview".Translate(), () => Select(Tab.Overview), current == Tab.Overview),
                new TabRecord("Dominion_Tab_Events".Translate(), () => Select(Tab.Events), current == Tab.Events),
                new TabRecord("Dominion_Tab_Integrations".Translate(), () => Select(Tab.Integrations), current == Tab.Integrations),
            };

            Rect body = inRect;
            body.yMin += TabDrawer.TabHeight;
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);
            body = body.ContractedBy(10f);

            switch (current)
            {
                case Tab.Overview: DrawOverview(body); break;
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
            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            var shown = new List<Faction>();
            foreach (Faction f in factions)
                if (DominionScheduler.IsSimulated(f))
                    shown.Add(f);

            Text.Font = GameFont.Small;
            Rect header = new Rect(rect.x, rect.y, rect.width, RowHeight);
            GUI.color = Color.gray;
            Widgets.Label(header.LeftPart(0.4f), "Dominion_Col_Faction".Translate());
            Widgets.Label(new Rect(header.x + header.width * 0.4f, header.y, header.width * 0.3f, RowHeight), "Dominion_Col_Leader".Translate());
            Widgets.Label(header.RightPart(0.3f), "Dominion_Col_Relation".Translate());
            GUI.color = Color.white;

            Rect outRect = new Rect(rect.x, rect.y + RowHeight, rect.width, rect.height - RowHeight);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, shown.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < shown.Count; i++)
            {
                Faction f = shown[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Rect nameRect = row.LeftPart(0.4f);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(nameRect.x, nameRect.y + 1f, 24f, 24f), f);
                nameRect.xMin += 30f;
                Widgets.Label(nameRect, f.Name.Truncate(nameRect.width));
                Widgets.Label(new Rect(row.x + row.width * 0.4f, row.y, row.width * 0.3f, RowHeight), f.leader?.LabelShortCap ?? "-");
                FactionRelationKind kind = f.RelationKindWith(Faction.OfPlayer);
                GUI.color = kind.GetColor();
                Widgets.Label(row.RightPart(0.3f), kind.GetLabelCap() + " (" + f.PlayerGoodwill.ToStringWithSign() + ")");
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
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
