using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The War tab: wars under way with goals and war score, what a peace would bring, cold wars, warbands in the
    /// field that you can see, blockades and occupied settlements.
    /// </summary>
    public class WarPanel
    {
        private const float Line = 24f;
        private Vector2 scroll;
        private float height = 800f;

        // Terms are worked out from the whole world state; recomputed at most twice a second.
        private readonly Dictionary<int, string> termsCache = new Dictionary<int, string>();
        private float termsTime;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard();
            l.Begin(view);
            if (Time.realtimeSinceStartup > termsTime)
            {
                termsCache.Clear();
                termsTime = Time.realtimeSinceStartup + 0.5f;
            }

            Header(l, "Dominion_WarTab_Yours".Translate());
            List<War> mine = comp.Wars.wars.Where(w => w.InvolvesPlayer).ToList();
            if (mine.Count == 0) None(l);
            foreach (War w in mine) DrawWar(l, w, comp, true);

            Header(l, "Dominion_WarTab_World".Translate());
            List<War> others = comp.Wars.wars.Where(w => !w.InvolvesPlayer).ToList();
            if (others.Count == 0) None(l);
            foreach (War w in others) DrawWar(l, w, comp, false);

            Header(l, "Dominion_WarTab_ColdWars".Translate());
            DrawColdWars(l, comp);

            Header(l, "Dominion_WarTab_Warbands".Translate());
            DrawWarbands(l);

            Header(l, "Dominion_WarTab_Occupied".Translate());
            DrawOccupied(l, comp);

            Header(l, "Dominion_WarTab_Blockades".Translate());
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

        private static void Header(Listing_Standard l, string text) => DominionTheme.Header(l, text);

        private static void None(Listing_Standard l)
        {
            GUI.color = Color.gray;
            l.Label("  " + "Dominion_None".Translate());
            GUI.color = Color.white;
        }

        private void DrawWar(Listing_Standard l, War w, WorldComponent_Dominion comp, bool player)
        {
            bool known = player || IntelUtility.IntelOn(w.attacker, w.defender) >= IntelUtility.RestrictedIntel;
            Rect row = l.GetRect(Line);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x, row.y + 2f, 20f, 20f), w.attacker);
            FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 22f, row.y + 2f, 20f, 20f), w.defender);
            string title = w.Label + "  (" + "Dominion_WarTab_Goal".Translate(WarUtility.GoalLabel(w.goal)) + ")";
            if (w.alliedTo != null) title += "  " + "Dominion_WarTab_JoinedFor".Translate(w.alliedTo.Name);
            Rect titleRect = new Rect(row.x + 48f, row.y, row.width * 0.62f - 48f, Line);
            Widgets.Label(titleRect, title.Truncate(titleRect.width));
            TooltipHandler.TipRegion(titleRect, title + "\n\n" + WarUtility.GoalDescription(w.goal));

            Rect bar = new Rect(row.x + row.width * 0.63f, row.y + 3f, row.width * 0.36f, Line - 6f);
            if (known) DrawScoreBar(bar, w);
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(bar, IntelUtility.Unknown(IntelUtility.RestrictedIntel));
                GUI.color = Color.white;
            }

            if (known)
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                l.Label("    " + "Dominion_WarTab_Details".Translate(w.DurationTicks.ToStringTicksToPeriod(allowSeconds: false), w.battles,
                    PeaceTerms.OccupiedBetween(w).Count));
                GUI.color = Color.white;
            }

            if (!player) return;
            if (!termsCache.TryGetValue(w.id, out string terms))
                termsCache[w.id] = terms = PeaceTerms.Describe(w);
            Rect termsRow = l.GetRect(Line);
            Widgets.Label(termsRow.LeftPart(0.62f), ("    " + "Dominion_WarTab_PeaceNow".Translate()).Truncate(termsRow.width * 0.62f));
            TooltipHandler.TipRegion(termsRow.LeftPart(0.62f), terms);

            Faction enemy = w.Other(Faction.OfPlayer);
            DiplomacyActionDef peace = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace");
            DiplomacyActionDef call = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_CallToArms");
            Rect b1 = new Rect(termsRow.x + termsRow.width * 0.63f, termsRow.y, termsRow.width * 0.18f - 2f, Line);
            Rect b2 = new Rect(b1.xMax + 4f, termsRow.y, termsRow.width * 0.18f - 2f, Line);
            if (peace != null) ActionButton(b1, peace, enemy);
            if (call != null && Widgets.ButtonText(b2, call.LabelCap))
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
            l.Gap(4f);
        }

        private static void ActionButton(Rect r, DiplomacyActionDef def, Faction f)
        {
            string why = DiplomacyActions.CannotUseReason(def, f);
            float? chance = def.Worker.AcceptanceChance(f);
            string label = def.LabelCap + (chance.HasValue ? " (" + chance.Value.ToStringPercent() + ")" : "");
            if (Widgets.ButtonText(r, label.Truncate(r.width), active: why == null) && why == null)
                def.Worker.Execute(f);
            if (why != null) TooltipHandler.TipRegion(r, why);
        }

        /// <summary>Red to the left (defender winning), green to the right (attacker winning).</summary>
        private static void DrawScoreBar(Rect bar, War w)
        {
            Widgets.DrawBoxSolid(bar, new Color(0.15f, 0.15f, 0.15f));
            float mid = bar.x + bar.width / 2f;
            float len = bar.width / 2f * Mathf.Abs(w.score) / 100f;
            Rect fill = w.score >= 0f ? new Rect(mid, bar.y, len, bar.height) : new Rect(mid - len, bar.y, len, bar.height);
            Widgets.DrawBoxSolid(fill, w.score >= 0f ? new Color(0.75f, 0.35f, 0.25f) : new Color(0.3f, 0.5f, 0.75f));
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
                Widgets.Label(row.LeftPart(0.84f), text.Truncate(row.width * 0.84f));
                if (Widgets.ButtonText(row.RightPart(0.14f), "Dominion_WarTab_Show".Translate()))
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
                    l.Label("  " + "Dominion_WarTab_OccupiedLine".Translate(s.Label, s.Faction.Name, o?.Name ?? "?"));
                }
            if (n == 0) None(l);
        }
    }
}
