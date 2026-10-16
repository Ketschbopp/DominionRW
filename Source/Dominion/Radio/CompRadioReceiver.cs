using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    public class CompProperties_RadioReceiver : CompProperties
    {
        /// <summary>Radio coverage gained per hour of monitoring by an average listener (Intellectual 5).</summary>
        public float coveragePerHour = 4f;

        public CompProperties_RadioReceiver()
        {
            compClass = typeof(CompRadioReceiver);
        }
    }

    /// <summary>
    /// A receiver a colonist can monitor. Monitoring raises the colony-wide radio coverage, which speeds up
    /// news and raises intel on every faction. Coverage decays daily, so someone has to keep listening.
    /// </summary>
    public class CompRadioReceiver : ThingComp
    {
        private CompPowerTrader power;

        public CompProperties_RadioReceiver Props => (CompProperties_RadioReceiver)props;

        public bool CanMonitorNow => (power == null || power.PowerOn) && !parent.IsBrokenDown();

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            power = parent.GetComp<CompPowerTrader>();
        }

        /// <summary>Called by the monitoring job every <paramref name="delta"/> ticks.</summary>
        public void Notify_Monitored(Pawn pawn, float delta)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            float skill = pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 5;
            float hearing = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f;
            float gain = Props.coveragePerHour * delta / GenDate.TicksPerHour * (0.5f + skill / 10f) * hearing;
            comp.radioCoverage = System.Math.Min(100f, comp.radioCoverage + gain);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Dominion_Radio_IssueAdvisory".Translate(),
                defaultDesc = "Dominion_Radio_AdvisoryMenu_Desc".Translate(RadioOrderUtility.UpsetColonists.Value),
                icon = DominionTheme.GizmoAdvisory,
                action = () => Find.WindowStack.Add(new FloatMenu(RadioOrderUtility.AdvisoryMenu())),
            };
            yield return new Command_Action
            {
                defaultLabel = "Dominion_Radio_Respond".Translate(),
                defaultDesc = "Dominion_Radio_RespondMenu_Desc".Translate(),
                icon = DominionTheme.GizmoInvestigate,
                action = () => Find.WindowStack.Add(new FloatMenu(StoryOptions(comp))),
            };
        }

        /// <summary>Recent stories the radio desk can act on, each with the responses that apply to it.</summary>
        private static List<FloatMenuOption> StoryOptions(WorldComponent_Dominion comp)
        {
            var options = new List<FloatMenuOption>();
            IReadOnlyList<NewsItem> news = comp.News.Delivered;
            int stories = 0;
            for (int i = news.Count - 1; i >= 0 && stories < 10; i--)
            {
                NewsItem n = news[i];
                if (!RadioOrderUtility.AnyResponse(n)) continue;
                stories++;
                foreach (FloatMenuOption o in RadioOrderUtility.StoryMenu(n))
                {
                    o.Label = o.Label + ": " + n.headline.Truncate(300f);
                    options.Add(o);
                }
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Dominion_Radio_NoStories".Translate(), null));
            return options;
        }

        public override string CompInspectStringExtra()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return null;
            string s = "Dominion_Radio_Coverage".Translate(comp.radioCoverage.ToString("F0"));
            Thing antenna = RadioAntenna.LinkedAntenna(parent);
            s += "\n" + (antenna != null ? "Dominion_Radio_AntennaLinked".Translate(antenna.LabelShort) : "Dominion_Radio_NoAntenna".Translate());
            int queued = comp.RadioOrders.Orders.Count;
            if (queued > 0) s += "\n" + "Dominion_Radio_OrdersQueued".Translate(queued);
            if (comp.RadioOrders.AdvisoryInForce)
                s += "\n" + "Dominion_Radio_AdvisoryInForce".Translate((comp.RadioOrders.advisoryUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            return s;
        }
    }
}
