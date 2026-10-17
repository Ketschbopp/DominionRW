using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The Rim Council tab, as the chamber: the motions on the agenda hang as banners in their sponsors' colours, the
    /// members sit in a hemicycle with seats in proportion to their vote weight, and the speaker's podium stands in
    /// front with the session's status. Pick a banner and the seats light up one by one with how each member leans on
    /// it (green aye, red nay, amber wavering), with the forecast tally on the podium. Pick a seat to see that member.
    /// Decrees in force hang as brass plaques.
    /// </summary>
    public class CouncilPanel
    {
        private const float NoticeHeight = 40f;
        private const float BannerH = 132f;
        private const float BannerW = 92f;
        private const float SideWidth = 280f;
        private const float SweepSeconds = 1.1f;

        private static readonly Color Aye = new Color(0.35f, 0.85f, 0.4f);
        private static readonly Color Nay = new Color(0.9f, 0.3f, 0.25f);
        private static readonly Color Waver = new Color(0.95f, 0.75f, 0.3f);

        // Members with their vote weight, heaviest first: weights loop over maps and allies, so not every frame.
        private readonly UICache<List<(Faction, float)>> memberCache = new UICache<List<(Faction, float)>>(() =>
        {
            List<Faction> attending = WorldComponent_Dominion.Instance.Council.attending;
            return CouncilUtility.Members().Where(f => f != null && !f.defeated)
                .Select(f => (f, CouncilUtility.VoteWeight(f, attending))).OrderByDescending(x => x.Item2).ToList();
        }, 1f);

        // How each member leans on the motion picked, worked out once a second.
        private readonly Dictionary<Faction, float> leanings = new Dictionary<Faction, float>();
        private float leaningsTime;
        private Motion leaningsFor;

        private Motion picked;
        private float sweepStart;
        private Faction member;
        private Vector2 sideScroll;
        private float sideHeight = 400f;

        private struct Seat
        {
            public Vector2 pos;
            public Faction faction;
        }

        private readonly List<Seat> seats = new List<Seat>();

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            CouncilState c = comp.Council;
            if (picked != null && !c.agenda.Contains(picked)) picked = null;
            if (picked == null && c.phase == CouncilPhase.Announced && c.agenda.Count > 0) Pick(c.agenda[0]);

            // The chamber: dark panelling, a red carpet before the podium, warm light from above.
            DominionRooms.DrawTiled(rect, DominionRooms.Wood, 0.6f, new Color(0.62f, 0.5f, 0.45f));
            Rect hall = new Rect(rect.x, rect.y, rect.width - SideWidth - 10f, rect.height);
            DominionRooms.DrawGlow(new Vector2(hall.center.x, rect.yMax), hall.width * 0.55f, new Color(0.55f, 0.06f, 0.06f, 0.55f));
            DominionRooms.DrawGlow(new Vector2(hall.center.x, rect.y + rect.height * 0.3f), hall.width * 0.6f, new Color(1f, 0.85f, 0.6f, 0.16f));
            DominionRooms.DrawVignette(rect, 0.75f);

            Rect inner = rect.ContractedBy(8f);
            DrawNotice(new Rect(inner.x, inner.y, inner.width, NoticeHeight), c);
            float top = inner.y + NoticeHeight + 8f;
            Rect side = new Rect(inner.xMax - SideWidth, top, SideWidth, inner.yMax - top);
            Rect chamber = new Rect(inner.x, top, inner.width - SideWidth - 10f, inner.yMax - top);
            DrawBanners(new Rect(chamber.x, chamber.y, chamber.width, BannerH), c);
            Rect floor = new Rect(chamber.x, chamber.y + BannerH + 6f, chamber.width, chamber.height - BannerH - 6f);
            DrawHemicycle(floor, c);
            DrawSide(side, c);
        }

        private void Pick(Motion m)
        {
            if (picked == m) return;
            picked = m;
            sweepStart = Time.realtimeSinceStartup;
            leaningsFor = null;
        }

        // ------------------------------------------------------------------ notice

        private void DrawNotice(Rect rect, CouncilState c)
        {
            bool isMember = CouncilUtility.IsMember(Faction.OfPlayer);
            int now = Find.TickManager.TicksGame;
            string text;
            if (!DominionMod.Settings.councilEnabled) text = "Dominion_Council_Disabled".Translate();
            else if (c.phase == CouncilPhase.Announced)
                text = "Dominion_Council_SessionAnnounced".Translate(c.host?.Name ?? "?", Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false));
            else if (c.nextSessionTick >= 0)
                text = "Dominion_Council_NextSession".Translate(Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false));
            else text = "Dominion_Council_Title".Translate();
            Rect plate = new Rect(rect.x, rect.y, rect.width - 220f, rect.height);
            DominionRooms.BrassPlate(plate, text, GameFont.Small);
            TooltipHandler.TipRegion(plate, isMember
                ? "Dominion_Council_YouAreMember".Translate(CouncilUtility.VoteWeight(Faction.OfPlayer, c.attending).ToString("F1")).ToString() + "\n\n" + AttendanceText(c, isMember)
                : "Dominion_Council_YouAreNotMember".Translate(DominionMod.Settings.councilMinTechLevel.ToStringHuman(), DominionResearchDefOf.Dominion_DiplomaticCorps.label).ToString());
            Rect propose = new Rect(plate.xMax + 8f, rect.y, 212f, rect.height);
            string why = !isMember ? "Dominion_Council_YouAreNotMember".Translate(DominionMod.Settings.councilMinTechLevel.ToStringHuman(), DominionResearchDefOf.Dominion_DiplomaticCorps.label).ToString()
                : c.phase != CouncilPhase.Announced ? "Dominion_Chamber_NoSession".Translate().ToString() : null;
            if (DominionRooms.BrassButton(propose, "Dominion_Council_ProposeMotion".Translate(), "Dominion_Tip_ProposeMotion".Translate(), why))
                ProposeMenu();
        }

        // ------------------------------------------------------------------ banners

        /// <summary>The agenda: a banner per motion in its sponsor's colour, hanging from a rail. Click one to see the room's leanings on it.</summary>
        private void DrawBanners(Rect rect, CouncilState c)
        {
            // The rail.
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 4f, rect.width, 5f), new Color(0.55f, 0.42f, 0.2f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 9f, rect.width, 2f), new Color(0f, 0f, 0f, 0.4f));
            if (c.phase != CouncilPhase.Announced || c.agenda.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(rect.x, rect.y + 30f, rect.width, 60f),
                    c.phase == CouncilPhase.Announced ? "Dominion_Chamber_NoMotions".Translate() : "Dominion_Chamber_NoAgenda".Translate(),
                    DominionRooms.BrassLight, GameFont.Small, TextAnchor.MiddleCenter);
                return;
            }
            var noVotes = new Dictionary<Motion, int>();
            float gap = Mathf.Min(24f, (rect.width - c.agenda.Count * BannerW) / Mathf.Max(1, c.agenda.Count + 1));
            float x = rect.center.x - (c.agenda.Count * BannerW + (c.agenda.Count - 1) * gap) / 2f;
            float t = Time.realtimeSinceStartup;
            foreach (Motion m in c.agenda)
            {
                bool sel = m == picked;
                Rect b = new Rect(x, rect.y + 8f + (sel ? 6f : 0f), BannerW, BannerH - 34f);
                bool over = Mouse.IsOver(b);
                // A gentle sway.
                Matrix4x4 mat = GUI.matrix;
                UI.RotateAroundPivot(Mathf.Sin(t * 1.3f + x * 0.05f) * (over ? 2.5f : 1f), new Vector2(b.center.x, b.y));
                Color col = m.sponsor?.Color ?? new Color(0.7f, 0.65f, 0.55f);
                GUI.color = sel ? col : new Color(col.r * 0.75f, col.g * 0.75f, col.b * 0.75f);
                GUI.DrawTexture(b, DominionRooms.Banner);
                GUI.color = Color.white;
                if (m.sponsor != null)
                {
                    GUI.color = Color.Lerp(col, Color.black, 0.6f);
                    GUI.DrawTexture(new Rect(b.center.x - 12f, b.y + 8f, 24f, 24f), m.sponsor.def.FactionIcon);
                    GUI.color = Color.white;
                }
                Color ink = (col.r * 0.3f + col.g * 0.59f + col.b * 0.11f) > 0.55f ? DominionTheme.Ink : new Color(0.97f, 0.94f, 0.88f);
                DominionTheme.InkLabel(new Rect(b.x + 8f, b.y + 34f, b.width - 16f, b.height - 50f), m.def.LabelCap, ink, GameFont.Tiny, TextAnchor.UpperCenter);
                GUI.matrix = mat;
                Widgets.DrawBoxSolid(new Rect(b.x + 4f, rect.y + 2f, b.width - 8f, 4f), new Color(0.75f, 0.6f, 0.3f));

                // The forecast under it.
                CouncilSystem.Tally(m, noVotes, out float yes, out float no);
                bool passes = CouncilSystem.WouldPass(m, noVotes);
                Rect fc = new Rect(b.x - 10f, rect.yMax - 24f, b.width + 20f, 22f);
                DominionTheme.InkLabel(fc, (passes ? "Dominion_Chamber_Carries" : "Dominion_Chamber_Fails").Translate(yes.ToString("F1"), no.ToString("F1")),
                    passes ? Aye : Nay, GameFont.Tiny, TextAnchor.MiddleCenter);
                if (sel) Widgets.DrawBoxSolid(new Rect(b.x + 10f, fc.y - 2f, b.width - 20f, 2f), DominionRooms.BrassLight);

                Rect hit = new Rect(b.x, rect.y, b.width, rect.height);
                TooltipHandler.TipRegion(hit, m.Summary + "\n" + "Dominion_Council_ProposedBy".Translate(m.sponsor?.Name ?? "?") + "\n\n" + m.def.description
                                              + "\n\n" + "Dominion_Council_Forecast_Desc".Translate() + "\n\n" + "Dominion_Chamber_ClickBanner".Translate());
                if (Widgets.ButtonInvisible(hit))
                {
                    Pick(m);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                x += BannerW + gap;
            }
        }

        // ------------------------------------------------------------------ hemicycle

        private float Leaning(Faction f)
        {
            if (picked == null) return 0f;
            if (leaningsFor != picked || Time.realtimeSinceStartup > leaningsTime)
            {
                leanings.Clear();
                leaningsFor = picked;
                leaningsTime = Time.realtimeSinceStartup + 1f;
            }
            if (!leanings.TryGetValue(f, out float v))
                leanings[f] = v = f.IsPlayer ? 0f : CouncilUtility.AILeaning(f, picked);
            return v;
        }

        private void DrawHemicycle(Rect rect, CouncilState c)
        {
            List<(Faction f, float w)> members = memberCache.Value;
            float podiumH = 64f;
            Vector2 centre = new Vector2(rect.center.x, rect.yMax - podiumH * 0.6f);
            float outer = Mathf.Min(rect.width / 2f - 16f, rect.height - podiumH * 0.6f - 10f);
            float innerR = outer * 0.42f;

            // The floor of the chamber: tiers of the hemicycle as darker arcs.
            DominionRooms.DrawGlow(centre, outer * 1.05f, new Color(0f, 0f, 0f, 0.35f));

            // Lay out the seats: rings from the inside out, each holding as many as fit, then take them in order of
            // angle, left to right, so each member's seats form a wedge.
            seats.Clear();
            int rings = Mathf.Clamp((int)((outer - innerR) / 22f), 3, 7);
            float seatSize = Mathf.Clamp((outer - innerR) / rings * 0.78f, 9f, 18f);
            var spots = new List<(float angle, Vector2 pos)>();
            for (int r = 0; r < rings; r++)
            {
                float radius = innerR + (outer - innerR) * (r + 0.5f) / rings;
                int n = Mathf.Max(3, (int)(Mathf.PI * radius / (seatSize * 1.25f)));
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.PI - i * Mathf.PI / (n - 1);
                    spots.Add((a, centre + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * radius));
                }
            }
            spots.Sort((p, q) => q.angle.CompareTo(p.angle));
            if (members.Count > 0)
            {
                // Seats in proportion to weight, at least one each.
                IEnumerable<(Faction f, float w)> order = picked != null
                    ? members.OrderByDescending(x => x.f.IsPlayer ? 0f : Leaning(x.f))
                    : members.AsEnumerable();
                var ordered = order.ToList();
                float total = ordered.Sum(x => x.w);
                var counts = ordered.Select(x => Mathf.Max(1, Mathf.RoundToInt(x.w / Mathf.Max(0.01f, total) * spots.Count))).ToList();
                while (counts.Sum() > spots.Count)
                {
                    int big = counts.IndexOf(counts.Max());
                    if (counts[big] <= 1) break;
                    counts[big]--;
                }
                int k = 0;
                for (int i = 0; i < ordered.Count && k < spots.Count; i++)
                    for (int j = 0; j < counts[i] && k < spots.Count; j++)
                        seats.Add(new Seat { pos = spots[k++].pos, faction = ordered[i].f });
            }

            // Draw them; the sweep lights them one after another when a motion is picked.
            float lit = picked == null ? seats.Count : (Time.realtimeSinceStartup - sweepStart) / SweepSeconds * seats.Count;
            Vector2 mouse = Event.current.mousePosition;
            int hovered = -1;
            float best = seatSize * seatSize;
            for (int i = 0; i < seats.Count; i++)
            {
                Seat s = seats[i];
                bool on = i < lit;
                Color col = SeatColor(s.faction, on, c);
                Rect r = new Rect(s.pos.x - seatSize / 2f, s.pos.y - seatSize / 2f, seatSize, seatSize);
                // Seats face the podium.
                Matrix4x4 m = GUI.matrix;
                float angle = Mathf.Atan2(centre.y - s.pos.y, centre.x - s.pos.x) * Mathf.Rad2Deg - 90f;
                UI.RotateAroundPivot(angle, r.center);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), DominionRooms.Seat);
                GUI.color = col;
                GUI.DrawTexture(r, DominionRooms.Seat);
                GUI.color = Color.white;
                GUI.matrix = m;
                if (s.faction == member) Widgets.DrawBox(r.ExpandedBy(1f));
                float d = (s.pos - mouse).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    hovered = i;
                }
            }

            if (members.Count == 0)
                DominionTheme.InkLabel(new Rect(rect.x, centre.y - outer * 0.6f, rect.width, 40f), "Dominion_Chamber_Empty".Translate(), DominionRooms.BrassLight, GameFont.Small, TextAnchor.MiddleCenter);

            if (hovered >= 0)
            {
                Faction f = seats[hovered].faction;
                // Light up the whole bloc.
                foreach (Seat s in seats)
                    if (s.faction == f) DominionRooms.DrawGlow(s.pos, seatSize, new Color(1f, 0.95f, 0.8f, 0.35f));
                Rect hit = new Rect(seats[hovered].pos.x - seatSize, seats[hovered].pos.y - seatSize, seatSize * 2f, seatSize * 2f);
                TooltipHandler.TipRegion(hit, () => SeatTip(f, c), f.loadID ^ 0xC0C);
                if (Widgets.ButtonInvisible(hit))
                {
                    member = f;
                    sideScroll = Vector2.zero;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }

            DrawPodium(new Rect(centre.x - 120f, centre.y - podiumH * 0.4f, 240f, podiumH), c);
            DrawLegend(new Rect(rect.x, rect.yMax - 20f, centre.x - 130f - rect.x, 18f));
        }

        private Color SeatColor(Faction f, bool lit, CouncilState c)
        {
            bool away = c.phase == CouncilPhase.Announced && !c.attending.Contains(f);
            Color col;
            if (!lit) col = new Color(0.35f, 0.32f, 0.3f);
            else if (picked == null) col = f.IsPlayer ? Color.white : f.Color;
            else if (f.IsPlayer) col = new Color(0.95f, 0.95f, 1f);
            else
            {
                float l = Leaning(f);
                col = l > 0.1f ? Aye : l < -0.1f ? Nay : Waver;
            }
            if (away) col = Color.Lerp(col, new Color(0.25f, 0.25f, 0.25f), 0.6f);
            return col;
        }

        private string SeatTip(Faction f, CouncilState c)
        {
            float weight = memberCache.Value.FirstOrDefault(x => x.Item1 == f).Item2;
            string tip = f.Name + "\n" + "Dominion_Council_Weight".Translate(weight.ToString("F1"));
            if (c.phase == CouncilPhase.Announced)
                tip += "\n" + (c.attending.Contains(f) ? "Dominion_Council_Present_Tag" : "Dominion_Council_Expected_Tag").Translate();
            if (picked != null)
            {
                if (f.IsPlayer) tip += "\n\n" + "Dominion_Chamber_YourVote".Translate();
                else tip += "\n\n" + LeanText(f, picked);
            }
            int v = c.ViolationsBy(f);
            if (v > 0) tip += "\n" + "Dominion_Council_ViolationCount".Translate(v);
            return tip + "\n\n" + "Dominion_Chamber_ClickSeat".Translate();
        }

        private static string LeanText(Faction f, Motion m)
        {
            float l = CouncilUtility.AILeaning(f, m);
            string key = l > 0.1f ? "Dominion_Chamber_LeansAye" : l < -0.1f ? "Dominion_Chamber_LeansNay" : "Dominion_Chamber_Wavering";
            return key.Translate(m.def.LabelCap).ToString() + (CouncilUtility.Wavering(f, m) ? "  " + "Dominion_Chamber_Lobbyable".Translate() : "");
        }

        /// <summary>The speaker's podium: the gavel, the session's state and the tally on the motion picked.</summary>
        private void DrawPodium(Rect rect, CouncilState c)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x + 4f, rect.y + 4f, rect.width, rect.height), new Color(0f, 0f, 0f, 0.4f));
            DominionRooms.DrawTiled(rect, DominionRooms.Wood, 0.5f, new Color(1f, 0.9f, 0.8f));
            DominionRooms.DrawMoulding(rect);
            DominionTheme.DrawInk(new Rect(rect.x + 8f, rect.y + 10f, 40f, 40f), DominionTheme.CategoryIcon(EventCategory.Council), DominionRooms.BrassLight);
            Rect text = new Rect(rect.x + 54f, rect.y + 4f, rect.width - 60f, rect.height - 8f);
            if (picked != null)
            {
                var noVotes = new Dictionary<Motion, int>();
                CouncilSystem.Tally(picked, noVotes, out float yes, out float no);
                DominionTheme.InkLabel(new Rect(text.x, text.y, text.width, 18f), picked.def.LabelCap.ToString().Truncate(text.width), DominionRooms.BrassLight, GameFont.Tiny);
                // The tally as a balance of ayes and nays.
                Rect bar = new Rect(text.x, text.y + 20f, text.width, 12f);
                float sum = Mathf.Max(0.01f, yes + no);
                Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.5f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * yes / sum, bar.height), Aye);
                Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * yes / sum, bar.y, bar.width * no / sum, bar.height), Nay);
                DominionTheme.InkLabel(new Rect(text.x, bar.yMax + 2f, text.width, 18f), "Dominion_Chamber_Tally".Translate(yes.ToString("F1"), no.ToString("F1"), CouncilSystem.Quorum.ToString("F1")),
                    new Color(0.95f, 0.9f, 0.8f), GameFont.Tiny);
                TooltipHandler.TipRegion(rect, "Dominion_Council_Forecast_Desc".Translate());
            }
            else
            {
                string status = c.phase == CouncilPhase.Announced ? "Dominion_Chamber_InSessionSoon".Translate(c.host?.Name ?? "?") : "Dominion_Chamber_Recess".Translate();
                DominionTheme.InkLabel(text, status, DominionRooms.BrassLight, GameFont.Tiny, TextAnchor.MiddleLeft);
            }
        }

        private void DrawLegend(Rect rect)
        {
            if (picked == null)
            {
                DominionTheme.InkLabel(rect, "Dominion_Chamber_LegendFactions".Translate(), new Color(0.85f, 0.8f, 0.7f), GameFont.Tiny, TextAnchor.MiddleLeft);
                return;
            }
            float x = rect.x;
            foreach ((Color col, string key) in new[] { (Aye, "Dominion_Chamber_Aye"), (Nay, "Dominion_Chamber_Nay"), (Waver, "Dominion_Chamber_Waver") })
            {
                Widgets.DrawBoxSolid(new Rect(x, rect.y + 5f, 10f, 10f), col);
                string label = key.Translate();
                Text.Font = GameFont.Tiny;
                float w = Text.CalcSize(label).x + 8f;
                Text.Font = GameFont.Small;
                DominionTheme.InkLabel(new Rect(x + 13f, rect.y, w, rect.height), label, new Color(0.9f, 0.85f, 0.75f), GameFont.Tiny, TextAnchor.MiddleLeft);
                x += 13f + w + 8f;
            }
        }

        // ------------------------------------------------------------------ the side: a member and the decrees

        private void DrawSide(Rect rect, CouncilState c)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.07f, 0.04f, 0.03f, 0.7f));
            DominionRooms.DrawMoulding(rect);
            Rect inner = rect.ContractedBy(8f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(inner.height, sideHeight));
            Widgets.BeginScrollView(inner, ref sideScroll, view);
            float y = 0f, w = view.width;

            if (member != null && !memberCache.Value.Any(x => x.Item1 == member)) member = null;
            if (member == null)
            {
                DominionTheme.InkLabel(new Rect(0f, y, w, 40f), "Dominion_Chamber_PickSeat".Translate(), DominionRooms.BrassLight, GameFont.Tiny);
                y += 44f;
            }
            else y = DrawMember(y, w, member, c);

            // Decrees in force, as brass plaques.
            y += 6f;
            DominionTheme.InkLabel(new Rect(0f, y, w, 20f), "Dominion_Council_Decrees".Translate().ToString().ToUpper(), DominionRooms.BrassLight, GameFont.Tiny);
            y += 20f;
            if (c.decrees.Count == 0)
            {
                DominionTheme.InkLabel(new Rect(4f, y, w, 20f), "Dominion_None".Translate(), new Color(0.75f, 0.7f, 0.62f), GameFont.Tiny);
                y += 22f;
            }
            int now = Find.TickManager.TicksGame;
            foreach (Decree d in c.decrees)
            {
                string expires = d.expiresTick < 0 ? "Dominion_Dip_NoExpiry".Translate().ToString()
                    : "Dominion_Dip_Expires".Translate(Mathf.Max(0, d.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false)).ToString();
                string line = d.Summary + "\n" + expires + (d.InGrace ? "  ·  " + "Dominion_Council_InGrace".Translate() : "");
                if (d.violators.Count > 0) line += "\n" + "Dominion_Council_Violators".Translate(string.Join(", ", d.violators.Select(v => v.Name)));
                Text.Font = GameFont.Tiny;
                float h = Text.CalcHeight(line, w - 24f) + 14f;
                Text.Font = GameFont.Small;
                Rect plaque = new Rect(0f, y, w, h);
                GUI.DrawTexture(plaque, DominionRooms.Brass, ScaleMode.StretchToFill);
                DominionRooms.DrawScrews(plaque, 5f);
                DominionTheme.InkLabel(new Rect(12f, y + 6f, w - 24f, h - 8f), line, d.violators.Count > 0 ? DominionTheme.InkRed : DominionRooms.BrassInk, GameFont.Tiny);
                TooltipHandler.TipRegion(plaque, d.def.description);
                y += h + 6f;
            }
            sideHeight = y + 6f;
            Widgets.EndScrollView();
        }

        private float DrawMember(float y, float w, Faction f, CouncilState c)
        {
            float weight = memberCache.Value.FirstOrDefault(x => x.Item1 == f).Item2;
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(0f, y + 2f, 28f, 28f), f);
            DominionTheme.InkLabel(new Rect(34f, y, w - 34f, 30f), f.Name.Truncate(w - 34f), DominionRooms.BrassLight, GameFont.Small, TextAnchor.MiddleLeft);
            y += 32f;
            var lines = new List<string> { "Dominion_Council_Weight".Translate(weight.ToString("F1")) };
            if (c.phase == CouncilPhase.Announced) lines.Add((c.attending.Contains(f) ? "Dominion_Council_Present_Tag" : "Dominion_Council_Expected_Tag").Translate());
            if (c.host == f) lines.Add("Dominion_Chamber_Host".Translate());
            int v = c.ViolationsBy(f);
            if (v > 0) lines.Add("Dominion_Council_ViolationCount".Translate(v));
            foreach (string l in lines)
            {
                DominionTheme.InkLabel(new Rect(4f, y, w - 4f, 18f), l, new Color(0.9f, 0.85f, 0.75f), GameFont.Tiny);
                y += 18f;
            }
            if (c.phase == CouncilPhase.Announced && !f.IsPlayer)
            {
                y += 4f;
                foreach (Motion m in c.agenda)
                {
                    float l = CouncilUtility.AILeaning(f, m);
                    Color col = l > 0.1f ? Aye : l < -0.1f ? Nay : Waver;
                    Widgets.DrawBoxSolid(new Rect(4f, y + 5f, 8f, 8f), col);
                    Rect r = new Rect(16f, y, w - 16f, 18f);
                    DominionTheme.InkLabel(r, LeanText(f, m).Truncate(r.width), col, GameFont.Tiny);
                    TooltipHandler.TipRegion(r, m.Summary + "\n\n" + LeanText(f, m) + "\n\n" + "Dominion_Chamber_LobbyHint".Translate());
                    y += 18f;
                }
            }
            y += 6f;
            if (!f.IsPlayer)
            {
                DominionUIState.JumpButtons(new Rect(0f, y, w, 24f), f, DominionTab.Council);
                y += 30f;
            }
            Widgets.DrawBoxSolid(new Rect(0f, y - 2f, w, 1f), new Color(DominionRooms.BrassLight.r, DominionRooms.BrassLight.g, DominionRooms.BrassLight.b, 0.3f));
            return y + 4f;
        }

        // ------------------------------------------------------------------ proposing

        private static string AttendanceText(CouncilState c, bool member)
        {
            if (!member) return "Dominion_Council_Attend_NotMember".Translate();
            if (c.host != null && c.host.IsPlayer) return "Dominion_Council_Attend_YouHost".Translate();
            if (c.playerDelegate != null) return "Dominion_Council_Attend_Registered".Translate(c.playerDelegate.LabelShortCap);
            return "Dominion_Council_Attend_Travel".Translate(c.host?.Name ?? "?");
        }

        private static void ProposeMenu()
        {
            var options = new List<FloatMenuOption>();
            float influence = WorldComponent_Dominion.Instance.Player.influence;
            foreach (CouncilMotionDef def in DefDatabase<CouncilMotionDef>.AllDefsListForReading)
            {
                CouncilMotionDef d = def;
                List<List<Faction>> targetSets = def.Worker.PlayerTargetOptions().ToList();
                string label = def.LabelCap + " (" + "Dominion_Dip_Cost".Translate(def.proposeCost.ToString("F0")) + ")";
                if (influence < def.proposeCost || targetSets.Count == 0)
                {
                    options.Add(new FloatMenuOption(label + (targetSets.Count == 0 ? " " + "Dominion_Council_NoTargets".Translate() : ""), null));
                    continue;
                }
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (d.targetCount == 0)
                    {
                        Submit(d, targetSets[0]);
                        return;
                    }
                    var targetOptions = targetSets.Select(t => new FloatMenuOption(d.Summarize(t), () => Submit(d, t))).ToList();
                    Find.WindowStack.Add(new FloatMenu(targetOptions));
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void Submit(CouncilMotionDef def, List<Faction> targets)
        {
            string error = CouncilSystem.ProposeByPlayer(def, targets);
            Messages.Message(error ?? "Dominion_Council_MotionAdded".Translate(def.Summarize(targets)),
                error == null ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
