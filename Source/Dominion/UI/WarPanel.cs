using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The War tab. Each of your wars is a card: the score as a tug of war, weariness and casualties, a map of the front
    /// with the warbands on it, your orders (stance, mercenaries, allies, the peace table) and the war's chronicle. Below:
    /// the world's wars, cold wars, warbands in the field, occupied settlements and blockades.
    /// </summary>
    public class WarPanel
    {
        private const float Line = 24f;
        private const float CardHeight = 236f;
        private const int ChronicleShown = 12;

        private Vector2 scroll;
        private float height = 800f;
        private readonly HashSet<int> openChronicles = new HashSet<int>();

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
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            Faction focus = DominionUIState.selected;
            DominionTheme.Header(l, "Dominion_WarTab_Yours".Translate());
            List<War> mine = comp.Wars.wars.Where(w => w.InvolvesPlayer).OrderBy(w => w.Involves(focus) ? 0 : 1).ToList();
            if (mine.Count == 0) None(l);
            foreach (War w in mine) DrawPlayerWar(l, w, comp);

            DominionTheme.Header(l, "Dominion_WarTab_World".Translate());
            List<War> others = comp.Wars.wars.Where(w => !w.InvolvesPlayer).OrderBy(w => w.Involves(focus) ? 0 : 1).ToList();
            if (others.Count == 0) None(l);
            foreach (War w in others) DrawWorldWar(l, w);

            DominionTheme.Header(l, "Dominion_WarTab_ColdWars".Translate());
            DrawColdWars(l, comp);

            DominionTheme.Header(l, "Dominion_WarTab_Warbands".Translate());
            DrawWarbands(l);

            DominionTheme.Header(l, "Dominion_WarTab_Occupied".Translate());
            DrawOccupied(l, comp);

            DominionTheme.Header(l, "Dominion_WarTab_Blockades".Translate());
            List<Blockade> blockades = comp.Wars.blockades.Where(b => b.StillValid).ToList();
            if (blockades.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_WarTab_NoBlockades".Translate());
                GUI.color = Color.white;
            }
            foreach (Blockade b in blockades)
                l.Label("  " + "Dominion_WarTab_BlockadeLine".Translate(b.caravan.Label, b.settlement.Label,
                    (Find.TickManager.TicksGame - b.startTick).ToStringTicksToPeriod(allowSeconds: false)));

            height = Mathf.Max(l.CurHeight + 20f, rect.height);
            l.End();
            Widgets.EndScrollView();
        }

        private static void None(Listing_Standard l)
        {
            GUI.color = Color.gray;
            l.Label("  " + "Dominion_None".Translate());
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ your wars

        private void DrawPlayerWar(Listing_Standard l, War w, WorldComponent_Dominion comp)
        {
            Faction enemy = w.Other(Faction.OfPlayer);
            Rect card = l.GetRect(CardHeight);
            Widgets.DrawMenuSection(card);
            Rect inner = card.ContractedBy(8f);
            float mapW = Mathf.Min(inner.width * 0.36f, 300f);
            Rect map = new Rect(inner.xMax - mapW, inner.y, mapW, mapW / 2f);
            Rect left = new Rect(inner.x, inner.y, inner.width - mapW - 10f, inner.height);

            // Title
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(left.x, left.y + 2f, 24f, 24f), w.attacker);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(left.x + 26f, left.y + 2f, 24f, 24f), w.defender);
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(left.x + 56f, left.y - 2f, left.width - 56f, 32f);
            Widgets.Label(titleRect, w.Label.Truncate(titleRect.width));
            Text.Font = GameFont.Small;
            string goal = "Dominion_WarTab_Goal".Translate(WarUtility.GoalLabel(w.goal));
            if (w.alliedTo != null) goal += "  " + "Dominion_WarTab_JoinedFor".Translate(w.alliedTo.Name);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(left.x, left.y + 30f, left.width, 22f), goal.Truncate(left.width));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(left.x, left.y, left.width, 52f), WarUtility.GoalDescription(w.goal));

            // Score and gauges
            DrawScoreBar(new Rect(left.x, left.y + 54f, left.width, 22f), w);
            float gy = left.y + 82f;
            FactionState es = DominionScheduler.IsSimulated(enemy) ? comp.GetState(enemy) : null;
            if (es != null)
            {
                Rect wr = new Rect(left.x, gy, left.width * 0.5f - 4f, 20f);
                DominionUI.Bar(wr, "Dominion_WarTab_Weariness".Translate(enemy.Name.Truncate(90f)), es.warWeariness / 100f, es.warWeariness.ToString("F0"));
                TooltipHandler.TipRegion(wr, "Dominion_WarTab_Weariness_Desc".Translate(enemy.Name));
            }
            Rect cas = new Rect(left.x + left.width * 0.5f + 4f, gy, left.width * 0.5f - 4f, 20f);
            Widgets.Label(cas, "Dominion_WarTab_Casualties".Translate(w.DeadOf(Faction.OfPlayer), w.DeadOf(enemy)).ToString().Truncate(cas.width));
            TooltipHandler.TipRegion(cas, "Dominion_WarTab_Casualties_Desc".Translate(w.DurationTicks.ToStringTicksToPeriod(allowSeconds: false), w.battles));
            gy += 24f;

            // Peace now
            if (!termsCache.TryGetValue(w.id, out string terms))
                termsCache[w.id] = terms = PeaceTerms.Describe(w);
            Rect termsRow = new Rect(left.x, gy, left.width, 22f);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(termsRow, ("Dominion_WarTab_PeaceNow".Translate() + "  " + terms.Split('\n').FirstOrDefault()).Truncate(termsRow.width));
            GUI.color = Color.white;
            TooltipHandler.TipRegion(termsRow, terms);
            gy += 26f;

            // Stance
            Widgets.Label(new Rect(left.x, gy, 90f, 26f), "Dominion_WarTab_Stance".Translate());
            float sw = (left.width - 92f) / 4f;
            int i = 0;
            foreach (WarStance s in System.Enum.GetValues(typeof(WarStance)))
            {
                Rect b = new Rect(left.x + 92f + i * sw, gy, sw - 3f, 26f);
                string why = WarOrders.CannotChangeStanceReason(w, s);
                if (w.stance == s) Widgets.DrawHighlightSelected(b);
                string tip = WarOrders.StanceDesc(s) + (w.stance == s ? "" : "\n\n" + "Dominion_Stance_Cost".Translate(WarOrders.StanceCost.ToString("F0"))
                             + (why != null ? "\n" + why.Colorize(ColorLibrary.RedReadable) : ""));
                if (DominionUIState.ButtonWithTip(b, WarOrders.StanceLabel(s), tip, w.stance != s && why == null)) WarOrders.SetStance(w, s);
                i++;
            }
            gy += 30f;

            // Orders
            float bw = (left.width - 9f) / 4f;
            Rect peaceB = new Rect(left.x, gy, bw, 28f);
            DiplomacyActionDef peace = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace");
            if (peace != null)
            {
                string why = DiplomacyActions.CannotUseReason(peace, enemy);
                float? chance = why == null ? peace.Worker.AcceptanceChance(enemy) : null;
                string label = "Dominion_WarTab_PeaceTable".Translate() + (chance.HasValue ? " (" + chance.Value.ToStringPercent() + ")" : "");
                if (DominionUIState.ButtonWithTip(peaceB, label, "Dominion_WarTab_PeaceTable_Desc".Translate() + (why != null ? "\n\n" + why.Colorize(ColorLibrary.RedReadable) : ""), why == null))
                    Find.WindowStack.Add(new Dialog_PeaceTable(w));
            }
            Rect mercB = new Rect(peaceB.xMax + 3f, gy, bw, 28f);
            if (DominionUIState.ButtonWithTip(mercB, "Dominion_WarTab_Mercenaries".Translate(), "Dominion_WarTab_Mercenaries_Desc".Translate(WarOrders.MercenaryCooldownDays)))
                MercenaryMenu(w);
            Rect callB = new Rect(mercB.xMax + 3f, gy, bw, 28f);
            DiplomacyActionDef call = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_CallToArms");
            if (call != null && DominionUIState.ButtonWithTip(callB, call.LabelCap, call.description)) CallToArmsMenu(call, enemy, comp);
            Rect chronB = new Rect(callB.xMax + 3f, gy, bw, 28f);
            bool open = openChronicles.Contains(w.id);
            if (DominionUIState.ButtonWithTip(chronB, (open ? "Dominion_WarTab_HideChronicle" : "Dominion_WarTab_Chronicle").Translate(w.chronicle.Count), "Dominion_WarTab_Chronicle_Desc".Translate()))
            {
                if (open) openChronicles.Remove(w.id);
                else openChronicles.Add(w.id);
            }

            // Map of the front, and the warbands in this war.
            MiniWorldMap.Draw(map, w.attacker, w.defender, new List<War> { w });
            float wy = map.yMax + 4f;
            foreach (Warband band in WarbandUtility.All())
            {
                if (!band.VisibleToPlayer || !w.Involves(band.Faction) || !w.Involves(band.target)) continue;
                if (wy + 22f > inner.yMax) break;
                Rect r = new Rect(map.x, wy, map.width, 22f);
                Widgets.DrawHighlightIfMouseover(r);
                Text.Font = GameFont.Tiny;
                string eta = band.Blockading ? "Dominion_Warband_BlockadingShort".Translate().ToString()
                    : band.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false);
                Widgets.Label(r, (band.Faction.Name + " → " + (band.targetSettlement?.Label ?? band.target?.Name ?? "?") + "  (" + eta + ")").Truncate(r.width));
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(r, band.MissionText + "\n\n" + "Dominion_Tip_ShowOnMap".Translate());
                if (Widgets.ButtonInvisible(r)) CameraJumper.TryJumpAndSelect(band);
                wy += 22f;
            }

            if (open) DrawChronicle(l, w);
            l.Gap(8f);
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
        private static void DrawChronicle(Listing_Standard l, War w)
        {
            if (w.chronicle.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("    " + "Dominion_WarTab_ChronicleEmpty".Translate());
                GUI.color = Color.white;
                return;
            }
            int now = Find.TickManager.TicksGame;
            NewsLog news = WorldComponent_Dominion.Instance.News;
            int shown = 0;
            for (int i = w.chronicle.Count - 1; i >= 0 && shown < ChronicleShown; i--, shown++)
            {
                WarChronicleEntry e = w.chronicle[i];
                Rect r = l.GetRect(20f);
                Widgets.DrawHighlightIfMouseover(r);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.x + 12f, r.y, 90f, r.height), "Dominion_News_Ago".Translate((now - e.tick).ToStringTicksToPeriod(allowSeconds: false)));
                Faction ahead = e.score >= 0f ? w.attacker : w.defender;
                GUI.color = ahead != null && ahead.IsPlayer ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.65f, 0.55f);
                Widgets.Label(new Rect(r.xMax - 70f, r.y, 70f, r.height), (Mathf.Abs(e.score) < 1f ? "0" : ahead?.Name.Truncate(40f) + " +" + Mathf.Abs(e.score).ToString("F0")));
                GUI.color = Color.white;
                Rect text = new Rect(r.x + 104f, r.y, r.width - 180f, r.height);
                Widgets.Label(text, e.text.Truncate(text.width));
                Text.Font = GameFont.Small;
                NewsItem story = e.eventId >= 0 ? FindStory(news, e.eventId) : null;
                TooltipHandler.TipRegion(r, e.text + "\n\n" + (story != null ? "Dominion_WarTab_ReadStory".Translate().ToString() : "Dominion_WarTab_NoStory".Translate().ToString()));
                if (story != null && Widgets.ButtonInvisible(r)) DominionUIState.ShowNews(story);
            }
        }

        private static NewsItem FindStory(NewsLog log, int eventId)
        {
            IReadOnlyList<NewsItem> all = log.Delivered;
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].eventId == eventId) return all[i];
            return null;
        }

        // ------------------------------------------------------------------ the world's wars

        private void DrawWorldWar(Listing_Standard l, War w)
        {
            bool known = IntelUtility.IntelOn(w.attacker, w.defender) >= IntelUtility.RestrictedIntel;
            Rect row = l.GetRect(Line);
            Widgets.DrawHighlightIfMouseover(row);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x, row.y + 2f, 20f, 20f), w.attacker);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 22f, row.y + 2f, 20f, 20f), w.defender);
            string title = w.Label + "  (" + "Dominion_WarTab_Goal".Translate(WarUtility.GoalLabel(w.goal)) + ")";
            if (w.alliedTo != null) title += "  " + "Dominion_WarTab_JoinedFor".Translate(w.alliedTo.Name);
            Rect titleRect = new Rect(row.x + 48f, row.y, row.width * 0.5f - 48f, Line);
            Widgets.Label(titleRect, title.Truncate(titleRect.width));
            TooltipHandler.TipRegion(titleRect, title + "\n\n" + WarUtility.GoalDescription(w.goal));

            Rect bar = new Rect(row.x + row.width * 0.51f, row.y + 3f, row.width * 0.33f, Line - 6f);
            if (known) DrawScoreBar(bar, w);
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(bar, IntelUtility.Unknown(IntelUtility.RestrictedIntel));
                GUI.color = Color.white;
            }
            Rect chron = new Rect(row.xMax - row.width * 0.15f, row.y, row.width * 0.15f, Line);
            bool open = openChronicles.Contains(w.id);
            if (known && DominionUIState.ButtonWithTip(chron, (open ? "Dominion_WarTab_HideChronicle" : "Dominion_WarTab_Chronicle").Translate(w.chronicle.Count), "Dominion_WarTab_Chronicle_Desc".Translate()))
            {
                if (open) openChronicles.Remove(w.id);
                else openChronicles.Add(w.id);
            }
            if (known)
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                l.Label("    " + "Dominion_WarTab_Details".Translate(w.DurationTicks.ToStringTicksToPeriod(allowSeconds: false), w.battles,
                    PeaceTerms.OccupiedBetween(w).Count) + "  " + "Dominion_WarTab_Casualties".Translate(w.deadAttacker, w.deadDefender));
                GUI.color = Color.white;
                if (open) DrawChronicle(l, w);
            }
            l.Gap(4f);
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

        // ------------------------------------------------------------------ the rest

        private static void DrawColdWars(Listing_Standard l, WorldComponent_Dominion comp)
        {
            var seen = new HashSet<long>();
            var factions = new List<Faction>(comp.Cache.Simulated);
            if (Faction.OfPlayerSilentFail != null) factions.Add(Faction.OfPlayer);
            int n = 0;
            foreach (Faction a in factions)
                foreach (Faction b in factions)
                {
                    if (a == b) continue;
                    RelationState r = comp.TryGetRelation(a, b);
                    if (r == null || !r.ColdWar) continue;
                    long key = RelationState.Key(Mathf.Min(a.loadID, b.loadID), Mathf.Max(a.loadID, b.loadID));
                    if (!seen.Add(key)) continue;
                    n++;
                    l.Label("  " + "Dominion_WarTab_ColdWarLine".Translate(a.Name, b.Name,
                        (Find.TickManager.TicksGame - r.coldWarSince).ToStringTicksToPeriod(allowSeconds: false)));
                }
            if (n == 0) None(l);
        }

        private static void DrawWarbands(Listing_Standard l)
        {
            int n = 0;
            foreach (Warband w in WarbandUtility.All())
            {
                if (!w.VisibleToPlayer) continue;
                n++;
                Rect row = l.GetRect(Line);
                string text = "  " + w.Label + ": " + w.MissionText + "  " + (w.Blockading ? "" : "Dominion_Warband_Arrives".Translate(
                    w.TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)).ToString());
                if (w.target != null && w.target.IsPlayer) GUI.color = new Color(1f, 0.7f, 0.6f);
                Widgets.Label(row.LeftPart(0.84f), text.Truncate(row.width * 0.84f));
                GUI.color = Color.white;
                if (DominionUIState.ButtonWithTip(row.RightPart(0.14f), "Dominion_WarTab_Show".Translate(), "Dominion_Tip_ShowOnMap".Translate()))
                    CameraJumper.TryJumpAndSelect(w);
            }
            if (n == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_WarTab_NoWarbands".Translate());
                GUI.color = Color.white;
            }
        }

        private static void DrawOccupied(Listing_Standard l, WorldComponent_Dominion comp)
        {
            int n = 0;
            if (comp.Wars.originalOwner.Count > 0)
                foreach (Settlement s in Find.WorldObjects.Settlements)
                {
                    int orig = comp.Wars.OriginalOwnerOf(s);
                    if (orig < 0 || s.Faction == null || s.Faction.loadID == orig) continue;
                    Faction o = comp.Cache.FactionById(orig);
                    n++;
                    Rect row = l.GetRect(Line);
                    Widgets.DrawHighlightIfMouseover(row);
                    Widgets.Label(row, "  " + "Dominion_WarTab_OccupiedLine".Translate(s.Label, s.Faction.Name, o?.Name ?? "?"));
                    TooltipHandler.TipRegion(row, "Dominion_Tip_ShowOnMap".Translate());
                    if (Widgets.ButtonInvisible(row)) CameraJumper.TryJumpAndSelect(s);
                }
            if (n == 0) None(l);
        }
    }
}
