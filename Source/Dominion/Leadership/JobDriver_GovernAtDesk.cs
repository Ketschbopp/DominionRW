using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Dominion
{
    /// <summary>The head of state spends two hours on affairs of state at the Leadership Desk, earning influence.</summary>
    public class JobDriver_GovernAtDesk : JobDriver
    {
        private const int Duration = GenDate.TicksPerHour * 2;

        /// <summary>Influence per hour for Social 5 at a wood desk.</summary>
        public const float BaseInfluencePerHour = 1.5f;

        private Building_LeadershipDesk Desk => job.targetA.Thing as Building_LeadershipDesk;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => LeadershipUtility.HeadOfState() != pawn);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var work = ToilMaker.MakeToil("GovernAtDesk");
            work.handlingFacing = true;
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = Duration;
            work.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(job.targetA);
                Building_LeadershipDesk desk = Desk;
                if (desk == null) return;
                float social = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
                float perTick = BaseInfluencePerHour / GenDate.TicksPerHour * (0.5f + social / 10f)
                                * LeadershipUtility.InfluenceRateMultiplier(desk.Tier);
                WorldComponent_Dominion.Instance?.Player.AddInfluence(perTick * delta);
                pawn.skills?.Learn(SkillDefOf.Social, 0.03f * delta);
            };
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
        }
    }

    /// <summary>Statecraft work: only the head of state, only while influence is below the cap.</summary>
    public class WorkGiver_GovernAtDesk : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_LeadershipDesk>();

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (LeadershipUtility.HeadOfState() != pawn) return true;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            return comp == null || comp.Player.influence >= PlayerState.MaxInfluence - 0.5f;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t.Faction == pawn.Faction && !t.IsForbidden(pawn) && !t.IsBurning() && pawn.CanReserve(t, 1, -1, null, forced);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(DominionJobDefOf.Dominion_GovernAtDesk, t);
    }
}
