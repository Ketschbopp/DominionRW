using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The War tab, as a war room: a bunker lit by a red lamp, the campaign map on the table (spheres of influence,
    /// fronts in red, warbands marching along their roads), and a rack of dossiers on the wall, one per war. Pick a
    /// dossier to put that war on the map and open it: the tug of war, weariness and casualties, your stance switches
    /// and orders, the peace table, and the war's chronicle. The last dossier is the theatre report: cold wars,
    /// warbands in the field, occupied settlements and blockades.
    /// </summary>
    public class WarPanel
    {
        private const float RackWidth = 240f;
        private const float FolderHeight = 58f;
        private const float HeaderHeight = 34f;
        private const int TheatreId = -1;

        private static readonly Color Stencil = new Color(0.95f, 0.85f, 0.7f);
        private static readonly Color Dim = new Color(0.75f, 0.68f, 0.6f);
        private static readonly Color Red = new Color(0.9f, 0.25f, 0.18f);

        private readonly WorldMapView map = new WorldMapView();
        private int selectedId = int.MinValue;
        private int focusedId = int.MinValue;
        private Vector2 rackScroll;
        private Vector2 dossierScroll;
        private float dossierHeight = 400f;

        // Terms are worked out from the whole world state; recomputed at most twice a second.
        private readonly Dictionary<int, string> termsCache = new Dictionary<int, string>();
        private float termsTime;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            if (Time.realtimeSinceStartup > termsTime)
            {
                termsCache.Clear();
                termsTime = Time.realtimeSinceStartup + 0.5f;
            }

            // The room: bare concrete, a red lamp over the table, dark corners.
            DominionRooms.DrawTiled(rect, DominionRooms.Concrete, 0.6f);
            DominionRooms.DrawGlow(new Vector2(rect.center.x - RackWidth / 2f, rect.y + rect.height * 0.3f), rect.width * 0.5f, new Color(0.9f, 0.15f, 0.08f, 0.22f));
            DominionRooms.DrawVignette(rect, 0.8f);

            List<War> mine = comp.Wars.wars.Where(w => w.InvolvesPlayer).ToList();
            List<War> world = comp.Wars.wars.Where(w => !w.InvolvesPlayer).ToList();
            War selected = Pick(mine, world);

            Rect inner = rect.ContractedBy(8f);
            DrawHeader(new Rect(inner.x, inner.y, inner.width, HeaderHeight), mine, world);
            Rect body = new Rect(inner.x, inner.y + HeaderHeight + 6f, inner.width, inner.height - HeaderHeight - 6f);
            Rect rack = new Rect(body.xMax - RackWidth, body.y, RackWidth, body.height);
            Rect table = new Rect(body.x, body.y, body.width - RackWidth - 10f, body.height);

            // The campaign map takes the top of the table; the open dossier lies below it.
            Rect mapRect = new Rect(table.x, table.y, table.width, Mathf.Min(Mathf.Max(220f, table.height * 0.52f), table.height - 160f));
            if (focusedId != selectedId)
            {
                focusedId = selectedId;
                if (selected != null) map.FocusOn(WarTiles(selected));
                else map.Reset();
            }
            map.Draw(mapRect, MapStyle.WarRoom, selected?.attacker, selected?.defender, selected != null ? new List<War> { selected } : null,
                f => PickWarWith(f, mine, world));
            Rect dossier = new Rect(table.x, mapRect.yMax + 8f, table.width, table.yMax - mapRect.yMax - 8f);
            if (selected != null) DrawDossier(dossier, selected, comp);
            else DrawTheatre(dossier, comp);

            DrawRack(rack, mine, world);
        }

        /// <summary>The selected war, or the theatre report; keeps the choice valid as wars end.</summary>
        private War Pick(List<War> mine, List<War> world)
        {
            Faction focus = DominionUIState.selected;
            if (selectedId == int.MinValue)
            {
                War first = mine.FirstOrDefault(w => w.Involves(focus)) ?? mine.FirstOrDefault() ?? world.FirstOrDefault(w => w.Involves(focus) && Known(w));
                selectedId = first?.id ?? TheatreId;
            }
            if (selectedId == TheatreId) return null;
            War found = mine.Concat(world).FirstOrDefault(w => w.id == selectedId);
            if (found == null) selectedId = TheatreId;
            return found;
        }

        /// <summary>A town clicked on the map opens the dossier of a war its faction is in, yours first.</summary>
        private void PickWarWith(Faction f, List<War> mine, List<War> world)
        {
            if (f.IsPlayer) return;
            DominionUIState.selected = f;
            War w = mine.FirstOrDefault(x => x.Involves(f)) ?? world.FirstOrDefault(x => x.Involves(f) && Known(x));
            if (w == null || w.id == selectedId) return;
            selectedId = w.id;
            dossierScroll = Vector2.zero;
        }

        private static IEnumerable<PlanetTile> WarTiles(War w)
        {
            Settlement a = WorldMapView.Seat(w.attacker), b = WorldMapView.Seat(w.defender);
            if (a != null) yield return a.Tile;
            if (b != null) yield return b.Tile;
            foreach (Warband band in WarbandUtility.All())
                if (band.VisibleToPlayer && w.Involves(band.Faction) && w.Involves(band.target)) yield return band.Tile;
        }

        private static bool Known(War w) => w.InvolvesPlayer || IntelUtility.IntelOn(w.attacker, w.defender) >= IntelUtility.RestrictedIntel;

        // ------------------------------------------------------------------ header

        private static void DrawHeader(Rect rect, List<War> mine, List<War> world)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.45f));
            GUI.color = new Color(Red.r, Red.g, Red.b, 0.6f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            // A red lamp that blinks while anyone marches on you.
            int atUs = WarbandUtility.All().Count(b => b.VisibleToPlayer && b.target != null && b.target.IsPlayer);
            Rect lamp = new Rect(rect.x + 8f, rect.y + 9f, 16f, 16f);
            bool on = atUs == 0 || Time.realtimeSinceStartup % 1f < 0.6f;
            Widgets.DrawBoxSolid(lamp, atUs > 0 ? (on ? Red : new Color(0.3f, 0.08f, 0.05f)) : new Color(0.25f, 0.5f, 0.25f));
            if (atUs > 0 && on) DominionRooms.DrawGlow(lamp.center, 26f, new Color(1f, 0.2f, 0.1f, 0.5f));
            TooltipHandler.TipRegion(lamp, atUs > 0 ? "Dominion_WarRoom_Alarm".Translate(atUs) : "Dominion_WarRoom_Quiet".Translate());
            DominionTheme.InkLabel(new Rect(lamp.xMax + 10f, rect.y, 300f, rect.height), "Dominion_WarRoom_Title".Translate().ToString().ToUpper(), Stencil, GameFont.Medium, TextAnchor.MiddleLeft);
            string status = "Dominion_WarRoom_Status".Translate(mine.Count, world.Count(Known), atUs);
            DominionTheme.InkLabel(new Rect(rect.x + 320f, rect.y, rect.width - 330f, rect.height), status, Dim, GameFont.Small, TextAnchor.MiddleRight);
        }

        // ------------------------------------------------------------------ the rack of dossiers

        private void DrawRack(Rect rect, List<War> mine, List<War> world)
        {
            // A board on the wall, with the dossiers clipped to it.
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.1f, 0.09f, 0.85f));
            DominionRooms.DrawMoulding(rect, new Color(0.5f, 0.4f, 0.3f, 0.4f));
            Rect inner = rect.ContractedBy(8f);
            var known = world.Where(Known).OrderBy(w => w.Involves(DominionUIState.selected) ? 0 : 1).ToList();
            int unknown = world.Count - known.Count;
            float h = 22f + Mathf.Max(1, mine.Count) * (FolderHeight + 4f) + 26f + Mathf.Max(1, known.Count) * (FolderHeight + 4f) + (unknown > 0 ? 22f : 0f) + 30f + FolderHeight + 10f;
            Rect view = new Rect(0f, 0f, inner.width - (h > inner.height ? 16f : 0f), h);
            Widgets.BeginScrollView(inner, ref rackScroll, view);
            float y = 0f;
            RackHeading(ref y, view.width, "Dominion_WarTab_Yours".Translate());
            if (mine.Count == 0) RackNone(ref y, view.width, "Dominion_WarRoom_NoWarsYours".Translate());
            foreach (War w in mine) Folder(ref y, view.width, w.Label, w, w.id);
            y += 4f;
            RackHeading(ref y, view.width, "Dominion_WarTab_World".Translate());
            if (known.Count == 0) RackNone(ref y, view.width, "Dominion_None".Translate());
            foreach (War w in known) Folder(ref y, view.width, w.Label, w, w.id);
            if (unknown > 0)
            {
                DominionTheme.InkLabel(new Rect(4f, y, view.width, 20f), "Dominion_WarRoom_Unknown".Translate(unknown), Dim, GameFont.Tiny);
                y += 22f;
            }
            y += 8f;
            RackHeading(ref y, view.width, "Dominion_WarRoom_Reports".Translate());
            Folder(ref y, view.width, "Dominion_WarRoom_Theatre".Translate(), null, TheatreId);
            Widgets.EndScrollView();
        }

        private static void RackHeading(ref float y, float width, string text)
        {
            DominionTheme.InkLabel(new Rect(2f, y, width, 20f), text.ToUpper(), Stencil, GameFont.Tiny);
            Widgets.DrawBoxSolid(new Rect(2f, y + 18f, width - 4f, 1f), new Color(Stencil.r, Stencil.g, Stencil.b, 0.3f));
            y += 22f;
        }

        private static void RackNone(ref float y, float width, string text)
        {
            DominionTheme.InkLabel(new Rect(6f, y, width - 6f, FolderHeight), text, Dim, GameFont.Tiny);
            y += FolderHeight + 4f;
        }

        /// <summary>A manila dossier clipped to the rack. The open one sticks out; hover lifts it a little.</summary>
        private void Folder(ref float y, float width, string label, War w, int id)
        {
            bool open = selectedId == id;
            Rect slot = new Rect(0f, y, width, FolderHeight);
            bool over = Mouse.IsOver(slot);
            Rect f = new Rect(open ? 0f : 10f, y + (over && !open ? -2f : 0f), width - 10f, FolderHeight);
            GUI.color = open ? Color.white : over ? new Color(0.95f, 0.93f, 0.88f) : new Color(0.82f, 0.8f, 0.75f);
            // Only the top of the folder shows: its tab and the start of the front cover.
            GUI.DrawTextureWithTexCoords(f, DominionRooms.Folder, new Rect(0f, 1f - FolderHeight / 300f * 1.4f, 1f, FolderHeight / 300f * 1.4f));
            GUI.color = Color.white;
            // A clip at the top.
            Widgets.DrawBoxSolid(new Rect(f.xMax - 30f, f.y - 2f, 14f, 8f), new Color(0.6f, 0.6f, 0.62f));
            Rect text = new Rect(f.x + 8f, f.y + 12f, f.width - 16f, 20f);
            if (w != null)
            {
                Widgets.DrawBoxSolid(new Rect(f.x + 4f, f.y + 2f, 26f, 6f), w.attacker.Color);
                Widgets.DrawBoxSolid(new Rect(f.x + 32f, f.y + 2f, 26f, 6f), w.defender.Color);
                DominionTheme.InkLabel(text, label.Truncate(text.width), DominionTheme.Ink, GameFont.Tiny);
                Rect bar = new Rect(f.x + 8f, f.y + 32f, f.width - 16f, 14f);
                if (Known(w)) DrawScoreBar(bar, w);
                if (w.InvolvesPlayer && WarbandUtility.All().Any(b => b.VisibleToPlayer && b.target != null && b.target.IsPlayer && b.Faction == w.Other(Faction.OfPlayer)))
                    Widgets.DrawBoxSolid(new Rect(f.xMax - 12f, f.y + 12f, 6f, 6f), Red);
            }
            else
            {
                DominionTheme.InkLabel(text, label.Truncate(text.width), DominionTheme.Ink, GameFont.Small);
                DominionTheme.InkLabel(new Rect(text.x, text.y + 18f, text.width, 18f), "Dominion_WarRoom_Theatre_Sub".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
            }
            TooltipHandler.TipRegion(slot, w != null ? w.Label + "\n" + WarUtility.GoalDescription(w.goal) + "\n\n" + "Dominion_WarRoom_OpenDossier".Translate()
                : (string)"Dominion_WarRoom_Theatre_Desc".Translate());
            if (Widgets.ButtonInvisible(slot))
            {
                if (selectedId != id) dossierScroll = Vector2.zero;
                selectedId = id;
                SoundDefOf.PageChange.PlayOneShotOnCamera();
            }
            y += FolderHeight + 4f;
        }

        // ------------------------------------------------------------------ the open dossier

        private void DrawDossier(Rect rect, War w, WorldComponent_Dominion comp)
        {
            DominionPaper.DrawParchment(rect, border: false);
            GUI.color = DominionPaper.Edge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            bool known = Known(w);
            if (w.InvolvesPlayer)
                DominionTheme.DrawStamp(new Rect(rect.xMax - 136f, rect.y + 6f, 120f, 40f), DominionPaper.StampTopSecret, DominionTheme.InkRed, 6f);

            Rect inner = rect.ContractedBy(12f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, dossierHeight));
            Widgets.BeginScrollView(inner, ref dossierScroll, view);
            float x = 0f, y = 0f, width = view.width;

            // Title and goal.
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(x, y + 2f, 24f, 24f), w.attacker);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(x + 26f, y + 2f, 24f, 24f), w.defender);
            DominionTheme.InkLabel(new Rect(x + 56f, y, width - 200f, 30f), w.Label.Truncate(width - 200f), DominionTheme.Ink, GameFont.Medium);
            y += 30f;
            string goal = "Dominion_WarTab_Goal".Translate(WarUtility.GoalLabel(w.goal)) + "  ·  " + "Dominion_WarRoom_Since".Translate(w.DurationTicks.ToStringTicksToPeriod(allowSeconds: false));
            if (w.alliedTo != null) goal += "  ·  " + "Dominion_WarTab_JoinedFor".Translate(w.alliedTo.Name);
            Rect goalRect = new Rect(x, y, width, 20f);
            DominionTheme.InkLabel(goalRect, goal.Truncate(width), DominionTheme.InkFaded, GameFont.Tiny);
            TooltipHandler.TipRegion(goalRect, WarUtility.GoalDescription(w.goal));
            y += 22f;

            if (!known)
            {
                DominionTheme.InkLabel(new Rect(x, y, width, 40f), IntelUtility.Unknown(IntelUtility.RestrictedIntel), DominionTheme.InkFaded);
                dossierHeight = y + 50f;
                Widgets.EndScrollView();
                return;
            }

            // The tug of war.
            DrawRope(new Rect(x, y, width, 46f), w);
            y += 52f;

            // Gauges: their weariness, the dead on both sides, battles.
            Faction them = w.InvolvesPlayer ? w.Other(Faction.OfPlayer) : w.defender;
            Faction us = w.InvolvesPlayer ? Faction.OfPlayer : w.attacker;
            float col = width / 3f;
            FactionState ts = DominionScheduler.IsSimulated(them) ? comp.GetState(them) : null;
            if (ts != null)
            {
                Rect wr = new Rect(x, y, col - 10f, 34f);
                InkGauge(wr, "Dominion_WarTab_Weariness".Translate(them.Name.Truncate(90f)), ts.warWeariness / 100f, ts.warWeariness.ToString("F0"));
                TooltipHandler.TipRegion(wr, "Dominion_WarTab_Weariness_Desc".Translate(them.Name));
            }
            Rect cas = new Rect(x + col, y, col - 10f, 34f);
            DominionTheme.InkLabel(new Rect(cas.x, cas.y, cas.width, 16f), "Dominion_WarRoom_Dead".Translate().ToString().ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(cas.x, cas.y + 15f, cas.width, 20f), us.Name.Truncate(cas.width * 0.5f) + " " + w.DeadOf(us) + "  ·  " + them.Name.Truncate(cas.width * 0.5f) + " " + w.DeadOf(them), DominionTheme.Ink, GameFont.Tiny);
            TooltipHandler.TipRegion(cas, "Dominion_WarTab_Casualties_Desc".Translate(w.DurationTicks.ToStringTicksToPeriod(allowSeconds: false), w.battles));
            Rect bat = new Rect(x + col * 2f, y, col, 34f);
            DominionTheme.InkLabel(new Rect(bat.x, bat.y, bat.width, 16f), "Dominion_WarRoom_Battles".Translate().ToString().ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(bat.x, bat.y + 15f, bat.width, 20f), w.battles + "  ·  " + "Dominion_WarRoom_Occupied".Translate(PeaceTerms.OccupiedBetween(w).Count), DominionTheme.Ink, GameFont.Tiny);
            y += 40f;

            if (w.InvolvesPlayer)
            {
                // Peace now, in one line; the full terms on hover.
                if (!termsCache.TryGetValue(w.id, out string terms))
                    termsCache[w.id] = terms = PeaceTerms.Describe(w);
                Rect termsRow = new Rect(x, y, width, 20f);
                DominionTheme.InkLabel(termsRow, ("Dominion_WarTab_PeaceNow".Translate() + "  " + terms.Split('\n').FirstOrDefault()).Truncate(width), DominionTheme.Ink, GameFont.Tiny);
                TooltipHandler.TipRegion(termsRow, terms);
                y += 24f;
                y = DrawStance(x, y, width, w);
                y = DrawOrders(x, y, width, w, comp);
            }

            // The chronicle, newest first, typed on the sheet.
            DominionPaper.Caption(new Rect(x, y, width, 16f), "Dominion_WarRoom_Chronicle".Translate());
            y += 18f;
            y = DrawChronicle(x, y, width, w);
            dossierHeight = y + 10f;
            Widgets.EndScrollView();
        }

        /// <summary>
        /// A rope pulled between the two sides: the knot sits where the score is, the flags at the ends are the
        /// factions, and the marks near each end are where the leader can dictate the peace.
        /// </summary>
        private static void DrawRope(Rect rect, War w)
        {
            float mid = rect.center.x;
            float ry = rect.y + 24f;
            float half = rect.width / 2f - 40f;
            // Flags at the ends.
            Rect fa = new Rect(rect.xMax - 32f, ry - 14f, 28f, 28f), fd = new Rect(rect.x + 4f, ry - 14f, 28f, 28f);
            GUI.color = w.attacker.Color;
            GUI.DrawTexture(fa, w.attacker.def.FactionIcon);
            GUI.color = w.defender.Color;
            GUI.DrawTexture(fd, w.defender.def.FactionIcon);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(fa, w.attacker.Name);
            TooltipHandler.TipRegion(fd, w.defender.Name);
            // The rope: a thick line with a twist drawn across it.
            Color rope = new Color(0.55f, 0.42f, 0.25f);
            Widgets.DrawLine(new Vector2(mid - half, ry), new Vector2(mid + half, ry), rope, 5f);
            for (float t = mid - half; t < mid + half; t += 6f)
                Widgets.DrawLine(new Vector2(t, ry - 2f), new Vector2(t + 3f, ry + 2f), new Color(0.35f, 0.26f, 0.15f), 1f);
            // Centre line and the decisive marks.
            float d = half * PeaceTerms.DecisiveScore / 100f;
            GUI.color = DominionTheme.InkFaded;
            Widgets.DrawLineVertical(mid, ry - 12f, 24f);
            GUI.color = DominionTheme.InkRed;
            Widgets.DrawLineVertical(mid - d, ry - 9f, 18f);
            Widgets.DrawLineVertical(mid + d, ry - 9f, 18f);
            GUI.color = Color.white;
            // The knot, eased towards the score so a change is seen to move.
            float k = mid + half * Mathf.Clamp(w.score, -100f, 100f) / 100f;
            Faction leader = w.Leader();
            Rect knot = new Rect(k - 8f, ry - 8f, 16f, 16f);
            Widgets.DrawBoxSolid(knot.ExpandedBy(1f), DominionTheme.Ink);
            Widgets.DrawBoxSolid(knot, leader?.Color ?? new Color(0.7f, 0.65f, 0.55f));
            string label = leader == null ? "Dominion_WarTab_Even".Translate(w.score.ToString("F0")).ToString()
                : "Dominion_WarTab_Ahead".Translate(leader.Name, Mathf.Abs(w.score).ToString("F0")).ToString();
            DominionTheme.InkLabel(new Rect(rect.x, rect.y, rect.width, 14f), label, DominionTheme.Ink, GameFont.Tiny, TextAnchor.UpperCenter);
            TooltipHandler.TipRegion(rect, "Dominion_WarTab_Score_Desc".Translate(w.attacker.Name, w.defender.Name));
        }

        private static void InkGauge(Rect r, string label, float frac, string value)
        {
            DominionTheme.InkLabel(new Rect(r.x, r.y, r.width, 16f), label.ToUpper(), DominionTheme.InkRed, GameFont.Tiny);
            Rect bar = new Rect(r.x, r.y + 17f, r.width, 12f);
            Widgets.DrawBoxSolid(bar, new Color(0.3f, 0.24f, 0.17f, 0.15f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(frac), bar.height), new Color(DominionTheme.InkRed.r, DominionTheme.InkRed.g, DominionTheme.InkRed.b, 0.55f));
            GUI.color = DominionPaper.Edge;
            Widgets.DrawBox(bar);
            GUI.color = Color.white;
            DominionTheme.InkLabel(bar, value, DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleCenter);
        }

        /// <summary>The stance switches: a row of toggles, the one in force lit green.</summary>
        private static float DrawStance(float x, float y, float width, War w)
        {
            DominionPaper.Caption(new Rect(x, y, width, 16f), "Dominion_WarTab_Stance".Translate());
            y += 16f;
            var stances = System.Enum.GetValues(typeof(WarStance)).Cast<WarStance>().ToList();
            float sw = width / stances.Count;
            for (int i = 0; i < stances.Count; i++)
            {
                WarStance s = stances[i];
                Rect b = new Rect(x + i * sw + 2f, y, sw - 4f, 30f);
                bool current = w.stance == s;
                string why = current ? null : WarOrders.CannotChangeStanceReason(w, s);
                string tip = WarOrders.StanceDesc(s) + (current ? "" : "\n\n" + "Dominion_Stance_Cost".Translate(WarOrders.StanceCost.ToString("F0")));
                if (DominionRooms.BrassButton(b, "   " + WarOrders.StanceLabel(s), tip, why, current) && !current) WarOrders.SetStance(w, s);
                // The lamp on the switch.
                Rect lamp = new Rect(b.x + 8f, b.center.y - 4f, 8f, 8f);
                Widgets.DrawBoxSolid(lamp, current ? new Color(0.3f, 0.95f, 0.35f) : new Color(0.15f, 0.2f, 0.15f));
                if (current) DominionRooms.DrawGlow(lamp.center, 12f, new Color(0.3f, 1f, 0.35f, 0.5f));
            }
            return y + 36f;
        }

        private float DrawOrders(float x, float y, float width, War w, WorldComponent_Dominion comp)
        {
            DominionPaper.Caption(new Rect(x, y, width, 16f), "Dominion_WarRoom_Orders".Translate());
            y += 16f;
            Faction enemy = w.Other(Faction.OfPlayer);
            float bw = width / 3f;
            Rect peaceB = new Rect(x + 2f, y, bw - 4f, 30f);
            DiplomacyActionDef peace = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace");
            if (peace != null)
            {
                string why = DiplomacyActions.CannotUseReason(peace, enemy);
                float? chance = why == null ? peace.Worker.AcceptanceChance(enemy) : null;
                string label = "Dominion_WarTab_PeaceTable".Translate() + (chance.HasValue ? " (" + chance.Value.ToStringPercent() + ")" : "");
                if (DominionRooms.BrassButton(peaceB, label, "Dominion_WarTab_PeaceTable_Desc".Translate(), why))
                    Find.WindowStack.Add(new Dialog_PeaceTable(w));
            }
            Rect mercB = new Rect(x + bw + 2f, y, bw - 4f, 30f);
            if (DominionRooms.BrassButton(mercB, "Dominion_WarTab_Mercenaries".Translate(), "Dominion_WarTab_Mercenaries_Desc".Translate(DominionSettings.PlayerCooldownDays(WarOrders.MercenaryCooldownDays))))
                MercenaryMenu(w);
            Rect callB = new Rect(x + bw * 2f + 2f, y, bw - 4f, 30f);
            DiplomacyActionDef call = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_CallToArms");
            if (call != null && DominionRooms.BrassButton(callB, call.LabelCap, call.description)) CallToArmsMenu(call, enemy, comp);
            y += 36f;

            // Their warbands in the field, one line each: click to find it.
            foreach (Warband band in WarbandUtility.All())
            {
                if (!band.VisibleToPlayer || !w.Involves(band.Faction) || !w.Involves(band.target)) continue;
                Rect r = new Rect(x, y, width, 20f);
                if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, DominionTheme.PaperShade);
                string eta = band.Blockading ? "Dominion_Warband_BlockadingShort".Translate().ToString() : band.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false);
                bool atUs = band.target != null && band.target.IsPlayer;
                DominionTheme.InkLabel(r, ("▸ " + band.Faction.Name + " → " + (band.targetSettlement?.Label ?? band.target?.Name ?? "?") + "  (" + eta + ")").Truncate(width),
                    atUs ? DominionTheme.InkRed : DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                TooltipHandler.TipRegion(r, band.MissionText + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                if (Widgets.ButtonInvisible(r)) CameraJumper.TryJumpAndSelect(band);
                y += 20f;
            }
            return y + 6f;
        }

        private static void MercenaryMenu(War w)
        {
            var options = new List<FloatMenuOption>();
            foreach (int silver in WarOrders.MercenaryOffers)
            {
                int s = silver;
                string why = WarOrders.CannotHireReason(w, s);
                string label = "Dominion_Mercenaries_Option".Translate(s, (s / 100f).ToString("F0"));
                options.Add(why == null ? new FloatMenuOption(label, () => WarOrders.HireMercenaries(w, s)) : new FloatMenuOption(label + " (" + why + ")", null));
            }
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_Mercenaries_Title".Translate()));
        }

        private static void CallToArmsMenu(DiplomacyActionDef call, Faction enemy, WorldComponent_Dominion comp)
        {
            var options = new List<FloatMenuOption>();
            foreach (Faction f in comp.Cache.Simulated)
            {
                if (f == enemy || f.HostileTo(Faction.OfPlayer) || f.HostileTo(enemy)) continue;
                string why = DiplomacyActions.CannotUseReason(call, f);
                Faction cf = f;
                options.Add(why == null
                    ? new FloatMenuOption("Dominion_CallToArms_Ask".Translate(f.Name, DiplomacyAction_CallToArms.Acceptance(f, enemy).ToStringPercent()),
                        () => call.Worker.Execute(cf))
                    : new FloatMenuOption(f.Name + " (" + why + ")", null));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>The war's chronicle, newest first, with the score after each entry. Click a line to read the story.</summary>
        private static float DrawChronicle(float x, float y, float width, War w)
        {
            int now = Find.TickManager.TicksGame;
            NewsLog news = WorldComponent_Dominion.Instance.News;
            float intel = w.InvolvesPlayer ? float.MaxValue : IntelUtility.IntelOn(w.attacker, w.defender);
            int shown = 0;
            for (int i = w.chronicle.Count - 1; i >= 0; i--)
            {
                WarChronicleEntry e = w.chronicle[i];
                if (intel < IntelUtility.RequiredIntel(e.secrecy)) continue;
                shown++;
                Rect r = new Rect(x, y, width, 20f);
                if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, DominionTheme.PaperShade);
                DominionTheme.InkLabel(new Rect(r.x, r.y, 90f, r.height), "Dominion_News_Ago".Translate((now - e.tick).ToStringTicksToPeriod(allowSeconds: false)), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.MiddleLeft);
                Faction ahead = e.score >= 0f ? w.attacker : w.defender;
                string score = Mathf.Abs(e.score) < 1f ? "0" : ahead?.Name.Truncate(60f) + " +" + Mathf.Abs(e.score).ToString("F0");
                bool good = ahead != null && ahead.IsPlayer;
                DominionTheme.InkLabel(new Rect(r.xMax - 110f, r.y, 110f, r.height), score, good ? DominionTheme.InkGreen : DominionTheme.InkRed, GameFont.Tiny, TextAnchor.MiddleRight);
                Rect text = new Rect(r.x + 92f, r.y, r.width - 206f, r.height);
                DominionTheme.InkLabel(text, e.text.Truncate(text.width), DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                NewsItem story = e.eventId >= 0 ? FindStory(news, e.eventId) : null;
                TooltipHandler.TipRegion(r, e.text + "\n\n" + (story != null ? "Dominion_WarTab_ReadStory".Translate().ToString() : "Dominion_WarTab_NoStory".Translate().ToString()));
                if (story != null && Widgets.ButtonInvisible(r)) DominionUIState.ShowNews(story);
                y += 20f;
            }
            if (shown == 0)
            {
                DominionTheme.InkLabel(new Rect(x, y, width, 20f), "Dominion_WarTab_ChronicleEmpty".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                y += 20f;
            }
            return y;
        }

        private static NewsItem FindStory(NewsLog log, int eventId)
        {
            IReadOnlyList<NewsItem> all = log.Delivered;
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].eventId == eventId) return all[i];
            return null;
        }

        // ------------------------------------------------------------------ the theatre report

        private void DrawTheatre(Rect rect, WorldComponent_Dominion comp)
        {
            DominionPaper.DrawParchment(rect, border: false);
            GUI.color = DominionPaper.Edge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            Rect inner = rect.ContractedBy(12f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, dossierHeight));
            Widgets.BeginScrollView(inner, ref dossierScroll, view);
            float y = 0f, width = view.width;
            DominionTheme.InkLabel(new Rect(0f, y, width, 30f), "Dominion_WarRoom_Theatre".Translate(), DominionTheme.Ink, GameFont.Medium);
            y += 34f;

            y = Section(y, width, "Dominion_WarTab_Warbands".Translate(), Warbands(), "Dominion_WarTab_NoWarbands".Translate());
            y = Section(y, width, "Dominion_WarTab_ColdWars".Translate(), ColdWars(comp), "Dominion_None".Translate());
            y = Section(y, width, "Dominion_WarTab_Occupied".Translate(), Occupied(comp), "Dominion_None".Translate());
            var blockades = comp.Wars.blockades.Where(b => b.StillValid).Select(b => new Line(
                "Dominion_WarTab_BlockadeLine".Translate(b.caravan.Label, b.settlement.Label, (Find.TickManager.TicksGame - b.startTick).ToStringTicksToPeriod(allowSeconds: false)),
                true, () => CameraJumper.TryJumpAndSelect(b.settlement))).ToList();
            y = Section(y, width, "Dominion_WarTab_Blockades".Translate(), blockades, "Dominion_WarTab_NoBlockades".Translate());
            dossierHeight = y + 10f;
            Widgets.EndScrollView();
        }

        private struct Line
        {
            public string text;
            public bool alarm;
            public System.Action click;

            public Line(string text, bool alarm, System.Action click)
            {
                this.text = text;
                this.alarm = alarm;
                this.click = click;
            }
        }

        private static float Section(float y, float width, string title, List<Line> lines, string none)
        {
            DominionPaper.Caption(new Rect(0f, y, width, 16f), title);
            y += 18f;
            if (lines.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(8f, y, width, 20f), none, DominionTheme.InkFaded, GameFont.Tiny);
                return y + 26f;
            }
            foreach (Line l in lines)
            {
                Rect r = new Rect(0f, y, width, 20f);
                if (l.click != null && Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, DominionTheme.PaperShade);
                DominionTheme.InkLabel(new Rect(8f, y, width - 8f, 20f), ("▸ " + l.text).Truncate(width - 8f), l.alarm ? DominionTheme.InkRed : DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                if (l.click != null)
                {
                    TooltipHandler.TipRegion(r, l.text + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                    if (Widgets.ButtonInvisible(r)) l.click();
                }
                y += 20f;
            }
            return y + 8f;
        }

        private static List<Line> Warbands()
        {
            var lines = new List<Line>();
            foreach (Warband w in WarbandUtility.All())
            {
                if (!w.VisibleToPlayer) continue;
                Warband band = w;
                string text = w.Label + ": " + w.MissionText + "  " + (w.Blockading ? "" : "Dominion_Warband_Arrives".Translate(w.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)).ToString());
                lines.Add(new Line(text, w.target != null && w.target.IsPlayer, () => CameraJumper.TryJumpAndSelect(band)));
            }
            return lines;
        }

        private static List<Line> ColdWars(WorldComponent_Dominion comp)
        {
            var lines = new List<Line>();
            var seen = new HashSet<long>();
            var factions = new List<Faction>(comp.Cache.Simulated);
            if (Faction.OfPlayerSilentFail != null) factions.Add(Faction.OfPlayer);
            foreach (Faction a in factions)
                foreach (Faction b in factions)
                {
                    if (a == b) continue;
                    RelationState r = comp.TryGetRelation(a, b);
                    if (r == null || !r.ColdWar) continue;
                    long key = RelationState.Key(Mathf.Min(a.loadID, b.loadID), Mathf.Max(a.loadID, b.loadID));
                    if (!seen.Add(key)) continue;
                    lines.Add(new Line("Dominion_WarTab_ColdWarLine".Translate(a.Name, b.Name, (Find.TickManager.TicksGame - r.coldWarSince).ToStringTicksToPeriod(allowSeconds: false)),
                        a.IsPlayer || b.IsPlayer, null));
                }
            return lines;
        }

        private static List<Line> Occupied(WorldComponent_Dominion comp)
        {
            var lines = new List<Line>();
            if (comp.Wars.originalOwner.Count == 0) return lines;
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                int orig = comp.Wars.OriginalOwnerOf(s);
                if (orig < 0 || s.Faction == null || s.Faction.loadID == orig) continue;
                Faction o = comp.Cache.FactionById(orig);
                Settlement captured = s;
                lines.Add(new Line("Dominion_WarTab_OccupiedLine".Translate(s.Label, s.Faction.Name, o?.Name ?? "?"), o != null && o.IsPlayer, () => CameraJumper.TryJumpAndSelect(captured)));
            }
            return lines;
        }

        /// <summary>A tug of war: red to the left (defender winning), the attacker's side to the right.</summary>
        public static void DrawScoreBar(Rect bar, War w)
        {
            Widgets.DrawBoxSolid(bar, new Color(0.15f, 0.15f, 0.15f));
            float mid = bar.x + bar.width / 2f;
            float len = bar.width / 2f * Mathf.Abs(w.score) / 100f;
            Rect fill = w.score >= 0f ? new Rect(mid, bar.y, len, bar.height) : new Rect(mid - len, bar.y, len, bar.height);
            Color winner = (w.score >= 0f ? w.attacker : w.defender)?.Color ?? Color.gray;
            Widgets.DrawBoxSolid(fill, new Color(winner.r * 0.8f, winner.g * 0.8f, winner.b * 0.8f, 0.9f));
            // The decisive marks: past them, the leader dictates the peace.
            float d = bar.width / 2f * PeaceTerms.DecisiveScore / 100f;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawLineVertical(mid - d, bar.y, bar.height);
            Widgets.DrawLineVertical(mid + d, bar.y, bar.height);
            GUI.color = Color.white;
            Widgets.DrawLineVertical(mid, bar.y, bar.height);
            Faction leader = w.Leader();
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            string label = leader == null ? "Dominion_WarTab_Even".Translate(w.score.ToString("F0")).ToString()
                : "Dominion_WarTab_Ahead".Translate(leader.Name, Mathf.Abs(w.score).ToString("F0")).ToString();
            Widgets.Label(bar, label.Truncate(bar.width));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(bar, "Dominion_WarTab_Score_Desc".Translate(w.attacker.Name, w.defender.Name));
        }
    }
}
