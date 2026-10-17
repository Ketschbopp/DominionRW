using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Overview tab: every faction in a sortable table (click a column to sort, click again to reverse) with trend
    /// arrows for the past week, a world map of spheres of influence (scroll to zoom, drag to pan), jump buttons to its other pages, and its dossier.
    /// </summary>
    public class OverviewPanel
    {
        private const float RowHeight = 26f;
        private const float JumpBarHeight = 28f;

        private enum Column { Faction, Government, Stability, Economy, Military, Relation }

        private static readonly float[] Widths = { 0.30f, 0.14f, 0.13f, 0.13f, 0.13f, 0.17f };

        private static Column sortBy = Column.Faction;
        private static bool descending;

        private Vector2 scroll;
        private readonly WorldMapView map = new WorldMapView();
        private Faction mapFocus;
        private Vector2 dossierScroll;
        private readonly List<Faction> sorted = new List<Faction>();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            List<Faction> shown = comp.Cache.Simulated;
            Faction selected = DominionUIState.SelectedIn(shown);

            Rect top = rect.TopPart(0.46f);
            float mapW = Mathf.Min(top.width * 0.46f, top.height * 2.2f);
            Rect tableRect = new Rect(top.x, top.y, top.width - mapW - 8f, top.height);
            Rect mapRect = new Rect(tableRect.xMax + 8f, top.y, mapW, top.height - 18f);
            DrawTable(tableRect, shown, comp, selected);
            // The map glides to whichever faction is picked, from the table or the map itself.
            if (selected != mapFocus)
            {
                mapFocus = selected;
                if (selected != null) map.FocusOn(comp.Cache.SettlementsOf(selected).Select(x => x.Tile));
            }
            map.Draw(mapRect, MapStyle.Atlas, selected, null, null, f =>
            {
                if (f.IsPlayer) return;
                DominionUIState.selected = f;
                dossierScroll = Vector2.zero;
            });
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(mapRect.x, mapRect.yMax + 1f, mapRect.width, 17f), "Dominion_UI_MapLegend".Translate(selected?.Name ?? "-").ToString().Truncate(mapRect.width));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            if (selected == null) return;
            Rect jump = new Rect(rect.x, top.yMax + 6f, rect.width, JumpBarHeight);
            Rect name = jump.LeftPart(0.3f);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(name.x, name.y + 2f, 24f, 24f), selected);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(name.x + 30f, name.y - 2f, name.width - 30f, 32f), selected.Name.Truncate(name.width - 30f));
            Text.Font = GameFont.Small;
            DominionUIState.JumpButtons(jump.RightPart(0.68f), selected, DominionTab.Overview);

            Rect dossierRect = new Rect(rect.x, jump.yMax + 6f, rect.width, rect.yMax - jump.yMax - 6f);
            FactionDossierPanel.Draw(dossierRect, selected, comp, ref dossierScroll);
        }

        private void DrawTable(Rect tableRect, List<Faction> shown, WorldComponent_Dominion comp, Faction selected)
        {
            Text.Font = GameFont.Small;
            Rect header = new Rect(tableRect.x, tableRect.y, tableRect.width - 16f, RowHeight);
            string[] headers =
            {
                "Dominion_Col_Faction".Translate(), "Dominion_Col_Government".Translate(), "Dominion_Col_Stability".Translate(),
                "Dominion_Col_Economy".Translate(), "Dominion_Col_Military".Translate(), "Dominion_Col_Relation".Translate(),
            };
            float x = header.x;
            for (int c = 0; c < headers.Length; c++)
            {
                Rect h = new Rect(x, header.y, header.width * Widths[c], RowHeight);
                Column col = (Column)c;
                string label = headers[c] + (sortBy == col ? (descending ? " ▼" : " ▲") : "");
                GUI.color = sortBy == col ? Color.white : Color.gray;
                Widgets.Label(h, label.Truncate(h.width));
                GUI.color = Color.white;
                Widgets.DrawHighlightIfMouseover(h);
                TooltipHandler.TipRegion(h, "Dominion_UI_SortBy".Translate(headers[c]));
                if (Widgets.ButtonInvisible(h))
                {
                    if (sortBy == col) descending = !descending;
                    else
                    {
                        sortBy = col;
                        descending = col != Column.Faction && col != Column.Government;
                    }
                }
                x += header.width * Widths[c];
            }

            sorted.Clear();
            sorted.AddRange(shown);
            Sort(sorted, comp);

            Rect outRect = new Rect(tableRect.x, tableRect.y + RowHeight, tableRect.width, tableRect.height - RowHeight);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, sorted.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < sorted.Count; i++)
            {
                Faction f = sorted[i];
                comp.TryGetState(f, out FactionState s);
                bool known = IntelUtility.Knows(f, IntelUtility.RestrictedIntel);
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (f == selected) Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);
                // A strip of the faction's colour down the left edge.
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 2f, 3f, row.height - 4f), f.Color);
                TooltipHandler.TipRegion(row, "Dominion_Tip_SelectFaction".Translate(f.Name));
                if (Widgets.ButtonInvisible(row))
                {
                    DominionUIState.selected = f;
                    dossierScroll = Vector2.zero;
                }

                x = row.x + 5f;
                Rect cell = new Rect(x, row.y, row.width * Widths[0] - 5f, RowHeight);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(cell.x, cell.y + 1f, 24f, 24f), f);
                cell.xMin += 30f;
                Widgets.Label(cell, f.Name.Truncate(cell.width));
                x = row.x + row.width * Widths[0];
                Widgets.Label(new Rect(x, row.y, row.width * Widths[1], RowHeight), s != null && s.governmentInitialized ? DominionUI.GovernmentLabel(s.government) : "-");
                x += row.width * Widths[1];
                StatCell(new Rect(x, row.y, row.width * Widths[2], RowHeight), s, known, s?.stability ?? 0f, s?.histStability, true);
                x += row.width * Widths[2];
                StatCell(new Rect(x, row.y, row.width * Widths[3], RowHeight), s, known, s?.economyIndex ?? 0f, s?.histEconomy, false);
                x += row.width * Widths[3];
                StatCell(new Rect(x, row.y, row.width * Widths[4], RowHeight), s, known, s?.militaryStrength ?? 0f, s?.histMilitary, false);
                x += row.width * Widths[4];
                FactionRelationKind kind = f.RelationKindWith(Faction.OfPlayer);
                GUI.color = kind.GetColor();
                Rect rel = new Rect(x, row.y, row.width * Widths[5], RowHeight);
                Widgets.Label(rel, (kind.GetLabelCap() + " (" + f.PlayerGoodwill.ToStringWithSign() + ")").Truncate(rel.width));
                GUI.color = Color.white;
            }
            Widgets.EndScrollView();
        }

        /// <summary>A number with a trend arrow for the past week (green up, red down), or "?" without intel.</summary>
        private static void StatCell(Rect r, FactionState s, bool known, float value, List<float> hist, bool stability)
        {
            if (s == null)
            {
                Widgets.Label(r, "-");
                return;
            }
            if (!known)
            {
                GUI.color = Color.gray;
                Widgets.Label(r, "?");
                GUI.color = Color.white;
                TooltipHandler.TipRegion(r, IntelUtility.Unknown(IntelUtility.RestrictedIntel));
                return;
            }
            string text = value.ToString("F0");
            Widgets.Label(r, stability ? DominionUI.Colored(text, DominionUI.StabilityColor(value)) : text);
            float trend = FactionState.Trend(hist, value);
            float threshold = Mathf.Max(1f, Mathf.Abs(value) * 0.02f);
            if (Mathf.Abs(trend) < threshold) return;
            Text.Font = GameFont.Tiny;
            float w = Text.CalcSize(text).x;
            Rect arrow = new Rect(r.x + w + 14f, r.y + 4f, 40f, r.height - 4f);
            GUI.color = trend > 0f ? ColorLibrary.Green : ColorLibrary.RedReadable;
            Widgets.Label(arrow, (trend > 0f ? "▲" : "▼") + Mathf.Abs(trend).ToString("F0"));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, "Dominion_UI_Trend".Translate(trend.ToStringWithSign("F0"), hist.Count));
        }

        private static void Sort(List<Faction> list, WorldComponent_Dominion comp)
        {
            Func<Faction, float> key = f =>
            {
                comp.TryGetState(f, out FactionState s);
                bool known = IntelUtility.Knows(f, IntelUtility.RestrictedIntel);
                switch (sortBy)
                {
                    case Column.Government: return s != null ? (int)s.government : -1;
                    case Column.Stability: return s != null && known ? s.stability : float.MinValue;
                    case Column.Economy: return s != null && known ? s.economyIndex : float.MinValue;
                    case Column.Military: return s != null && known ? s.militaryStrength : float.MinValue;
                    case Column.Relation: return f.PlayerGoodwill;
                    default: return 0f;
                }
            };
            if (sortBy == Column.Faction)
                list.Sort((a, b) => descending ? string.Compare(b.Name, a.Name, StringComparison.CurrentCultureIgnoreCase)
                    : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            else
                list.Sort((a, b) =>
                {
                    float ka = key(a), kb = key(b);
                    // Unknown values always sink to the bottom.
                    if (ka == float.MinValue && kb != float.MinValue) return 1;
                    if (kb == float.MinValue && ka != float.MinValue) return -1;
                    int c = descending ? kb.CompareTo(ka) : ka.CompareTo(kb);
                    return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                });
        }
    }

    /// <summary>Takes the daily snapshots the trend arrows compare against.</summary>
    public class TrendSystem : SimSystem
    {
        public override int Order => 95;

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp != null && comp.TryGetState(faction, out FactionState s)) s.RecordHistory();
        }
    }
}
