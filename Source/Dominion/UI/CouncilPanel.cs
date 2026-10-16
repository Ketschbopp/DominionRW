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
            var l = new Listing_Standard();
            l.Begin(view);

            DominionTheme.Header(l, "Dominion_Council_Title".Translate());

            bool member = CouncilUtility.IsMember(Faction.OfPlayer);
            l.Label(member
                ? "Dominion_Council_YouAreMember".Translate(CouncilUtility.VoteWeight(Faction.OfPlayer, c.attending).ToString("F1"))
                : "Dominion_Council_YouAreNotMember".Translate(DominionMod.Settings.councilMinTechLevel.ToStringHuman(), DominionResearchDefOf.Dominion_DiplomaticCorps.label));
            if (!DominionMod.Settings.councilEnabled)
                l.Label("Dominion_Council_Disabled".Translate());

            l.GapLine();
            int now = Find.TickManager.TicksGame;
            if (c.phase == CouncilPhase.Announced)
            {
                l.Label("Dominion_Council_SessionAnnounced".Translate(c.host?.Name ?? "?", Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false)));
                l.Label(AttendanceText(c, member));
                l.Label("Dominion_Council_Present".Translate(string.Join(", ", c.attending.Select(f => f.Name))));
                l.Gap(6f);
                l.Label("Dominion_Council_Agenda".Translate());
                l.Label(CouncilSystem.AgendaText(c));
                if (member && l.ButtonText("Dominion_Council_ProposeMotion".Translate()))
                    ProposeMenu();
            }
            else if (c.nextSessionTick >= 0)
                l.Label("Dominion_Council_NextSession".Translate(Mathf.Max(0, c.nextSessionTick - now).ToStringTicksToPeriod(allowSeconds: false)));

            l.GapLine();
            l.Label("Dominion_Council_Decrees".Translate());
            if (c.decrees.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_None".Translate());
                GUI.color = Color.white;
            }
            foreach (Decree d in c.decrees)
            {
                string line = "  " + d.Summary + "  (" + (d.expiresTick < 0 ? "Dominion_Dip_NoExpiry".Translate().ToString()
                    : "Dominion_Dip_Expires".Translate(Mathf.Max(0, d.expiresTick - now).ToStringTicksToPeriod(allowSeconds: false)).ToString()) + ")";
                if (d.violators.Count > 0)
                    line += "  " + "Dominion_Council_Violators".Translate(string.Join(", ", d.violators.Select(v => v.Name))).Colorize(ColorLibrary.RedReadable);
                Rect r = l.GetRect(Text.CalcHeight(line, view.width));
                Widgets.Label(r, line);
                TooltipHandler.TipRegion(r, d.def.description);
            }

            l.GapLine();
            l.Label("Dominion_Council_Members".Translate());
            foreach ((Faction f, float weight) in memberCache.Value)
            {
                if (f == null || f.defeated) continue;
                Rect row = l.GetRect(Line);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x, row.y + 2f, 20f, 20f), f);
                string present = c.phase == CouncilPhase.Announced
                    ? "  " + (c.attending.Contains(f) ? "Dominion_Council_Present_Tag" : "Dominion_Council_Expected_Tag").Translate().ToString() : "";
                int violations = c.ViolationsBy(f);
                Widgets.Label(new Rect(row.x + 24f, row.y, row.width - 24f, Line),
                    f.Name + "  " + "Dominion_Council_Weight".Translate(weight.ToString("F1")) + present
                    + (violations > 0 ? "  " + "Dominion_Council_ViolationCount".Translate(violations).Colorize(ColorLibrary.RedReadable) : ""));
            }

            height = Mathf.Max(l.CurHeight + 20f, rect.height);
            l.End();
            Widgets.EndScrollView();
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
