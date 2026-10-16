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
            RadioOrders orders = comp.RadioOrders;
            var advisory = new Command_Action
            {
                defaultLabel = (orders.AdvisoryQueued ? "Dominion_Radio_CancelAdvisory" : "Dominion_Radio_IssueAdvisory").Translate(),
                defaultDesc = "Dominion_Radio_IssueAdvisory_Desc".Translate(RadioOrders.AdvisoryDays, RadioOrderUtility.UpsetColonists.Value),
                icon = DominionTheme.GizmoAdvisory,
                action = RadioOrderUtility.OrderAdvisory,
            };
            if (orders.AdvisoryInForce && !orders.AdvisoryQueued)
                advisory.Disable("Dominion_Radio_AdvisoryInForce".Translate((orders.advisoryUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod()));
            yield return advisory;
            yield return new Command_Action
            {
                defaultLabel = "Dominion_Radio_Investigate".Translate(),
                defaultDesc = "Dominion_Radio_Investigate_Desc".Translate(),
                icon = DominionTheme.GizmoInvestigate,
                action = () => Find.WindowStack.Add(new FloatMenu(RumourOptions(comp))),
            };
        }

        private static List<FloatMenuOption> RumourOptions(WorldComponent_Dominion comp)
        {
            var options = new List<FloatMenuOption>();
            IReadOnlyList<NewsItem> news = comp.News.Delivered;
            for (int i = news.Count - 1; i >= 0 && options.Count < 15; i--)
            {
                NewsItem n = news[i];
                if (!RadioOrderUtility.CanInvestigate(n)) continue;
                bool queued = comp.RadioOrders.Investigating(n);
                string label = (queued ? "Dominion_Radio_InvestigateCancelOption" : "Dominion_Radio_InvestigateOption").Translate(n.headline.Truncate(320f));
                options.Add(new FloatMenuOption(label, () => RadioOrderUtility.OrderInvestigation(n)));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("Dominion_Radio_NoRumours".Translate(), null));
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
