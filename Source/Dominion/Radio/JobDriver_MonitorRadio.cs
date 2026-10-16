using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Dominion
{
    /// <summary>Sit at a radio receiver and listen for an hour.</summary>
    public class JobDriver_MonitorRadio : JobDriver
    {
        private const int Duration = GenDate.TicksPerHour;

        private CompRadioReceiver Receiver => job.targetA.Thing?.TryGetComp<CompRadioReceiver>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Receiver == null || !Receiver.CanMonitorNow);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var listen = ToilMaker.MakeToil("MonitorRadio");
            listen.handlingFacing = true;
            listen.defaultCompleteMode = ToilCompleteMode.Delay;
            listen.defaultDuration = Duration;
            listen.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(job.targetA);
                Receiver?.Notify_Monitored(pawn, delta);
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.02f * delta);
            };
            listen.WithProgressBarToilDelay(TargetIndex.A);
            yield return listen;
        }
    }
}
