using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A flat little map of the planet's surface (longitude across, latitude down), for the Overview and War tabs:
    /// every settlement as a dot in its faction's colour, the focused factions larger, wars as red lines between
    /// capitals and visible warbands as arrows toward their target. Click a dot to jump there on the world map.
    /// </summary>
    public static class MiniWorldMap
    {
        private struct Dot
        {
            public Settlement settlement;
            public Vector2 pos;
        }

        // Positions only change when settlements change hands or appear; a few seconds old is fine.
        private static readonly UICache<List<Dot>> dots = new UICache<List<Dot>>(Collect, 3f);

        private static readonly Color Sea = new Color(0.1f, 0.13f, 0.17f);
        private static readonly Color Grid = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color WarLine = new Color(0.85f, 0.2f, 0.15f, 0.7f);

        private static List<Dot> Collect()
        {
            var list = new List<Dot>();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || !DominionGeo.OnSurface(s.Tile)) continue;
                list.Add(new Dot { settlement = s, pos = Project(s.Tile) });
            }
            return list;
        }

        /// <summary>A surface tile as 0..1 map coordinates.</summary>
        public static Vector2 Project(PlanetTile tile)
        {
            Vector2 ll = Find.WorldGrid.LongLatOf(tile);
            return new Vector2((ll.x + 180f) / 360f, (90f - ll.y) / 180f);
        }

        private static Vector2 ToRect(Rect r, Vector2 p) => new Vector2(r.x + p.x * r.width, r.y + p.y * r.height);

        /// <summary>
        /// Draws the map. <paramref name="focus"/> (and <paramref name="focus2"/>) are drawn large; <paramref name="wars"/>
        /// limits war lines and warbands to those wars (null: every war the player knows of).
        /// </summary>
        public static void Draw(Rect rect, Faction focus, Faction focus2 = null, List<War> wars = null)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Widgets.DrawBoxSolid(rect, Sea);
            GUI.color = Grid;
            for (int i = 1; i < 6; i++) Widgets.DrawLineVertical(rect.x + rect.width * i / 6f, rect.y, rect.height);
            GUI.color = Color.white;
            for (int i = 1; i < 4; i++) Widgets.DrawLineHorizontal(rect.x, rect.y + rect.height * i / 4f, rect.width, Grid);
            Widgets.DrawLineHorizontal(rect.x, rect.center.y, rect.width, new Color(1f, 1f, 1f, 0.12f)); // the equator
            GUI.BeginGroup(rect);
            Rect local = new Rect(0f, 0f, rect.width, rect.height);

            // Wars: a line between the two capitals.
            if (comp != null)
            {
                foreach (War w in wars ?? comp.Wars.wars)
                {
                    if (!w.InvolvesPlayer && IntelUtility.IntelOn(w.attacker, w.defender) < IntelUtility.RestrictedIntel) continue;
                    Settlement a = DominionUIState.Capital(w.attacker), b = DominionUIState.Capital(w.defender);
                    if (a == null || b == null || !DominionGeo.OnSurface(a.Tile) || !DominionGeo.OnSurface(b.Tile)) continue;
                    Vector2 pa = ToRect(local, Project(a.Tile)), pb = ToRect(local, Project(b.Tile));
                    if (Mathf.Abs(pa.x - pb.x) > local.width / 2f) continue; // across the date line: skip rather than draw it the long way
                    bool focused = w.Involves(focus) || w.Involves(focus2);
                    Widgets.DrawLine(pa, pb, focused ? WarLine : new Color(WarLine.r, WarLine.g, WarLine.b, 0.25f), focused ? 2f : 1f);
                }
            }

            Dot hovered = default;
            bool any = false;
            foreach (Dot d in dots.Value)
            {
                Settlement s = d.settlement;
                if (s.Destroyed || s.Faction == null) continue;
                Vector2 p = ToRect(local, d.pos);
                bool big = s.Faction == focus || s.Faction == focus2;
                bool player = s.Faction.IsPlayer;
                float size = player ? 6f : big ? 7f : 3f;
                Rect r = new Rect(p.x - size / 2f, p.y - size / 2f, size, size);
                Color c = s.Faction.Color;
                if (!big && !player) c.a = 0.55f;
                if (big) Widgets.DrawBoxSolid(r.ExpandedBy(1f), Color.black);
                Widgets.DrawBoxSolid(r, player ? Color.white : c);
                if (s == DominionUIState.Capital(s.Faction) && big)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(r.ExpandedBy(2f));
                }
                if (Mouse.IsOver(r.ExpandedBy(3f)))
                {
                    hovered = d;
                    any = true;
                }
            }

            // Warbands: an arrow from where they are to where they are going.
            foreach (Warband w in WarbandUtility.All())
            {
                if (!w.VisibleToPlayer || !DominionGeo.OnSurface(w.Tile)) continue;
                if (wars != null && !wars.Exists(x => x.Involves(w.Faction) && x.Involves(w.target))) continue;
                Vector2 from = ToRect(local, Project(w.Tile)), to = ToRect(local, Project(w.DestinationTile));
                if (Mathf.Abs(from.x - to.x) > local.width / 2f) continue;
                Color c = w.target != null && w.target.IsPlayer ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.8f, 0.3f);
                Widgets.DrawLine(from, to, new Color(c.r, c.g, c.b, 0.5f), 1f);
                Vector2 dir = (to - from).sqrMagnitude > 1f ? (to - from).normalized : Vector2.right;
                Vector2 side = new Vector2(-dir.y, dir.x);
                Widgets.DrawLine(from + dir * 5f, from - dir * 3f + side * 4f, c, 2f);
                Widgets.DrawLine(from + dir * 5f, from - dir * 3f - side * 4f, c, 2f);
                Rect hit = new Rect(from.x - 5f, from.y - 5f, 10f, 10f);
                TooltipHandler.TipRegion(hit, w.Label + "\n" + w.MissionText + "\n" +
                    "Dominion_Warband_Arrives".Translate(w.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)) + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                if (Widgets.ButtonInvisible(hit)) CameraJumper.TryJumpAndSelect(w);
            }
            GUI.EndGroup();

            if (any)
            {
                Settlement s = hovered.settlement;
                TooltipHandler.TipRegion(new Rect(rect.x + hovered.pos.x * rect.width - 6f, rect.y + hovered.pos.y * rect.height - 6f, 12f, 12f),
                    s.Label + "\n" + s.Faction.Name + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                if (Widgets.ButtonInvisible(new Rect(rect.x + hovered.pos.x * rect.width - 6f, rect.y + hovered.pos.y * rect.height - 6f, 12f, 12f)))
                    CameraJumper.TryJumpAndSelect(s);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }
    }
}
