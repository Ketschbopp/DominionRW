using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Dominion
{
    /// <summary>
    /// Sit at a radio receiver and work through the front order on the radio desk: read an advisory to the
    /// colony, or call around to check a rumour. Progress is kept on the order, so someone else can finish it.
    /// </summary>
    public class JobDriver_RadioOrder : JobDriver
    {
        /// <summary>A shift at the desk; the order carries over to the next one if unfinished.</summary>
        private const int ShiftTicks = 2 * GenDate.TicksPerHour;

        private CompRadioReceiver Receiver => job.targetA.Thing?.TryGetComp<CompRadioReceiver>();

        private static RadioOrder Order => WorldComponent_Dominion.Instance?.RadioOrders.Next;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        public override string GetReport()
        {
            RadioOrder o = Order;
            if (o == null) return base.GetReport();
            return "Dominion_Radio_Report".Translate(o.Spec.Label.ToLower());
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Receiver == null || !Receiver.CanMonitorNow || Order == null);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var work = ToilMaker.MakeToil("RadioOrder");
            work.handlingFacing = true;
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = ShiftTicks;
            work.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(job.targetA);
                RadioOrder o = Order;
                if (o == null) return;
                o.progress += delta * RadioOrderUtility.WorkSpeed(pawn, o);
                pawn.skills?.Learn(RadioOrderUtility.Skill(o), 0.04f * delta);
                // Being on the air also keeps the receiver tuned.
                Receiver?.Notify_Monitored(pawn, delta * 0.5f);
                if (o.progress >= o.WorkNeeded)
                {
                    RadioOrderUtility.Complete(o, pawn);
                    ReadyForNextToil();
                }
            };
            work.WithProgressBar(TargetIndex.A, () => Order == null ? 1f : Order.progress / Order.WorkNeeded);
            work.WithEffect(EffecterDefOf.Research, TargetIndex.A);
            yield return work;
        }
    }

    /// <summary>Research work: take the orders queued on the radio desk. Comes before plain monitoring.</summary>
    public class WorkGiver_RadioOrder : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfDef(DominionThingDefOf.Dominion_RadioReceiver);

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished) return true;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return true;
            comp.RadioOrders.Prune(comp.News);
            return comp.RadioOrders.Next == null;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.Faction != pawn.Faction || t.IsForbidden(pawn) || t.IsBurning()) return false;
            CompRadioReceiver receiver = t.TryGetComp<CompRadioReceiver>();
            if (receiver == null || !receiver.CanMonitorNow) return false;
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(DominionJobDefOf.Dominion_RadioOrder, t);
    }
}
