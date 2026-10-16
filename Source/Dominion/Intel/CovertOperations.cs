using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    // Covert actions (design §5.5). Each worker applies the effect of a successful operation; the operation def
    // carries the numbers, the news event and whether it can be run off-map, on the map, or both.

    /// <summary>Kills the faction's leader. Off-map it needs the leader free in the world; on the map, the agents do it.</summary>
    public class Operation_Assassinate : OperationWorker
    {
        public override string CannotTargetReason(Faction f) =>
            f.leader == null && GovernorOf(f) == null ? "Dominion_Ops_NoLeader".Translate(f.Name) : null;

        public override void Succeed(Operation op)
        {
            Faction f = op.target;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            if (op.physical)
            {
                // The agents already did it on the map; a senior official instead of the leader still shakes the regime.
                if (!op.targetIsLeader)
                {
                    s.stability = Mathf.Max(0f, s.stability - 8f);
                    s.legitimacy = Mathf.Max(0f, s.legitimacy - 8f);
                }
                return;
            }
            Pawn victim = VisitorUtility.LeaderIsFree(f.leader) ? f.leader : null;
            // Simple Leadership: with the leader out of reach, the capital's governor is the target.
            victim ??= GovernorOf(f);
            if (victim != null && !victim.Dead && !victim.Spawned)
                victim.Kill(null);
            s.stability = Mathf.Max(0f, s.stability - 5f);
        }

        private static Pawn GovernorOf(Faction f)
        {
            var sl = IntegrationRegistry.Get<Integration_SimpleLeadership>();
            Settlement cap = WorldComponent_Dominion.Instance.GetState(f).capital;
            Pawn p = sl != null && cap != null ? sl.GovernorOf(cap) : null;
            return p != null && !p.Dead && !p.Spawned && p != f.leader ? p : null;
        }
    }

    /// <summary>Wrecks depots, power or workshops: economy and military take a blow, and E&amp;D stockpiles burn.</summary>
    public class Operation_Sabotage : OperationWorker
    {
        /// <summary>How long sabotage hurts: the economy and the army recover after about six days.</summary>
        public const int SabotageDays = 6;

        public override void Succeed(Operation op)
        {
            float m = Mult(op);
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(op.target);
            EconomyEffects.Shock(op.target, -0.08f * m, days: SabotageDays);
            s.sabotagedUntilTick = Find.TickManager.TicksGame + SabotageDays * GenDate.TicksPerDay;
            EconomyEffects.DestroyGoods(op.target, 1500f * m);
            WarUtility.AddScore(Faction.OfPlayer, op.target, 5f * m);
        }
    }

    /// <summary>Money and agitators: riots, and maybe more.</summary>
    public class Operation_InciteRiots : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(op.target);
            s.stability = Mathf.Max(0f, s.stability - 10f * Mult(op));
            s.blocs[(int)Bloc.Reformists] += 0.05f;
            s.NormalizeBlocs();
            EventBus.Raise(DominionEventDefOf.Dominion_Riots, op.target);
            if (s.stability < 20f && s.legitimacy < 45f && Rand.Chance(0.3f))
                EventBus.Raise(DominionEventDefOf.Dominion_Coup, op.target);
        }
    }

    /// <summary>Topples the government and installs a regime that owes you everything: a puppet.</summary>
    public class Operation_Coup : OperationWorker
    {
        public override string CannotTargetReason(Faction f)
        {
            if (f.HostileTo(Faction.OfPlayer)) return "Dominion_Ops_NotWhileHostile".Translate();
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            if (s.stability > def.maxTargetStability)
                return "Dominion_Ops_TooStable".Translate(f.Name, def.maxTargetStability.ToString("F0"));
            if (WarSystem.IsPuppetOf(f, Faction.OfPlayer, WorldComponent_Dominion.Instance)) return "Dominion_Ops_AlreadyPuppet".Translate(f.Name);
            return null;
        }

        public override void Succeed(Operation op)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction f = op.target;
            EventBus.Raise(DominionEventDefOf.Dominion_Coup, f);
            RelationState view = comp.GetRelation(f, Faction.OfPlayer);
            view.grievance = 0f;
            view.favour = Mathf.Max(view.favour, 30f);
            view.opinion = Mathf.Max(view.opinion, 30f);
            if (!f.HostileTo(Faction.OfPlayer) && !comp.Treaties.Has(f, Faction.OfPlayer, DominionTreatyDefOf.Dominion_Puppet))
                comp.Treaties.Sign(DominionTreatyDefOf.Dominion_Puppet, f, Faction.OfPlayer, Faction.OfPlayer);
        }
    }

    /// <summary>Makes it look as if a third faction did it. They take the blame, and may take a war for it.</summary>
    public class Operation_FalseFlag : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            Faction f = op.target, blamed = op.thirdParty;
            if (blamed == null || blamed.defeated) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState view = comp.GetRelation(f, blamed);
            view.grievance = Mathf.Min(100f, view.grievance + 35f * Mult(op));
            view.favour -= 20f;
            view.opinion = Mathf.Max(-100f, view.opinion - 20f);
            // As far as the world knows, the blamed faction's agents were caught.
            EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, f, blamed);
            if (DiplomacyUtility.AiMayChange(f, blamed) && !f.HostileTo(blamed) && view.opinion < -40f
                && !comp.Treaties.AnyNonAggression(f, blamed)
                && Rand.Chance(0.25f * comp.GetState(f).personality.aggression + 0.05f))
                DiplomacyUtility.SetRelationKind(f, blamed, FactionRelationKind.Hostile, f, "Dominion_Reason_FalseFlag".Translate());
        }
    }

    /// <summary>Copies their research: progress on your current project.</summary>
    public class Operation_StealTechnology : OperationWorker
    {
        public override string CannotTargetReason(Faction f)
        {
            if (f.def.techLevel < Faction.OfPlayer.def.techLevel) return "Dominion_Ops_NothingToSteal".Translate(f.Name);
            return Find.ResearchManager.GetProject() == null ? "Dominion_Ops_NoProject".Translate() : null;
        }

        public override void Succeed(Operation op)
        {
            ResearchProjectDef proj = Find.ResearchManager.GetProject();
            if (proj == null || proj.IsFinished)
            {
                FactionState s = WorldComponent_Dominion.Instance.GetState(op.target);
                s.playerIntel = Mathf.Min(100f, s.playerIntel + 15f);
                return;
            }
            float gap = 1f + Mathf.Max(0, (int)op.target.def.techLevel - (int)Faction.OfPlayer.def.techLevel) * 0.5f;
            float amount = Mathf.Min(proj.Cost - proj.ProgressReal, proj.Cost * 0.3f * gap * Mult(op));
            if (amount > 0f) Find.ResearchManager.AddProgress(proj, amount, null);
        }
    }

    /// <summary>Gets a captured colonist out. The rescue itself happens on the map.</summary>
    public class Operation_Rescue : OperationWorker
    {
        public override string CannotTargetReason(Faction f) =>
            HostageUtility.HeldByThem(f).Count == 0 ? "Dominion_Ops_NoHostages".Translate(f.Name) : null;

        public override void Succeed(Operation op)
        {
            if (op.hostage != null)
                EventBus.Raise(DominionEventDefOf.Dominion_HostageRescued, Faction.OfPlayer, op.target, detail: op.hostage.LabelShort);
        }
    }
}
