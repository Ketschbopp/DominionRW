using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Dominion
{
    /// <summary>
    /// A studio and transmitter. Pick a programme (and, for some, a target faction); a colonist on research work
    /// operates it, adding airtime. Airtime turns into effects once a day.
    /// </summary>
    public class Building_BroadcastCenter : Building
    {
        public BroadcastProgramDef program;
        public Faction target;

        private CompPowerTrader power;

        public bool CanOperate => (power == null || power.PowerOn) && !this.IsBrokenDown() && program != null
                                  && (!program.needsTarget || (target != null && !target.defeated));

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            power = GetComp<CompPowerTrader>();
            program ??= DefDatabase<BroadcastProgramDef>.AllDefsListForReading.OrderBy(d => d.order).FirstOrDefault();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = "Dominion_Broadcast_Program".Translate(program?.label ?? "-"),
                defaultDesc = program?.description ?? "",
                icon = DominionTex.Broadcast,
                action = () => Find.WindowStack.Add(new FloatMenu(DefDatabase<BroadcastProgramDef>.AllDefsListForReading.OrderBy(d => d.order)
                    .Select(d => new FloatMenuOption(d.LabelCap, () => program = d)).ToList())),
            };
            if (program != null && program.needsTarget)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dominion_Broadcast_Target".Translate(target?.Name ?? "-"),
                    defaultDesc = "Dominion_Broadcast_Target_Desc".Translate(),
                    icon = DominionTex.Diplomacy,
                    action = () => Find.WindowStack.Add(new FloatMenu(WorldComponent_Dominion.Instance.Cache.Simulated
                        .Select(f => new FloatMenuOption(f.Name, () => target = f)).ToList())),
                };
            }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Dominion_Broadcast_Inspect".Translate(program?.label ?? "-"));
            if (program != null && program.needsTarget)
                sb.Append(": " + (target?.Name ?? "Dominion_Broadcast_NoTarget".Translate().ToString()));
            return sb.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref program, "program");
            Scribe_References.Look(ref target, "target");
        }
    }

    /// <summary>Run the studio for two hours. Social skill makes better broadcasts.</summary>
    public class JobDriver_OperateBroadcast : JobDriver
    {
        private const int Duration = GenDate.TicksPerHour * 2;

        private Building_BroadcastCenter Center => job.targetA.Thing as Building_BroadcastCenter;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.A);
            this.FailOn(() => Center == null || !Center.CanOperate);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);

            var work = ToilMaker.MakeToil("OperateBroadcast");
            work.handlingFacing = true;
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.defaultDuration = Duration;
            work.tickIntervalAction = delta =>
            {
                pawn.rotationTracker.FaceTarget(job.targetA);
                Building_BroadcastCenter c = Center;
                if (c == null) return;
                float social = pawn.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
                float hours = (float)delta / GenDate.TicksPerHour * (0.5f + social / 10f);
                WorldComponent_Dominion.Instance?.Broadcast.AddAirtime(c.program, c.program.needsTarget ? c.target : null, hours);
                pawn.skills?.Learn(SkillDefOf.Social, 0.02f * delta);
            };
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
        }
    }

    public class WorkGiver_OperateBroadcast : WorkGiver_Scanner
    {
        /// <summary>More airtime than this per day adds little; colonists stop and do other work.</summary>
        private const float EnoughAirtime = 8f;

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_BroadcastCenter>();

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !DominionResearchDefOf.Dominion_Broadcasting.IsFinished;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_BroadcastCenter c) || !c.CanOperate) return false;
            if (t.Faction != pawn.Faction || t.IsForbidden(pawn) || t.IsBurning()) return false;
            if (!forced)
            {
                if (WorldComponent_Dominion.Instance.Broadcast.AirtimeFor(c.program, c.program.needsTarget ? c.target : null) >= EnoughAirtime)
                    return false;
            }
            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            JobMaker.MakeJob(DominionBroadcastDefOf.Dominion_OperateBroadcast, t);
    }

    [DefOf]
    public static class DominionBroadcastDefOf
    {
        public static JobDef Dominion_OperateBroadcast;

        static DominionBroadcastDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionBroadcastDefOf));
        }
    }
}
