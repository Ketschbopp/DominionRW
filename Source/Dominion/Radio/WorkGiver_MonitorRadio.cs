using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Dominion
{
    /// <summary>Research work: keep the radio monitored while coverage is below what is useful.</summary>
    public class WorkGiver_MonitorRadio : WorkGiver_Scanner
    {
        /// <summary>Above this, colonists stop monitoring until coverage decays.</summary>
        private const float EnoughCoverage = 90f;

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfDef(DominionThingDefOf.Dominion_RadioReceiver);

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished) return true;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            return comp == null || (!forced && comp.radioCoverage >= EnoughCoverage);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Faction != pawn.Faction || t.IsForbidden(pawn) || t.IsBurning()) return false;
            CompRadioReceiver receiver = t.TryGetComp<CompRadioReceiver>();
            if (receiver == null || !receiver.CanMonitorNow) return false;
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(DominionJobDefOf.Dominion_MonitorRadio, t);
    }

    [DefOf]
    public static class DominionJobDefOf
    {
        public static JobDef Dominion_MonitorRadio;

        static DominionJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionJobDefOf));
        }
    }

    [DefOf]
    public static class DominionThingDefOf
    {
        public static ThingDef Dominion_RadioReceiver;

        static DominionThingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionThingDefOf));
        }
    }
}
