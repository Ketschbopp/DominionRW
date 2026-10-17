using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    public enum MapStyle { Atlas, WarRoom }

    /// <summary>
    /// The world map in the Overview and the war room: <see cref="TerritoryMap"/> with settlements, wars and warbands
    /// on top. Scroll to zoom, drag to pan; the map wraps round east to west. Names appear as you zoom in. Click a
    /// settlement to pick its faction, right-click it for more.
    /// </summary>
    public class WorldMapView
    {
        public const float MaxZoom = 10f;

        public float zoom = 1f;
        public Vector2 centre = new Vector2(0.5f, 0.5f);
        private float? targetZoom;
        private Vector2? targetCentre;

        private bool dragging;
        private float dragMoved;
        private bool suppressClick;
        private Rect local;

        private static readonly Color Graticule = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color WarLine = new Color(0.95f, 0.25f, 0.18f);

        /// <summary>Visible span across and down, as fractions of the whole map.</summary>
        private Vector2 Span => new Vector2(1f / zoom * (local.width / Mathf.Max(1f, local.height)) / 2f, 1f / zoom);

        // ------------------------------------------------------------------ view

        public void Reset()
        {
            targetZoom = MinZoom;
            targetCentre = new Vector2(centre.x, 0.5f);
        }

        /// <summary>Glide to show all these tiles, with a margin.</summary>
        public void FocusOn(IEnumerable<PlanetTile> tiles)
        {
            var pts = new List<Vector2>();
            foreach (PlanetTile t in tiles)
                if (DominionGeo.OnSurface(t)) pts.Add(TerritoryMap.Project(t));
            if (pts.Count == 0) return;
            // Unwrap longitudes round the first point so a pair across the date line is close, not half a world apart.
            float x0 = pts[0].x, minX = x0, maxX = x0, minY = pts[0].y, maxY = pts[0].y;
            foreach (Vector2 p in pts)
            {
                float x = p.x + Mathf.Round(x0 - p.x);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float aspect = local.height > 0f ? local.width / local.height : 2f;
            float needY = Mathf.Max(maxY - minY + 0.12f, (maxX - minX + 0.08f) * 2f / aspect);
            float z = Mathf.Clamp(1f / needY, MinZoom, 4f);
            targetZoom = z;
            targetCentre = new Vector2(Mathf.Repeat((minX + maxX) / 2f, 1f), Mathf.Clamp((minY + maxY) / 2f, 0.5f / z, 1f - 0.5f / z));
        }

        /// <summary>Never wider than the world: a view wider than 2:1 starts zoomed in a little.</summary>
        private float MinZoom => Mathf.Max(1f, local.height > 0f ? local.width / local.height / 2f : 1f);

        private void ClampView()
        {
            zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            float hv = 1f / zoom;
            centre.y = Mathf.Clamp(centre.y, hv / 2f, 1f - hv / 2f);
            centre.x = Mathf.Repeat(centre.x, 1f);
        }

        public Vector2 ToScreen(Vector2 m)
        {
            Vector2 span = Span;
            float x = m.x + Mathf.Round(centre.x - m.x); // the copy of the point nearest the middle of the view
            return new Vector2((x - (centre.x - span.x / 2f)) / span.x * local.width, (m.y - (centre.y - span.y / 2f)) / span.y * local.height);
        }

        public Vector2 ToMap(Vector2 s)
        {
            Vector2 span = Span;
            return new Vector2(Mathf.Repeat(centre.x - span.x / 2f + s.x / local.width * span.x, 1f), centre.y - span.y / 2f + s.y / local.height * span.y);
        }

        private bool Visible(Vector2 s, float margin = 20f) =>
            s.x > -margin && s.y > -margin && s.x < local.width + margin && s.y < local.height + margin;

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// Draws the map. <paramref name="focus"/> and <paramref name="focus2"/> are drawn large; <paramref name="wars"/>
        /// limits war lines and warbands to those wars (null: every war the player knows of). <paramref name="onPick"/>
        /// is called with a settlement's faction when one is clicked.
        /// </summary>
        public void Draw(Rect rect, MapStyle style, Faction focus = null, Faction focus2 = null, List<War> wars = null, Action<Faction> onPick = null)
        {
            if (Find.World == null) return;
            GUI.BeginGroup(rect);
            local = new Rect(0f, 0f, rect.width, rect.height);
            HandleInput();
            Animate();
            ClampView();

            Vector2 span = Span;
            Widgets.DrawBoxSolid(local, new Color(0.08f, 0.11f, 0.15f));
            GUI.color = style == MapStyle.WarRoom ? new Color(0.85f, 0.8f, 0.78f) : Color.white;
            GUI.DrawTextureWithTexCoords(local, TerritoryMap.Texture, new Rect(centre.x - span.x / 2f, 1f - (centre.y + span.y / 2f), span.x, span.y));
            GUI.color = Color.white;
            DrawGraticule(span);

            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            List<War> shownWars = new List<War>();
            if (comp != null)
                foreach (War w in wars ?? comp.Wars.wars)
                    if (TerritoryMap.KnownWar(w)) shownWars.Add(w);

            foreach (War w in shownWars) DrawWarLine(w, focus, focus2, style);
            Settlement hovered = DrawSettlements(focus, focus2, style);
            Warband hoveredBand = DrawWarbands(shownWars, wars != null, style, hovered);
            DrawControls();

            // Hover: a settlement or warband's details, otherwise whose land this is.
            Vector2 mouse = Event.current.mousePosition;
            if (Mouse.IsOver(local))
            {
                if (hovered != null) SettlementHover(hovered, mouse, onPick);
                else if (hoveredBand == null) TerritoryCaption(ToMap(mouse));
            }
            GUI.EndGroup();
            GUI.color = style == MapStyle.WarRoom ? new Color(0.9f, 0.3f, 0.2f, 0.5f) : new Color(1f, 1f, 1f, 0.3f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
        }

        private void HandleInput()
        {
            Event e = Event.current;
            bool over = Mouse.IsOver(local);
            if (e.type == EventType.ScrollWheel && over)
            {
                Vector2 before = ToMap(e.mousePosition);
                zoom *= e.delta.y > 0f ? 0.8f : 1.25f;
                ClampView();
                Vector2 after = ToMap(e.mousePosition);
                centre.x += Mathf.DeltaAngle(after.x * 360f, before.x * 360f) / 360f;
                centre.y += before.y - after.y;
                targetZoom = null;
                targetCentre = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && over)
            {
                dragging = true;
                dragMoved = 0f;
                suppressClick = false;
            }
            else if (e.type == EventType.MouseDrag && dragging)
            {
                Vector2 span = Span;
                dragMoved += e.delta.magnitude;
                centre -= new Vector2(e.delta.x / local.width * span.x, e.delta.y / local.height * span.y);
                targetCentre = null;
            }
            else if (e.rawType == EventType.MouseUp && dragging)
            {
                dragging = false;
                suppressClick = dragMoved > 4f;
            }
            if (e.type == EventType.Repaint && !Input.GetMouseButton(0)) dragging = false;
        }

        private void Animate()
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = Mathf.Clamp01(Time.unscaledDeltaTime * 6f);
            if (targetZoom is float z)
            {
                zoom = Mathf.Lerp(zoom, z, k);
                if (Mathf.Abs(zoom - z) < 0.01f) targetZoom = null;
            }
            if (targetCentre is Vector2 c)
            {
                centre.x += Mathf.DeltaAngle(centre.x * 360f, c.x * 360f) / 360f * k;
                centre.y = Mathf.Lerp(centre.y, c.y, k);
                if (Mathf.Abs(Mathf.DeltaAngle(centre.x * 360f, c.x * 360f)) < 0.2f && Mathf.Abs(centre.y - c.y) < 0.002f) targetCentre = null;
            }
        }

        private bool Clicked(Rect r) => Widgets.ButtonInvisible(r) && !suppressClick;

        private void DrawGraticule(Vector2 span)
        {
            // Every 30 degrees; the equator a little brighter.
            for (int i = 0; i < 12; i++)
            {
                float x = ToScreen(new Vector2(i / 12f, 0.5f)).x;
                if (x < 0f || x > local.width) continue;
                Widgets.DrawBoxSolid(new Rect(x, 0f, 1f, local.height), Graticule);
            }
            for (int i = 1; i < 6; i++)
            {
                float y = ToScreen(new Vector2(centre.x, i / 6f)).y;
                if (y < 0f || y > local.height) continue;
                Widgets.DrawBoxSolid(new Rect(0f, y, local.width, 1f), i == 3 ? new Color(1f, 1f, 1f, 0.1f) : Graticule);
            }
        }

        private void DrawWarLine(War w, Faction focus, Faction focus2, MapStyle style)
        {
            Settlement a = Seat(w.attacker), b = Seat(w.defender);
            if (a == null || b == null || !DominionGeo.OnSurface(a.Tile) || !DominionGeo.OnSurface(b.Tile)) return;
            // Unwrap in map space so a pair across the date line takes the short way round.
            Vector2 ma = TerritoryMap.Project(a.Tile), mb = TerritoryMap.Project(b.Tile);
            mb.x += Mathf.Round(ma.x - mb.x);
            Vector2 span = Span;
            Vector2 pa = ToScreen(ma), pb = pa + new Vector2((mb.x - ma.x) / span.x * local.width, (mb.y - ma.y) / span.y * local.height);
            bool lit = focus == null || w.Involves(focus) || w.Involves(focus2);
            if (!lit && style == MapStyle.Atlas) return;
            DashedLine(pa, pb, new Color(WarLine.r, WarLine.g, WarLine.b, lit ? 0.75f : 0.25f), lit ? 2f : 1f, 8f, 5f, lit ? Time.realtimeSinceStartup * 12f : 0f);
            // Crossed swords halfway.
            Vector2 mid = (pa + pb) / 2f;
            Widgets.DrawLine(mid + new Vector2(-5f, -5f), mid + new Vector2(5f, 5f), WarLine, 2f);
            Widgets.DrawLine(mid + new Vector2(-5f, 5f), mid + new Vector2(5f, -5f), WarLine, 2f);
            TooltipHandler.TipRegion(new Rect(mid.x - 7f, mid.y - 7f, 14f, 14f), w.Label);
        }

        /// <summary>A faction's capital, or for a colony without one yet, any of its settlements.</summary>
        public static Settlement Seat(Faction f)
        {
            Settlement s = DominionUIState.Capital(f);
            if (s != null || f == null) return s;
            foreach (Settlement x in Find.WorldObjects.Settlements)
                if (x.Faction == f) return x;
            return null;
        }

        /// <summary>A line drawn in dashes; <paramref name="phase"/> slides the dashes along it.</summary>
        public static void DashedLine(Vector2 a, Vector2 b, Color c, float width, float dash, float gap, float phase)
        {
            float len = (b - a).magnitude;
            if (len < 1f) return;
            Vector2 dir = (b - a) / len;
            float period = dash + gap;
            for (float t = -Mathf.Repeat(phase, period); t < len; t += period)
            {
                float s = Mathf.Max(0f, t), e = Mathf.Min(len, t + dash);
                if (e > s) Widgets.DrawLine(a + dir * s, a + dir * e, c, width);
            }
        }

        private Settlement DrawSettlements(Faction focus, Faction focus2, MapStyle style)
        {
            Settlement hovered = null;
            float best = 8f * 8f;
            Vector2 mouse = Event.current.mousePosition;
            bool names = zoom >= 2.5f;
            bool allNames = zoom >= 5f;
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || !DominionGeo.OnSurface(s.Tile)) continue;
                Vector2 p = ToScreen(TerritoryMap.Project(s.Tile));
                if (!Visible(p)) continue;
                bool big = s.Faction == focus || s.Faction == focus2;
                bool player = s.Faction.IsPlayer;
                bool capital = DominionUIState.Capital(s.Faction) == s;
                float size = (player ? 6f : big ? 7f : 4f) * Mathf.Lerp(1f, 1.6f, (zoom - 1f) / (MaxZoom - 1f));
                Rect r = new Rect(p.x - size / 2f, p.y - size / 2f, size, size);
                Color c = player ? Color.white : s.Faction.Color;
                if (!big && !player && focus != null) c.a = 0.75f;
                Widgets.DrawBoxSolid(r.ExpandedBy(1f), new Color(0f, 0f, 0f, 0.8f));
                Widgets.DrawBoxSolid(r, c);
                if (capital)
                {
                    GUI.color = big || player ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                    Widgets.DrawBox(r.ExpandedBy(3f));
                    GUI.color = Color.white;
                }
                if ((capital && names) || allNames || (big && capital))
                {
                    Text.Font = GameFont.Tiny;
                    float w = Text.CalcSize(s.Label).x;
                    Rect lr = new Rect(p.x - w / 2f, r.yMax + 2f, w, 16f);
                    Widgets.DrawBoxSolid(lr.ExpandedBy(2f, 0f), new Color(0f, 0f, 0f, 0.45f));
                    GUI.color = style == MapStyle.WarRoom ? new Color(1f, 0.85f, 0.7f) : new Color(0.95f, 0.95f, 0.9f);
                    Widgets.Label(lr, s.Label);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                float d = (p - mouse).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    hovered = s;
                }
            }
            return hovered;
        }

        private void SettlementHover(Settlement s, Vector2 mouse, Action<Faction> onPick)
        {
            Vector2 p = ToScreen(TerritoryMap.Project(s.Tile));
            Rect hit = new Rect(p.x - 8f, p.y - 8f, 16f, 16f);
            Widgets.DrawBox(hit.ContractedBy(2f));
            string tip = s.Label + "\n" + s.Faction.Name + (DominionUIState.Capital(s.Faction) == s ? "  ·  " + "Dominion_Map_Capital".Translate() : "")
                         + "\n\n" + (onPick != null ? "Dominion_Map_ClickPick".Translate() + "\n" : "") + "Dominion_Map_RightClick".Translate();
            TooltipHandler.TipRegion(hit, tip);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 1 && hit.Contains(e.mousePosition))
            {
                e.Use();
                SettlementMenu(s);
                return;
            }
            if (Clicked(hit))
            {
                if (onPick != null)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    onPick(s.Faction);
                }
                else SettlementMenu(s);
            }
        }

        private static void SettlementMenu(Settlement s)
        {
            Faction f = s.Faction;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Dominion_UI_Jump_Map".Translate() + ": " + s.Label, () => CameraJumper.TryJumpAndSelect(s)),
            };
            if (!f.IsPlayer)
            {
                options.Add(new FloatMenuOption("Dominion_UI_Jump_Dossier".Translate() + ": " + f.Name, () => DominionUIState.Show(DominionTab.Overview, f)));
                options.Add(new FloatMenuOption("Dominion_UI_Jump_Diplomacy".Translate() + ": " + f.Name, () => DominionUIState.Show(DominionTab.Diplomacy, f)));
                string why = Dialog_Audience.CannotHoldReason(f);
                options.Add(new FloatMenuOption("Dominion_Audience_Button".Translate() + ": " + f.Name + (why != null ? " (" + why + ")" : ""),
                    why == null ? (System.Action)(() => Dialog_Audience.Request(f)) : null));
                options.Add(new FloatMenuOption("Dominion_UI_Jump_War".Translate() + ": " + f.Name, () => DominionUIState.Show(DominionTab.War, f)));
                options.Add(new FloatMenuOption("Dominion_UI_Jump_Intel".Translate() + ": " + f.Name, () => DominionUIState.Show(DominionTab.Intelligence, f)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void TerritoryCaption(Vector2 m)
        {
            Faction owner = TerritoryMap.OwnerAt(m);
            string text = owner != null ? "Dominion_Map_TerritoryOf".Translate(owner.Name).ToString()
                : TerritoryMap.IsLand(m) ? "Dominion_Map_Unclaimed".Translate().ToString() : "Dominion_Map_Sea".Translate().ToString();
            Text.Font = GameFont.Tiny;
            float w = Text.CalcSize(text).x + 10f;
            Rect r = new Rect(4f, local.height - 20f, w, 16f);
            Widgets.DrawBoxSolid(r, new Color(0f, 0f, 0f, 0.6f));
            if (owner != null) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), owner.IsPlayer ? Color.white : owner.Color);
            Widgets.Label(new Rect(r.x + 6f, r.y, w, 16f), text);
            Text.Font = GameFont.Small;
        }

        private Warband DrawWarbands(List<War> wars, bool onlyThese, MapStyle style, Settlement settlementHovered)
        {
            Warband hovered = null;
            float phase = Time.realtimeSinceStartup * 18f;
            foreach (Warband w in WarbandUtility.All())
            {
                if (!w.VisibleToPlayer || !DominionGeo.OnSurface(w.Tile)) continue;
                if (onlyThese && !wars.Exists(x => x.Involves(w.Faction) && x.Involves(w.target))) continue;
                bool atUs = w.target != null && w.target.IsPlayer;
                Color c = atUs ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.8f, 0.3f);

                // The road ahead, marching.
                Vector2 prev = ToScreen(TerritoryMap.Project(w.Tile));
                Vector2 from = prev;
                Vector2 next = prev;
                bool first = true;
                foreach (PlanetTile t in w.RouteAhead(3))
                {
                    Vector2 p = ToScreen(TerritoryMap.Project(t));
                    if (Mathf.Abs(p.x - prev.x) < local.width * 0.5f && (Visible(p, 200f) || Visible(prev, 200f)))
                        DashedLine(prev, p, new Color(c.r, c.g, c.b, 0.65f), style == MapStyle.WarRoom ? 2f : 1.5f, 5f, 4f, phase);
                    if (first && (p - from).sqrMagnitude > 4f)
                    {
                        next = p;
                        first = false;
                    }
                    prev = p;
                }
                if (!Visible(from)) continue;

                // The token: a chip in the faction's colour, pointing the way it is going.
                float size = style == MapStyle.WarRoom ? 14f : 10f;
                Rect chip = new Rect(from.x - size / 2f, from.y - size / 2f, size, size);
                Widgets.DrawBoxSolid(chip.ExpandedBy(1.5f), Color.black);
                Widgets.DrawBoxSolid(chip, w.Faction.Color);
                if (atUs)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 5f);
                    GUI.color = new Color(1f, 0.3f, 0.2f, pulse);
                    Widgets.DrawBox(chip.ExpandedBy(4f), 2);
                    GUI.color = Color.white;
                }
                Vector2 dir = (next - from).sqrMagnitude > 1f ? (next - from).normalized : Vector2.right;
                Vector2 side = new Vector2(-dir.y, dir.x);
                Vector2 tip = from + dir * (size * 0.5f + 7f);
                Widgets.DrawLine(tip, tip - dir * 6f + side * 4f, c, 2f);
                Widgets.DrawLine(tip, tip - dir * 6f - side * 4f, c, 2f);

                Rect hit = chip.ExpandedBy(4f);
                if (Mouse.IsOver(hit)) hovered = w;
                string eta = w.Blockading ? "Dominion_Warband_BlockadingShort".Translate().ToString()
                    : "Dominion_Warband_Arrives".Translate(w.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)).ToString();
                TooltipHandler.TipRegion(hit, w.Label + "\n" + w.MissionText + "\n" + eta + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                // A warband sitting on a town does not steal the town's click.
                if (settlementHovered == null && Clicked(hit)) CameraJumper.TryJumpAndSelect(w);
            }
            return hovered;
        }

        private void DrawControls()
        {
            float s = 22f;
            Rect plus = new Rect(local.width - s - 4f, 4f, s, s);
            Rect minus = new Rect(plus.x, plus.yMax + 2f, s, s);
            Rect home = new Rect(plus.x, minus.yMax + 2f, s, s);
            if (MapButton(plus, "+", "Dominion_Map_ZoomIn".Translate()))
            {
                targetZoom = Mathf.Min(MaxZoom, (targetZoom ?? zoom) * 1.6f);
            }
            if (MapButton(minus, "−", "Dominion_Map_ZoomOut".Translate()))
            {
                targetZoom = Mathf.Max(MinZoom, (targetZoom ?? zoom) / 1.6f);
            }
            if (MapButton(home, "⌂", "Dominion_Map_Reset".Translate())) Reset();
            TooltipHandler.TipRegion(new Rect(0f, 0f, 60f, 20f), "Dominion_Map_Help".Translate());
            if (zoom > 1.05f)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(new Rect(4f, 2f, 80f, 16f), "×" + zoom.ToString("F1"));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
        }

        private static bool MapButton(Rect r, string label, string tip)
        {
            Widgets.DrawBoxSolid(r, Mouse.IsOver(r) ? new Color(0.25f, 0.25f, 0.25f, 0.9f) : new Color(0f, 0f, 0f, 0.6f));
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            Widgets.DrawBox(r);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(r, tip);
            if (!Widgets.ButtonInvisible(r)) return false;
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            return true;
        }
    }
}
