using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The Treaties tab, as the state archive: stone walls under a lamp. Your shelves hold every treaty you are party
    /// to as a scroll tied with a ribbon in its kind's colour (frayed when it is about to lapse); pull one out and it
    /// unrolls on the reading desk, where you can read it in full, renew or amend it, or tear it up. The foreign records
    /// are index cards for the treaties between others that your intelligence knows of, and the corkboard pins every
    /// known faction with a string for each treaty between them.
    /// </summary>
    public class TreatiesPanel
    {
        private enum Mode { Shelves, Records, Corkboard }

        private const float BarHeight = 34f;
        private const float ScrollW = 132f;
        private const float ScrollH = 32f;
        private const float ShelfRowH = 92f;
        private const float RecordH = 34f;
        private const float UnrollSeconds = 0.35f;

        private static Mode mode = Mode.Shelves;
        private Vector2 shelfScroll;
        private Vector2 deskScroll;
        private Vector2 recordScroll;
        private float deskHeight = 400f;
        private int openId = -1;
        private float openedAt;
        private Faction boardFocus;
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

            // The archive: stone, a lamp, dark corners.
            DominionRooms.DrawTiled(rect, DominionRooms.Stone, 0.5f);
            DominionRooms.DrawGlow(new Vector2(rect.x + rect.width * 0.7f, rect.y + rect.height * 0.3f), rect.width * 0.5f, new Color(1f, 0.78f, 0.45f, 0.2f));
            DominionRooms.DrawVignette(rect, 0.85f);

            Rect inner = rect.ContractedBy(8f);
            DrawBar(new Rect(inner.x, inner.y, inner.width, BarHeight));
            Rect body = new Rect(inner.x, inner.y + BarHeight + 8f, inner.width, inner.height - BarHeight - 8f);
            switch (mode)
            {
                case Mode.Shelves: DrawShelves(body); break;
                case Mode.Records: DrawRecords(body); break;
                case Mode.Corkboard: DrawCorkboard(body, comp); break;
            }
        }

        private void DrawBar(Rect bar)
        {
            float x = bar.x;
            foreach (Mode m in System.Enum.GetValues(typeof(Mode)))
            {
                Rect b = new Rect(x, bar.y, 190f, bar.height);
                string label = ("Dominion_Archive_Mode_" + m).Translate() + (m == Mode.Shelves ? " (" + mine.Count + ")" : m == Mode.Records ? " (" + world.Count + ")" : "");
                if (DominionRooms.BrassButton(b, label, ("Dominion_Archive_Mode_" + m + "_Desc").Translate(), null, mode == m) && mode != m)
                    mode = m;
                x += 196f;
            }
            Rect draft = new Rect(bar.xMax - 200f, bar.y, 200f, bar.height);
            if (DominionRooms.BrassButton(draft, "Dominion_Treaties_Draft".Translate(), "Dominion_Treaties_Draft_Desc".Translate()))
                Dialog_TreatyDraft.ProposeAnyMenu();
        }

        // ------------------------------------------------------------------ shelves

        private void DrawShelves(Rect rect)
        {
            Rect shelves = new Rect(rect.x, rect.y, rect.width * 0.56f, rect.height);
            Rect desk = new Rect(shelves.xMax + 12f, rect.y, rect.width - shelves.width - 12f, rect.height);
            Treaty open = mine.FirstOrDefault(t => t.id == openId);

            // The bookcase.
            Widgets.DrawBoxSolid(shelves, new Color(0.1f, 0.06f, 0.03f, 0.85f));
            DominionRooms.DrawTiled(shelves.ContractedBy(6f), DominionRooms.Wood, 0.5f, new Color(0.45f, 0.4f, 0.36f));
            DominionRooms.DrawMoulding(shelves);
            Rect inner = shelves.ContractedBy(14f);
            if (mine.Count == 0)
            {
                DrawShelfBoard(new Rect(inner.x, inner.y + ShelfRowH - 26f, inner.width, 22f));
                DominionTheme.InkLabel(new Rect(inner.x, inner.y + ShelfRowH + 6f, inner.width, 80f), "Dominion_Treaties_NoneYours".Translate(), DominionRooms.BrassLight, GameFont.Small, TextAnchor.UpperCenter);
            }
            else
            {
                int perShelf = Mathf.Max(1, (int)((inner.width - 16f) / (ScrollW + 12f)));
                int rows = Mathf.Max(Mathf.CeilToInt(mine.Count / (float)perShelf), Mathf.FloorToInt(inner.height / ShelfRowH));
                Rect view = new Rect(0f, 0f, inner.width - 16f, rows * ShelfRowH);
                Widgets.BeginScrollView(inner, ref shelfScroll, view);
                for (int r = 0; r < rows; r++) DrawShelfBoard(new Rect(0f, r * ShelfRowH + ShelfRowH - 26f, view.width, 22f));
                for (int i = 0; i < mine.Count; i++)
                {
                    float x = i % perShelf * (ScrollW + 12f) + 6f;
                    float y = i / perShelf * ShelfRowH + ShelfRowH - 26f - ScrollH - 22f;
                    DrawScroll(new Rect(x, y, ScrollW, ScrollH), mine[i]);
                }
                Widgets.EndScrollView();
            }

            DrawDesk(desk, open);
        }

        private static void DrawShelfBoard(Rect r)
        {
            DominionRooms.DrawStrip(r, DominionRooms.Shelf);
        }

        /// <summary>A scroll lying on the shelf with its ribbon and a brass tag naming the other party.</summary>
        private void DrawScroll(Rect r, Treaty t)
        {
            Faction other = t.Other(Faction.OfPlayer);
            bool isOpen = t.id == openId;
            Rect slot = new Rect(r.x, r.y - 4f, r.width, r.height + 26f);
            if (isOpen)
            {
                // The gap where the scroll was taken out.
                GUI.color = new Color(1f, 0.9f, 0.7f, 0.15f);
                Widgets.DrawBox(r, 1);
                GUI.color = Color.white;
            }
            else
            {
                bool over = Mouse.IsOver(slot);
                Rect s = over ? new Rect(r.x, r.y - 4f, r.width, r.height) : r;
                Widgets.DrawBoxSolid(new Rect(s.x + 6f, r.yMax - 2f, s.width - 12f, 4f), new Color(0f, 0f, 0f, 0.35f));
                GUI.DrawTexture(s, DominionRooms.Scroll);
                bool fraying = t.expiresTick >= 0 && t.def.Expires && t.DaysLeft <= 15;
                GUI.color = TreatyColor(t.def);
                GUI.DrawTexture(new Rect(s.center.x - 10f, s.y - 2f, 20f, 40f), fraying ? DominionRooms.RibbonFrayed : DominionRooms.Ribbon);
                GUI.color = Color.white;
                DominionPaper.DrawSeal(new Rect(s.x + 18f, s.center.y - 9f, 18f, 18f), other);
            }
            Rect tag = new Rect(r.x + 10f, r.yMax + 4f, r.width - 20f, 18f);
            DominionRooms.BrassPlate(tag, other.Name, GameFont.Tiny);
            string expires = t.expiresTick >= 0 ? "Dominion_Treaties_DaysLeft".Translate(t.DaysLeft).ToString() : "Dominion_Dip_NoExpiry".Translate().ToString();
            TooltipHandler.TipRegion(slot, t.Label + "\n" + other.Name + "  ·  " + expires + "\n\n" + t.def.description + "\n\n" + "Dominion_Archive_PullOut".Translate());
            if (Widgets.ButtonInvisible(slot))
            {
                if (isOpen) openId = -1;
                else
                {
                    openId = t.id;
                    openedAt = Time.realtimeSinceStartup;
                    deskScroll = Vector2.zero;
                    SoundDefOf.PageChange.PlayOneShotOnCamera();
                }
            }
        }

        /// <summary>The reading desk: the pulled-out scroll unrolls between its two rollers.</summary>
        private void DrawDesk(Rect rect, Treaty t)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.07f, 0.04f, 0.85f));
            DominionRooms.DrawTiled(rect.ContractedBy(4f), DominionRooms.Wood, 0.9f, new Color(0.8f, 0.72f, 0.64f));
            DominionRooms.DrawMoulding(rect);
            DominionRooms.DrawGlow(new Vector2(rect.center.x, rect.y + 40f), rect.width * 0.6f, new Color(1f, 0.85f, 0.55f, 0.18f));
            Rect area = rect.ContractedBy(16f);
            if (t == null)
            {
                DominionTheme.InkLabel(new Rect(area.x, area.center.y - 30f, area.width, 60f), "Dominion_Archive_DeskEmpty".Translate(), DominionRooms.BrassLight, GameFont.Small, TextAnchor.MiddleCenter);
                return;
            }

            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / UnrollSeconds);
            k = 1f - (1f - k) * (1f - k);
            float h = Mathf.Lerp(ScrollH, area.height, k);
            Rect sheet = new Rect(area.x + 8f, area.y + ScrollH / 2f, area.width - 16f, h - ScrollH);
            DominionPaper.DrawParchment(sheet, border: false);
            GUI.color = DominionPaper.EdgeFaint;
            Widgets.DrawBox(sheet);
            GUI.color = Color.white;

            if (k > 0.85f)
            {
                Rect page = sheet.ContractedBy(14f, ScrollH / 2f + 4f);
                Rect view = new Rect(0f, 0f, page.width - 16f, Mathf.Max(page.height, deskHeight));
                Widgets.BeginScrollView(page, ref deskScroll, view);
                deskHeight = DrawDocument(view.width, t);
                Widgets.EndScrollView();
            }
            // The rollers, top and bottom.
            GUI.DrawTexture(new Rect(area.x, area.y, area.width, ScrollH), DominionRooms.Scroll);
            GUI.DrawTexture(new Rect(area.x, area.y + h - ScrollH, area.width, ScrollH), DominionRooms.Scroll);
            GUI.color = TreatyColor(t.def);
            GUI.DrawTexture(new Rect(area.center.x - 9f, area.y + h - ScrollH - 4f, 18f, 36f), DominionRooms.Ribbon);
            GUI.color = Color.white;
        }

        private float DrawDocument(float w, Treaty t)
        {
            Faction player = Faction.OfPlayer;
            Faction other = t.Other(player);
            float y = DominionPaper.Title(new Rect(0f, 0f, w, 40f), t.Label);
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f), "Dominion_Archive_Between".Translate(t.a.Name, t.b.Name), DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
            y += 22f;
            foreach (string p in TreatyDraft.Provisions(t.def, t.a, t.b))
                y += DominionPaper.Paragraph(10f, y, w - 10f, "§  " + p, DominionTheme.Ink, GameFont.Tiny) + 3f;
            y += 6f;
            DominionTheme.InkLabel(new Rect(0f, y, w, 18f), "Dominion_Archive_Signed".Translate(DominionPaper.Date(t.signedTick)), DominionTheme.InkFaded, GameFont.Tiny);
            y += 18f;

            // Time left as a ribbon of ink.
            if (t.expiresTick >= 0)
            {
                float total = Mathf.Max(1f, t.expiresTick - t.signedTick);
                float left = Mathf.Clamp01((t.expiresTick - Find.TickManager.TicksGame) / total);
                Rect bar = new Rect(0f, y + 2f, w, 16f);
                Widgets.DrawBoxSolid(bar, new Color(0.3f, 0.24f, 0.17f, 0.15f));
                Color c = t.DaysLeft <= 15 ? DominionTheme.InkRed : DominionTheme.InkGreen;
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * left, bar.height), new Color(c.r, c.g, c.b, 0.45f));
                DominionTheme.InkLabel(bar, "Dominion_Treaties_DaysLeft".Translate(t.DaysLeft), DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleCenter);
                y += 24f;
            }
            else
            {
                DominionTheme.InkLabel(new Rect(0f, y, w, 18f), "Dominion_Dip_NoExpiry".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
                y += 20f;
            }

            // The seals.
            float sx = w / 2f - 70f;
            DominionPaper.DrawSeal(new Rect(sx, y + 4f, 52f, 52f), t.a);
            DominionPaper.DrawSeal(new Rect(sx + 88f, y + 4f, 52f, 52f), t.b);
            y += 64f;

            // What can be done with it.
            float bw = (w - 8f) / 3f;
            if (DominionPaper.InkButton(new Rect(0f, y, bw, 28f), "Dominion_Archive_ReadFull".Translate(), "Dominion_Treaties_ClickToRead".Translate()))
                Find.WindowStack.Add(new Dialog_TreatyDocument(t));
            string renewWhy = !t.def.Expires || t.def.imposed ? "Dominion_Archive_CannotRenew".Translate().ToString() : null;
            if (DominionPaper.InkButton(new Rect(bw + 4f, y, bw, 28f), t.DaysLeft <= 15 ? "Dominion_Treaty_Renew".Translate() : "Dominion_Treaty_Amend".Translate(),
                    "Dominion_Treaty_Renew_Desc".Translate(other.Name), renewWhy, false, DominionTheme.InkGreen))
                Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, other, t));
            if (DominionPaper.InkButton(new Rect(bw * 2f + 8f, y, bw, 28f), "Dominion_Treaty_TearUp".Translate(),
                    "Dominion_Tip_CancelTreaty".Translate() + "\n\n" + TreatyDraft.TearUpCost(t, player), null, false, DominionTheme.InkRed))
                Dialog_TreatyDocument.ConfirmTearUp(t, () => openId = -1);
            y += 34f;
            if (DominionPaper.InkButton(new Rect(0f, y, w, 24f), "Dominion_Archive_OpenParty".Translate(other.Name), "Dominion_UI_Jump_Diplomacy_Desc".Translate(other.Name)))
                DominionUIState.Show(DominionTab.Diplomacy, other);
            return y + 30f;
        }

        // ------------------------------------------------------------------ foreign records

        /// <summary>The card index: one card per treaty between others that you know of.</summary>
        private void DrawRecords(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.06f, 0.03f, 0.75f));
            DominionRooms.DrawMoulding(rect);
            Rect inner = rect.ContractedBy(12f);
            if (world.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(inner.x, inner.y + 40f, inner.width, 80f), "Dominion_Treaties_None".Translate(), DominionRooms.BrassLight, GameFont.Small, TextAnchor.UpperCenter);
                return;
            }
            world.SortBy(t => t.def.label, t => t.a.Name);
            Rect view = new Rect(0f, 0f, inner.width - 16f, world.Count * (RecordH + 4f));
            Widgets.BeginScrollView(inner, ref recordScroll, view);
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < world.Count; i++)
            {
                Treaty t = world[i];
                Rect card = new Rect(i % 2 == 0 ? 0f : 6f, i * (RecordH + 4f), view.width - 6f, RecordH);
                DominionPaper.DrawCard(card);
                Widgets.DrawBoxSolid(new Rect(card.x, card.y, 6f, card.height), TreatyColor(t.def));
                Rect c = card.ContractedBy(10f, 4f);
                DominionTheme.InkLabel(new Rect(c.x, c.y, c.width * 0.3f, c.height), t.Label.Truncate(c.width * 0.3f), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
                Party(new Rect(c.x + c.width * 0.31f, c.y, c.width * 0.27f, c.height), t.a);
                Party(new Rect(c.x + c.width * 0.59f, c.y, c.width * 0.27f, c.height), t.b);
                string exp = t.expiresTick >= 0 ? (t.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false) : "Dominion_Dip_NoExpiry".Translate().ToString();
                DominionTheme.InkLabel(new Rect(c.x + c.width * 0.87f, c.y, c.width * 0.13f, c.height), exp, DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(card, t.def.description + "\n\n" + "Dominion_Treaties_ClickToRead".Translate());
                if (Widgets.ButtonInvisible(card)) Find.WindowStack.Add(new Dialog_TreatyDocument(t));
            }
            Widgets.EndScrollView();
        }

        private static void Party(Rect r, Faction f)
        {
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(r.x, r.center.y - 10f, 20f, 20f), f);
            DominionTheme.InkLabel(new Rect(r.x + 24f, r.y, r.width - 24f, r.height), f.Name.Truncate(r.width - 24f), DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
        }

        // ------------------------------------------------------------------ corkboard

        /// <summary>
        /// Every known faction pinned to cork in a ring, with a string for each treaty you know of, coloured by kind and
        /// sagging a little. Hover a faction to light up its strings; click it to keep them lit.
        /// </summary>
        private void DrawCorkboard(Rect rect, WorldComponent_Dominion comp)
        {
            Widgets.DrawBoxSolid(rect.ExpandedBy(4f), new Color(0.2f, 0.12f, 0.06f));
            DominionRooms.DrawTiled(rect, DominionRooms.Cork, 0.6f);
            DominionRooms.DrawMoulding(rect, new Color(0.6f, 0.45f, 0.3f, 0.5f));

            var factions = new List<Faction> { Faction.OfPlayer };
            factions.AddRange(comp.Cache.Simulated);
            Rect legend = new Rect(rect.xMax - 210f, rect.y + 16f, 190f, 40f + DefDatabase<TreatyDef>.AllDefsListForReading.Count * 20f + 70f);
            Rect area = new Rect(rect.x, rect.y, rect.width - 220f, rect.height);
            Vector2 centre = area.center;
            float rx = area.width / 2f - 80f, ry = area.height / 2f - 50f;
            var pos = new Dictionary<Faction, Vector2>();
            for (int i = 0; i < factions.Count; i++)
            {
                float a = -Mathf.PI / 2f + i * 2f * Mathf.PI / factions.Count;
                pos[factions[i]] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }

            Faction hover = null;
            foreach (Faction f in factions)
                if (new Rect(pos[f].x - 60f, pos[f].y - 22f, 120f, 44f).Contains(Event.current.mousePosition)) hover = f;
            Faction lit = hover ?? boardFocus;

            // Strings first, so the cards sit on top.
            var pairs = new Dictionary<long, int>();
            foreach (Treaty t in mine.Concat(world))
            {
                if (!pos.TryGetValue(t.a, out Vector2 pa) || !pos.TryGetValue(t.b, out Vector2 pb)) continue;
                long key = RelationState.Key(Mathf.Min(t.a.loadID, t.b.loadID), Mathf.Max(t.a.loadID, t.b.loadID));
                pairs.TryGetValue(key, out int n);
                pairs[key] = n + 1;
                Color c = TreatyColor(t.def);
                bool on = lit == null || t.Involves(lit);
                c.a = on ? 0.95f : 0.15f;
                // Several treaties between the same pair hang at different slacks.
                DrawString(pa, pb, 18f + n * 14f, c, on && lit != null ? 3f : 2f);
            }

            foreach (Faction f in factions)
            {
                Vector2 p = pos[f];
                Rect card = new Rect(p.x - 58f, p.y - 20f, 116f, 40f);
                float tilt = (f.loadID * 37 % 9 - 4) * 0.6f;
                Matrix4x4 m = GUI.matrix;
                UI.RotateAroundPivot(tilt, card.center);
                Widgets.DrawBoxSolid(new Rect(card.x + 3f, card.y + 3f, card.width, card.height), new Color(0f, 0f, 0f, 0.3f));
                DominionTheme.DrawPaper(card);
                if (f == lit) Widgets.DrawBoxSolid(card, DominionTheme.PaperHighlight);
                GUI.color = f.IsPlayer ? Color.white : f.Color;
                GUI.DrawTexture(new Rect(card.x + 5f, card.y + 9f, 22f, 22f), f.def.FactionIcon);
                GUI.color = Color.white;
                DominionTheme.InkLabel(new Rect(card.x + 30f, card.y + 2f, card.width - 32f, card.height - 4f), f.Name, DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleLeft);
                GUI.DrawTexture(new Rect(card.center.x - 8f, card.y - 10f, 16f, 16f), DominionTheme.Pin);
                GUI.matrix = m;
                TooltipHandler.TipRegion(card, () => BoardTip(f), f.loadID ^ 0x7EB);
                if (Widgets.ButtonInvisible(card))
                {
                    boardFocus = boardFocus == f ? null : f;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }

            // The legend, pinned as a note.
            DominionTheme.DrawPaper(legend);
            GUI.DrawTexture(new Rect(legend.center.x - 8f, legend.y - 10f, 16f, 16f), DominionTheme.Pin);
            float ly = legend.y + 8f;
            DominionTheme.InkLabel(new Rect(legend.x + 8f, ly, legend.width - 16f, 20f), "Dominion_Treaties_Legend".Translate(), DominionTheme.InkRed, GameFont.Tiny);
            ly += 22f;
            foreach (TreatyDef def in DefDatabase<TreatyDef>.AllDefsListForReading)
            {
                Widgets.DrawBoxSolid(new Rect(legend.x + 8f, ly + 8f, 22f, 3f), TreatyColor(def));
                DominionTheme.InkLabel(new Rect(legend.x + 36f, ly, legend.width - 44f, 20f), def.LabelCap.ToString().Truncate(legend.width - 44f), DominionTheme.Ink, GameFont.Tiny);
                ly += 20f;
            }
            DominionTheme.InkLabel(new Rect(legend.x + 8f, ly + 6f, legend.width - 16f, 60f), "Dominion_Treaties_WebHelp".Translate(), DominionTheme.InkFaded, GameFont.Tiny);
        }

        /// <summary>A string pinned at both ends, sagging under its own weight.</summary>
        private static void DrawString(Vector2 a, Vector2 b, float sag, Color c, float width)
        {
            Vector2 prev = a;
            const int Steps = 14;
            for (int i = 1; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(0f, sag * 4f * t * (1f - t));
                Widgets.DrawLine(prev, p, c, width);
                prev = p;
            }
        }

        private string BoardTip(Faction f)
        {
            var lines = new List<string> { f.Name };
            foreach (Treaty t in mine.Concat(world))
                if (t.Involves(f)) lines.Add("  " + t.Label + ": " + t.Other(f).Name);
            if (lines.Count == 1) lines.Add("  " + "Dominion_None".Translate());
            return string.Join("\n", lines);
        }

        /// <summary>The colour a kind of treaty is drawn in: its ribbon, its string and its card's edge.</summary>
        public static Color TreatyColor(TreatyDef def)
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
