using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Treaties tab. Your cabinet: every treaty you are party to as a sealed card (click to read it, renew it before
    /// it lapses, or tear it up). The world: treaties between other factions that your intelligence knows of, as a list
    /// or as a web of who is bound to whom.
    /// </summary>
    public class TreatiesPanel
    {
        private enum Mode { Cabinet, World, Web }

        private const float Line = 26f;
        private const float CardW = 280f;
        private const float CardH = 124f;

        private static Mode mode = Mode.Cabinet;
        private Vector2 scroll;
        private Faction webFocus;
        private readonly List<Treaty> mine = new List<Treaty>();
        private readonly List<Treaty> world = new List<Treaty>();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            mine.Clear();
            world.Clear();
            foreach (Treaty t in comp.Treaties.All)
            {
                if (t.Involves(Faction.OfPlayer)) mine.Add(t);
                else if (IsKnown(t)) world.Add(t);
            }
            mine.SortBy(t => t.expiresTick < 0 ? int.MaxValue : t.expiresTick);

            Rect bar = new Rect(rect.x, rect.y, rect.width, 30f);
            float x = bar.x;
            foreach (Mode m in System.Enum.GetValues(typeof(Mode)))
            {
                Rect b = new Rect(x, bar.y, 170f, bar.height);
                string label = ("Dominion_Treaties_Mode_" + m).Translate() + (m == Mode.Cabinet ? " (" + mine.Count + ")" : m == Mode.World ? " (" + world.Count + ")" : "");
                if (mode == m) Widgets.DrawHighlightSelected(b);
                if (DominionUIState.ButtonWithTip(b, label, ("Dominion_Treaties_Mode_" + m + "_Desc").Translate())) mode = m;
                x += 174f;
            }
            Rect draft = new Rect(bar.xMax - 200f, bar.y, 200f, bar.height);
            if (DominionUIState.ButtonWithTip(draft, "Dominion_Treaties_Draft".Translate(), "Dominion_Treaties_Draft_Desc".Translate()))
                Dialog_TreatyDraft.ProposeAnyMenu();

            Rect body = new Rect(rect.x, bar.yMax + 8f, rect.width, rect.yMax - bar.yMax - 8f);
            switch (mode)
            {
                case Mode.Cabinet: DrawCabinet(body); break;
                case Mode.World: DrawWorld(body); break;
                case Mode.Web: DrawWeb(body, comp); break;
            }
        }

        // ------------------------------------------------------------------ cabinet

        private void DrawCabinet(Rect rect)
        {
            if (mine.Count == 0)
            {
                Widgets.Label(rect, "Dominion_Treaties_NoneYours".Translate());
                return;
            }
            int perRow = Mathf.Max(1, (int)((rect.width - 16f) / (CardW + 10f)));
            int rows = Mathf.CeilToInt(mine.Count / (float)perRow);
            Rect view = new Rect(0f, 0f, rect.width - 16f, rows * (CardH + 10f));
            Widgets.BeginScrollView(rect, ref scroll, view);
            for (int i = 0; i < mine.Count; i++)
            {
                Rect card = new Rect(i % perRow * (CardW + 10f), i / perRow * (CardH + 10f), CardW, CardH);
                DrawCard(card, mine[i]);
            }
            Widgets.EndScrollView();
        }

        private static void DrawCard(Rect card, Treaty t)
        {
            Faction other = t.Other(Faction.OfPlayer);
            DominionPaper.DrawCard(card);
            Rect inner = card.ContractedBy(8f);
            DominionPaper.DrawSeal(new Rect(inner.x, inner.y, 46f, 46f), other);
            float tx = inner.x + 52f, tw = inner.width - 52f;
            DominionTheme.InkLabel(new Rect(tx, inner.y, tw, 22f), t.Label.Truncate(tw));
            DominionTheme.InkLabel(new Rect(tx, inner.y + 20f, tw, 18f), other.Name.Truncate(tw), DominionTheme.InkFaded, GameFont.Tiny);
            List<string> provisions = TreatyDraft.Provisions(t.def, t.a, t.b);
            if (provisions.Count > 0)
                DominionTheme.InkLabel(new Rect(inner.x, inner.y + 50f, inner.width, 30f), provisions[0], DominionTheme.Ink, GameFont.Tiny);

            Rect timeBar = new Rect(inner.x, inner.yMax - 22f, inner.width, 20f);
            bool soon = t.expiresTick >= 0 && t.DaysLeft <= 15;
            Rect barRect = soon && t.def.Expires && !t.def.imposed ? timeBar.LeftPart(0.62f) : timeBar;
            if (t.expiresTick >= 0)
            {
                float total = Mathf.Max(1f, t.expiresTick - t.signedTick);
                float left = Mathf.Clamp01((t.expiresTick - Find.TickManager.TicksGame) / total);
                Widgets.DrawBoxSolid(barRect, new Color(0.3f, 0.24f, 0.17f, 0.15f));
                Color c = soon ? DominionTheme.InkRed : DominionTheme.InkGreen;
                Widgets.DrawBoxSolid(new Rect(barRect.x, barRect.y, barRect.width * left, barRect.height), new Color(c.r, c.g, c.b, 0.45f));
                DominionTheme.InkLabel(barRect, "Dominion_Treaties_DaysLeft".Translate(t.DaysLeft), DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleCenter);
            }
            else DominionTheme.InkLabel(barRect, "Dominion_Dip_NoExpiry".Translate(), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.MiddleLeft);

            if (barRect != timeBar)
            {
                Rect renew = timeBar.RightPart(0.35f);
                if (DominionPaper.InkButton(renew, "Dominion_Treaty_Renew".Translate(), "Dominion_Treaty_Renew_Desc".Translate(other.Name), null, false, DominionTheme.InkGreen))
                    Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, other, t));
                card.height -= 24f; // the renew button is not part of the card's click area
            }
            TooltipHandler.TipRegion(card, t.def.description + "\n\n" + "Dominion_Treaties_ClickToRead".Translate());
            if (Widgets.ButtonInvisible(card)) Find.WindowStack.Add(new Dialog_TreatyDocument(t));
        }

        // ------------------------------------------------------------------ world list

        private void DrawWorld(Rect rect)
        {
            if (world.Count == 0)
            {
                Widgets.Label(rect, "Dominion_Treaties_None".Translate());
                return;
            }
            world.SortBy(t => t.def.label, t => t.a.Name);
            float[] widths = { 0.24f, 0.29f, 0.29f, 0.18f };
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
            Rect view = new Rect(0f, 0f, outRect.width - 16f, world.Count * Line);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < world.Count; i++)
            {
                Treaty t = world[i];
                Rect row = new Rect(0f, i * Line, view.width, Line);
                if (i % 2 == 1) Widgets.DrawLightHighlight(row);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 4f, 4f, row.height - 8f), WebColor(t.def));
                x = row.x + 8f;
                Widgets.Label(new Rect(x, row.y, row.width * widths[0] - 8f, Line), t.Label.Truncate(row.width * widths[0] - 8f));
                x = row.x + row.width * widths[0];
                Party(new Rect(x, row.y, row.width * widths[1], Line), t.a);
                x += row.width * widths[1];
                Party(new Rect(x, row.y, row.width * widths[2], Line), t.b);
                x += row.width * widths[2];
                Widgets.Label(new Rect(x, row.y, row.width * widths[3], Line),
                    t.expiresTick >= 0 ? (t.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false) : "Dominion_Dip_NoExpiry".Translate().ToString());
                TooltipHandler.TipRegion(row, t.def.description + "\n\n" + "Dominion_Treaties_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(row)) Find.WindowStack.Add(new Dialog_TreatyDocument(t));
            }
            Widgets.EndScrollView();
        }

        private static void Party(Rect r, Faction f)
        {
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(r.x, r.y + 2f, 20f, 20f), f);
            Widgets.Label(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), f.Name.Truncate(r.width - 24f));
        }

        // ------------------------------------------------------------------ web

        /// <summary>
        /// Every known faction on a circle, with a line for each treaty you know of, coloured by kind. Hover a faction
        /// to light up its treaties; click it to keep it lit.
        /// </summary>
        private void DrawWeb(Rect rect, WorldComponent_Dominion comp)
        {
            var factions = new List<Faction> { Faction.OfPlayer };
            factions.AddRange(comp.Cache.Simulated);
            Rect legend = new Rect(rect.xMax - 200f, rect.y, 200f, rect.height);
            Rect area = new Rect(rect.x, rect.y, rect.width - 210f, rect.height);
            Widgets.DrawMenuSection(area);
            Vector2 centre = area.center;
            float radius = Mathf.Min(area.width, area.height) / 2f - 60f;
            var pos = new Dictionary<Faction, Vector2>();
            for (int i = 0; i < factions.Count; i++)
            {
                float a = -Mathf.PI / 2f + i * 2f * Mathf.PI / factions.Count;
                pos[factions[i]] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }

            Faction hover = null;
            foreach (Faction f in factions)
                if ((Event.current.mousePosition - pos[f]).sqrMagnitude < 18f * 18f) hover = f;
            Faction lit = hover ?? webFocus;

            // Lines first, so the faction badges sit on top.
            var pairs = new Dictionary<long, int>();
            foreach (Treaty t in mine.Concat(world))
            {
                if (!pos.TryGetValue(t.a, out Vector2 pa) || !pos.TryGetValue(t.b, out Vector2 pb)) continue;
                long key = RelationState.Key(Mathf.Min(t.a.loadID, t.b.loadID), Mathf.Max(t.a.loadID, t.b.loadID));
                pairs.TryGetValue(key, out int n);
                pairs[key] = n + 1;
                // Several treaties between the same pair are drawn side by side.
                Vector2 side = new Vector2(-(pb - pa).y, (pb - pa).x).normalized * (n * 3f);
                Color c = WebColor(t.def);
                bool on = lit == null || t.Involves(lit);
                c.a = on ? 0.9f : 0.12f;
                Widgets.DrawLine(pa + side, pb + side, c, on && lit != null ? 3f : 2f);
            }

            foreach (Faction f in factions)
            {
                Vector2 p = pos[f];
                Rect badge = new Rect(p.x - 16f, p.y - 16f, 32f, 32f);
                Widgets.DrawBoxSolid(badge.ExpandedBy(2f), f == lit ? Color.white : new Color(0f, 0f, 0f, 0.6f));
                GUI.color = f.Color;
                GUI.DrawTexture(badge, f.def.FactionIcon);
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(p.x - 70f, p.y + 18f, 140f, 30f), f.Name);
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(badge, () => WebTip(f), f.loadID ^ 0x7EB);
                if (Widgets.ButtonInvisible(badge)) webFocus = webFocus == f ? null : f;
            }

            // Legend
            float ly = legend.y;
            Widgets.Label(new Rect(legend.x, ly, legend.width, 24f), "Dominion_Treaties_Legend".Translate());
            ly += 26f;
            foreach (TreatyDef def in DefDatabase<TreatyDef>.AllDefsListForReading)
            {
                Widgets.DrawBoxSolid(new Rect(legend.x, ly + 9f, 22f, 4f), WebColor(def));
                Widgets.Label(new Rect(legend.x + 28f, ly, legend.width - 28f, 22f), def.LabelCap.ToString().Truncate(legend.width - 28f));
                ly += 22f;
            }
            ly += 8f;
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(legend.x, ly, legend.width, 80f), "Dominion_Treaties_WebHelp".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private string WebTip(Faction f)
        {
            var lines = new List<string> { f.Name };
            foreach (Treaty t in mine.Concat(world))
                if (t.Involves(f)) lines.Add("  " + t.Label + ": " + t.Other(f).Name);
            if (lines.Count == 1) lines.Add("  " + "Dominion_None".Translate());
            return string.Join("\n", lines);
        }

        /// <summary>The colour a kind of treaty is drawn in on the web and in lists.</summary>
        public static Color WebColor(TreatyDef def)
        {
            if (def.federation) return new Color(0.95f, 0.95f, 0.95f);
            if (def.alliance) return new Color(0.3f, 0.85f, 0.35f);
            if (def.defensive) return new Color(0.35f, 0.6f, 1f);
            if (def.puppet || def.tribute != 0f) return new Color(0.9f, 0.3f, 0.25f);
            if (def.intelBonus != 0f) return new Color(0.7f, 0.45f, 0.95f);
            if (def.tradeBonus != 0f || def.economyBonus != 0f) return new Color(0.95f, 0.78f, 0.3f);
            if (def.nonAggression) return new Color(0.65f, 0.65f, 0.65f);
            return new Color(0.6f, 0.75f, 0.75f);
        }

        private static bool IsKnown(Treaty t) =>
            t.Involves(Faction.OfPlayer) || IntelUtility.IntelOn(t.a, t.b) >= IntelUtility.RequiredIntel(t.def.secrecy);
    }
}
