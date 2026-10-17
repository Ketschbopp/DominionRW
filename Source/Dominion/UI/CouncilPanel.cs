using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The Rim Council tab: membership, the coming session, the agenda, decrees in force and the members.</summary>
    public class CouncilPanel
    {
        private const float Line = 24f;
        private Vector2 scroll;

        // Members with their vote weight, sorted: weights loop over maps and allies, so not every frame.
        private readonly UICache<List<(Faction, float)>> memberCache = new UICache<List<(Faction, float)>>(() =>
        {
            List<Faction> attending = WorldComponent_Dominion.Instance.Council.attending;
            return CouncilUtility.Members().Select(f => (f, CouncilUtility.VoteWeight(f, attending))).OrderByDescending(x => x.Item2).ToList();
        }, 1f);
        private float height = 800f;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            CouncilState c = comp.Council;
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            DominionTheme.Header(l, "Dominion_Council_Title".Translate());

            // The chamber's notice board: membership and the next session, on paper.
            bool member = CouncilUtility.IsMember(Faction.OfPlayer);
            int now = Find.TickManager.TicksGame;
            var notice = new List<string>
            {
                member
                    ? "Dominion_Council_YouAreMember".Translate(CouncilUtility.VoteWeight(Faction.OfPlayer, c.attending).ToString("F1"))
                    : "Dominion_Council_YouAreNotMember".Translate(DominionMod.Settings.councilMinTechLevel.ToStringHuman(), DominionResearchDefOf.Dominion_DiplomaticCorps.label),
            };
            if (!DominionMod.Settings.councilEnabled) notice.Add("Dominion_Council_Disabled".Translate());
            if (c.phase == CouncilPhase.Announced)
            {
                notice.Add("Dominion_Council_SessionAnnounced".Translate(c.host?.Name ?? "?", Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false)));
                notice.Add(AttendanceText(c, member));
                notice.Add("Dominion_Council_Present".Translate(string.Join(", ", c.attending.Select(f => f.Name))));
            }
            else if (c.nextSessionTick >= 0)
                notice.Add("Dominion_Council_NextSession".Translate(Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false)));
            string noticeText = string.Join("\n", notice);
            Text.Font = GameFont.Small;
            Rect card = l.GetRect(Text.CalcHeight(noticeText, view.width - 90f) + 20f);
            DominionPaper.DrawCard(card);
            DominionPaper.DrawSeal(new Rect(card.x + 8f, card.y + 8f, 54f, 54f), c.host);
            DominionTheme.InkLabel(new Rect(card.x + 72f, card.y + 10f, card.width - 82f, card.height - 12f), noticeText);
            l.Gap(8f);

            // Agenda, with a forecast of how each motion would go without your vote.
            if (c.phase == CouncilPhase.Announced)
            {
                Rect head = l.GetRect(30f);
                Text.Font = GameFont.Medium;
                Widgets.Label(head.LeftPart(0.6f), "Dominion_Council_Agenda".Translate());
                Text.Font = GameFont.Small;
                if (member && DominionUIState.ButtonWithTip(head.RightPart(0.35f), "Dominion_Council_ProposeMotion".Translate(), "Dominion_Tip_ProposeMotion".Translate()))
                    ProposeMenu();
                if (c.agenda.Count == 0) None(l);
                var noVotes = new Dictionary<Motion, int>();
                foreach (Motion m in c.agenda)
                {
                    Rect row = l.GetRect(Line + 18f);
                    Widgets.DrawLightHighlight(row);
                    Widgets.DrawHighlightIfMouseover(row);
                    FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x + 4f, row.y + 4f, 20f, 20f), m.sponsor);
                    Widgets.Label(new Rect(row.x + 30f, row.y, row.width * 0.6f, Line), m.Summary.Truncate(row.width * 0.6f));
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(row.x + 30f, row.y + Line - 4f, row.width * 0.6f, 18f), "Dominion_Council_ProposedBy".Translate(m.sponsor?.Name ?? "?"));
                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                    CouncilSystem.Tally(m, noVotes, out float yes, out float no);
                    bool passes = CouncilSystem.WouldPass(m, noVotes);
                    Rect fc = new Rect(row.x + row.width * 0.62f, row.y + 8f, row.width * 0.38f - 6f, 24f);
                    GUI.color = passes ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.6f, 0.5f);
                    Widgets.Label(fc, "Dominion_Council_Forecast".Translate(passes ? "Dominion_Council_ForecastPass".Translate() : "Dominion_Council_ForecastFail".Translate(),
                        yes.ToString("F1"), no.ToString("F1")));
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(row, m.def.description + "\n\n" + "Dominion_Council_Forecast_Desc".Translate());
                    l.Gap(2f);
                }
                l.Gap(6f);
            }

            // Decrees in force, as paper notices.
            Text.Font = GameFont.Medium;
            l.Label("Dominion_Council_Decrees".Translate());
            Text.Font = GameFont.Small;
            if (c.decrees.Count == 0) None(l);
            foreach (Decree d in c.decrees)
            {
                string expires = d.expiresTick < 0 ? "Dominion_Dip_NoExpiry".Translate().ToString()
                    : "Dominion_Dip_Expires".Translate(Mathf.Max(0, d.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false)).ToString();
                string line = d.Summary + "\n" + expires + (d.InGrace ? "  ·  " + "Dominion_Council_InGrace".Translate() : "");
                if (d.violators.Count > 0)
                    line += "\n" + "Dominion_Council_Violators".Translate(string.Join(", ", d.violators.Select(v => v.Name)));
                Rect r = l.GetRect(Text.CalcHeight(line, view.width - 30f) + 14f);
                DominionPaper.DrawCard(r);
                DominionTheme.InkLabel(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, r.height - 8f), line, d.violators.Count > 0 ? DominionTheme.InkRed : DominionTheme.Ink);
                TooltipHandler.TipRegion(r, d.def.description);
                l.Gap(4f);
            }

            // Members and their weight.
            l.Gap(6f);
            Text.Font = GameFont.Medium;
            l.Label("Dominion_Council_Members".Translate());
            Text.Font = GameFont.Small;
            float maxWeight = Mathf.Max(1f, memberCache.Value.Count > 0 ? memberCache.Value.Max(x => x.Item2) : 1f);
            foreach ((Faction f, float weight) in memberCache.Value)
            {
                if (f == null || f.defeated) continue;
                Rect row = l.GetRect(Line);
                Widgets.DrawHighlightIfMouseover(row);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x, row.y + 2f, 20f, 20f), f);
                string present = c.phase == CouncilPhase.Announced
                    ? "  " + (c.attending.Contains(f) ? "Dominion_Council_Present_Tag" : "Dominion_Council_Expected_Tag").Translate().ToString() : "";
                int violations = c.ViolationsBy(f);
                Rect name = new Rect(row.x + 24f, row.y, row.width * 0.5f - 24f, Line);
                Widgets.Label(name, (f.Name + present).Truncate(name.width));
                Rect bar = new Rect(row.x + row.width * 0.5f, row.y + 4f, row.width * 0.3f, Line - 8f);
                Widgets.FillableBar(bar, weight / maxWeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(bar, "Dominion_Council_Weight".Translate(weight.ToString("F1")));
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                if (violations > 0)
                {
                    GUI.color = ColorLibrary.RedReadable;
                    Widgets.Label(new Rect(bar.xMax + 6f, row.y, row.xMax - bar.xMax - 6f, Line), "Dominion_Council_ViolationCount".Translate(violations));
                    GUI.color = Color.white;
                }
                TooltipHandler.TipRegion(row, "Dominion_Council_Weight_Desc".Translate());
            }

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
