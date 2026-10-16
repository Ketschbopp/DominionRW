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
        public void Notify_Monitored(Pawn pawn, int delta)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            float skill = pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 5;
            float hearing = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f;
            float gain = Props.coveragePerHour * delta / GenDate.TicksPerHour * (0.5f + skill / 10f) * hearing;
            comp.radioCoverage = System.Math.Min(100f, comp.radioCoverage + gain);
        }

        public override string CompInspectStringExtra()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            return comp == null ? null : "Dominion_Radio_Coverage".Translate(comp.radioCoverage.ToString("F0"));
        }
    }
}
