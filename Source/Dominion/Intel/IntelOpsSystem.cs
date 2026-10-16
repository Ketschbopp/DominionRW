using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Runs the player's intelligence service: launches and resolves operations, keeps networks decaying and
    /// occasionally uncovered, and handles AI factions gathering intelligence on the player and on each other.
    /// </summary>
    public class IntelOpsSystem : SimSystem
    {
        public override int Order => 16;

        public const float CounterIntelCost = 15f;
        public const int CounterIntelDays = 30;

        private static IntelService Svc => WorldComponent_Dominion.Instance.Intel;

        // ---------------------------------------------------------------- player operations

        public static float SuccessChance(OperationDef def, Faction target, List<Pawn> agents)
        {
            if (agents.Count == 0) return 0f;
            float skill = 5f;
            if (def.skill != null)
                skill = (float)agents.Average(p => p.skills?.GetSkill(def.skill)?.Level ?? 0);
            FactionState s = WorldComponent_Dominion.Instance.GetState(target);
            float chance = def.baseSuccess + (skill - 5f) * 0.03f + Svc.Network(target) / 200f + (agents.Count - 1) * 0.05f
                           - s.personality.paranoia * 0.15f;
            if (DominionResearchDefOf.Dominion_IntelligenceAgency.IsFinished) chance += 0.05f;
            if (s.government == GovernmentType.Junta || s.government == GovernmentType.Autocracy) chance -= 0.05f;
            return Mathf.Clamp(chance * DominionMod.Settings.operationSuccess, 0.05f, 0.95f);
        }

        public static string CannotLaunchReason(OperationDef def, Faction target, List<Pawn> agents)
        {
            if (!DominionResearchDefOf.Dominion_IntelligenceNetworks.IsFinished)
                return "Dominion_Action_NeedsResearch".Translate(DominionResearchDefOf.Dominion_IntelligenceNetworks.label);
            if (target == null || target.IsPlayer || target.defeated) return "Dominion_Ops_NoTarget".Translate();
            if (agents.Count == 0) return "Dominion_Ops_NoAgents".Translate();
            if (agents.Count > def.maxAgents) return "Dominion_Ops_TooManyAgents".Translate(def.maxAgents);
            PlayerState p = WorldComponent_Dominion.Instance.Player;
            if (p.influence < def.influenceCost) return "Dominion_Action_NeedsInfluence".Translate(def.influenceCost.ToString("F0"));
            if (def.silverCost > 0 && DiplomacyHelpers.AvailableSilver() < def.silverCost) return "Dominion_Action_NeedsSilver".Translate(def.silverCost);
            if (Svc.operations.Any(o => o.target == target && o.def == def)) return "Dominion_Ops_AlreadyRunning".Translate();
            return def.Worker.CannotTargetReason(target);
        }

        /// <summary>Agents leave the colony for the length of the operation.</summary>
        public static void Launch(OperationDef def, Faction target, List<Pawn> agents)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.Player.AddInfluence(-def.influenceCost);
            if (def.silverCost > 0) DiplomacyHelpers.TakeSilver(def.silverCost);
            int now = Find.TickManager.TicksGame;
            var op = new Operation
            {
                id = Svc.NewId(),
                def = def,
                target = target,
                agents = new List<Pawn>(agents),
                startTick = now,
                endTick = now + def.durationDays * GenDate.TicksPerDay,
            };
            foreach (Pawn p in agents)
            {
                if (p.Spawned) p.DeSpawn();
                if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
            }
            Svc.operations.Add(op);
            Messages.Message("Dominion_Ops_Launched".Translate(def.label, target.Name, def.durationDays), MessageTypeDefOf.NeutralEvent, historical: true);
        }

        public override void Hourly()
        {
            int now = Find.TickManager.TicksGame;
            foreach (Operation op in Svc.operations.ToList())
                if (now >= op.endTick) Resolve(op);
        }

        private static void Resolve(Operation op)
        {
            IntelService svc = Svc;
            svc.operations.Remove(op);
            List<Pawn> agents = op.agents.Where(p => p != null && !p.Dead).ToList();
            if (op.target.defeated || agents.Count == 0)
            {
                ReturnAgents(agents);
                return;
            }

            bool success = Rand.Chance(SuccessChance(op.def, op.target, agents));
            if (success) op.def.Worker.Succeed(op);
            float exposure = success ? op.def.exposureOnSuccess : op.def.exposureOnFail;
            if (svc.Network(op.target) > 50f) exposure -= 0.1f;
            bool exposed = Rand.Chance(Mathf.Max(0f, exposure));

            string text = (success ? "Dominion_Ops_Success" : "Dominion_Ops_Failure").Translate(op.def.label, op.target.Name);
            if (exposed)
            {
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                RelationState view = comp.GetRelation(op.target, Faction.OfPlayer);
                view.favour -= 20f;
                view.grievance += 15f;
                EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, op.target, Faction.OfPlayer, detail: op.def.label);
                text += "\n\n" + "Dominion_Ops_Exposed".Translate(op.target.Name);
                if (Rand.Chance(0.4f))
                {
                    Pawn detained = agents.RandomElement();
                    agents.Remove(detained);
                    if (Find.WorldPawns.Contains(detained)) Find.WorldPawns.RemovePawn(detained);
                    op.target.kidnapped.Kidnap(detained, null);
                    text += "\n\n" + "Dominion_Ops_Detained".Translate(detained.LabelShortCap, op.target.Name);
                }
            }
            ReturnAgents(agents);
            Find.LetterStack.ReceiveLetter("Dominion_Ops_ReportLabel".Translate(op.def.LabelCap), text,
                success && !exposed ? LetterDefOf.PositiveEvent : exposed ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent, null, op.target);
        }

        /// <summary>Agents come home, walking in at the capital.</summary>
        private static void ReturnAgents(List<Pawn> agents)
        {
            Map map = VisitorUtility.PlayerCapitalMap();
            if (map == null || agents.Count == 0) return;
            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map,
                    CellFinder.EdgeRoadChance_Friendly, out IntVec3 edge))
                edge = DropCellFinder.TradeDropSpot(map);
            foreach (Pawn p in agents)
            {
                if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 4, null), map);
            }
        }

        public static string StartCounterIntel()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (!DominionResearchDefOf.Dominion_IntelligenceNetworks.IsFinished)
                return "Dominion_Action_NeedsResearch".Translate(DominionResearchDefOf.Dominion_IntelligenceNetworks.label);
            if (Svc.CounterIntelActive) return "Dominion_Ops_CounterIntelActive".Translate();
            if (comp.Player.influence < CounterIntelCost) return "Dominion_Action_NeedsInfluence".Translate(CounterIntelCost.ToString("F0"));
            comp.Player.AddInfluence(-CounterIntelCost);
            Svc.counterIntelUntil = Find.TickManager.TicksGame + CounterIntelDays * GenDate.TicksPerDay;
            return null;
        }

        // ---------------------------------------------------------------- networks and AI services

        public override void DailyGlobal()
        {
            IntelService svc = Svc;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (KeyValuePair<int, float> kv in svc.Networks.ToList())
            {
                Faction f = comp.Cache.FactionById(kv.Key);
                if (f == null || kv.Value <= 0f) continue;
                float level = kv.Value - 0.3f;
                // A suspicious faction may uncover part of the network.
                if (Rand.Chance(comp.GetState(f).personality.paranoia * 0.01f))
                {
                    level *= 0.5f;
                    Messages.Message("Dominion_Ops_NetworkUncovered".Translate(f.Name), MessageTypeDefOf.NegativeEvent, historical: true);
                }
                svc.SetNetwork(f, level);
            }
        }

        public override void DailyFaction(Faction f)
        {
            if (!DominionMod.Settings.aiIntelligence) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            Faction player = Faction.OfPlayerSilentFail;

            // Interest in the player.
            if (player != null)
            {
                float op = comp.GetRelation(f, player).opinion;
                float chance = 0.01f + s.personality.aggression * 0.01f + (op < -30f ? 0.01f : 0f);
                if (Rand.Chance(chance)) AiOperationOnPlayer(f, s);
            }

            // Interest in its main rival.
            if (Rand.Chance(0.005f + s.personality.paranoia * 0.005f))
            {
                Faction rival = comp.Cache.Simulated.Where(o => o != f).OrderBy(o => comp.GetRelation(f, o).opinion).FirstOrDefault();
                if (rival != null && comp.GetRelation(f, rival).opinion < 0f && Rand.Chance(0.3f + comp.GetState(rival).personality.paranoia * 0.3f))
                {
                    comp.GetRelation(rival, f).grievance += 15f;
                    EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, rival, f, secrecy: Secrecy.Secret);
                }
            }
        }

        private static void AiOperationOnPlayer(Faction f, FactionState s)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            bool propaganda = Rand.Chance(0.3f);
            float catchChance = 0.2f + (Svc.CounterIntelActive ? 0.3f : 0f)
                                + (DominionResearchDefOf.Dominion_IntelligenceAgency.IsFinished ? 0.1f : 0f)
                                + IntelUtility.IntelOn(f) / 300f;
            if (Rand.Chance(catchChance))
            {
                comp.GetState(f).playerIntel = Mathf.Min(100f, comp.GetState(f).playerIntel + 5f);
                foreach (Faction other in comp.Cache.Simulated)
                    if (other != f) comp.GetRelation(other, f).favour -= 3f;
                EventBus.Raise(DominionEventDefOf.Dominion_ForeignAgentsCaught, Faction.OfPlayer, f);
                Find.LetterStack.ReceiveLetter("Dominion_Ops_CaughtLabel".Translate(), "Dominion_Ops_CaughtText".Translate(f.Name),
                    LetterDefOf.NeutralEvent, null, f);
                return;
            }
            if (propaganda && DominionMod.Settings.colonistNewsThoughts)
            {
                foreach (Map map in Find.Maps)
                    if (map.IsPlayerHome)
                        foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                            p.needs?.mood?.thoughts?.memories?.TryGainMemory(DominionThoughtDefOf.Dominion_ForeignPropaganda, null, null);
            }
        }
    }
}
