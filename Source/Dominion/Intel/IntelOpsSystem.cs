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

        public static string CannotLaunchReason(OperationDef def, Faction target, List<Pawn> agents, Faction thirdParty = null)
        {
            if (!DominionResearchDefOf.Dominion_IntelligenceNetworks.IsFinished)
                return "Dominion_Action_NeedsResearch".Translate(DominionResearchDefOf.Dominion_IntelligenceNetworks.label);
            if (!def.allowAbstract) return "Dominion_Ops_PhysicalOnly".Translate();
            if (target == null || target.IsPlayer || target.defeated) return "Dominion_Ops_NoTarget".Translate();
            if (def.needsThirdParty && (thirdParty == null || thirdParty == target || thirdParty.IsPlayer || thirdParty.defeated))
                return "Dominion_Ops_NoThirdParty".Translate();
            if (Svc.Network(target) < def.minNetwork) return "Dominion_Ops_NeedsNetwork".Translate(def.minNetwork.ToString("F0"));
            if (agents.Count == 0) return "Dominion_Ops_NoAgents".Translate();
            if (agents.Count > def.maxAgents) return "Dominion_Ops_TooManyAgents".Translate(def.maxAgents);
            // Agents abroad are not on any map; sending everyone would end the game.
            if (!AnyColonistLeftBehind(agents)) return "Dominion_Ops_LastColonists".Translate();
            PlayerState p = WorldComponent_Dominion.Instance.Player;
            if (p.influence < def.influenceCost) return "Dominion_Action_NeedsInfluence".Translate(def.influenceCost.ToString("F0"));
            if (def.silverCost > 0 && DiplomacyHelpers.AvailableSilver() < def.silverCost) return "Dominion_Action_NeedsSilver".Translate(def.silverCost);
            if (Svc.operations.Any(o => o.target == target && o.def == def)) return "Dominion_Ops_AlreadyRunning".Translate();
            return def.Worker.CannotTargetReason(target);
        }

        private static bool AnyColonistLeftBehind(List<Pawn> agents)
        {
            foreach (Pawn p in PawnsFinder.AllMaps_FreeColonistsSpawned)
                if (!agents.Contains(p) && !p.Downed) return true;
            foreach (Caravan c in Find.WorldObjects.Caravans)
                if (c.IsPlayerControlled && c.PawnsListForReading.Any(p => p.IsColonist && !agents.Contains(p))) return true;
            return false;
        }

        /// <summary>Agents leave the colony for the length of the operation.</summary>
        public static void Launch(OperationDef def, Faction target, List<Pawn> agents, Faction thirdParty = null)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (def.silverCost > 0 && DiplomacyHelpers.AvailableSilverFresh() < def.silverCost)
            {
                Messages.Message("Dominion_Action_NeedsSilver".Translate(def.silverCost), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            comp.Player.AddInfluence(-def.influenceCost);
            // Bribes and agitators' pay end up in their economy.
            if (def.silverCost > 0) EconomyEffects.Transfer(Faction.OfPlayer, target, def.silverCost);
            int now = Find.TickManager.TicksGame;
            var op = new Operation
            {
                id = Svc.NewId(),
                def = def,
                target = target,
                agents = new List<Pawn>(agents),
                thirdParty = def.needsThirdParty ? thirdParty : null,
                startTick = now,
                endTick = now + def.durationDays * GenDate.TicksPerDay,
            };
            foreach (Pawn p in agents)
            {
                // Leaving the map (rather than despawning) releases beds, lords and jobs properly.
                if (p.Spawned) p.ExitMap(false, Rot4.Invalid);
                if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
            }
            Svc.operations.Add(op);
            Messages.Message("Dominion_Ops_Launched".Translate(def.label, target.Name, def.durationDays), MessageTypeDefOf.NeutralEvent, historical: true);
        }

        public override void Hourly()
        {
            if (Svc.returning.Count > 0)
            {
                var back = new List<Pawn>(Svc.returning);
                Svc.returning.Clear();
                ReturnAgents(back);
            }
            int now = Find.TickManager.TicksGame;
            foreach (Operation op in Svc.operations.ToList())
            {
                if (op.physical) PhysicalOps.Check(op, hourly: true);
                else if (now >= op.endTick) Resolve(op);
            }
        }

        /// <summary>Physical operations are watched closely: objectives and the agents leaving are noticed within a few seconds.</summary>
        public override void Slot(int slot)
        {
            List<Operation> ops = Svc.operations;
            for (int i = ops.Count - 1; i >= 0; i--)
                if (i < ops.Count && ops[i].physical) PhysicalOps.Check(ops[i], hourly: false);
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
            string blocked = op.def.Worker.CannotTargetReason(op.target);
            bool success = blocked == null && Rand.Chance(SuccessChance(op.def, op.target, agents));
            float exposure = success ? op.def.exposureOnSuccess : op.def.exposureOnFail;
            if (svc.Network(op.target) > 50f) exposure -= 0.1f;
            bool exposed = Rand.Chance(Mathf.Max(0f, exposure));
            Conclude(op, success, exposed, agents);
            ReturnAgents(agents);
        }

        /// <summary>
        /// The outcome of an operation, abstract or physical: its effect, the news, and the price of being caught.
        /// <paramref name="agents"/> are the agents abroad (abstract only); one of them may be detained.
        /// </summary>
        public static void Conclude(Operation op, bool success, bool exposed, List<Pawn> agents)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction player = Faction.OfPlayer;
            if (success)
            {
                op.def.Worker.Succeed(op);
                if (op.def.successEvent != null)
                    EventBus.Raise(op.def.successEvent, op.target, player, detail: op.thirdParty?.Name ?? op.hostage?.LabelShort,
                        secrecy: exposed ? Secrecy.Public : (Secrecy?)null);
            }

            string text = (success ? "Dominion_Ops_Success" : "Dominion_Ops_Failure").Translate(op.def.label, op.target.Name);
            if (exposed)
            {
                float scale = op.def.hostileAct ? 2f : 1f;
                RelationState view = comp.GetRelation(op.target, player);
                view.favour -= 20f * scale;
                view.grievance += 15f * scale;
                if (op.thirdParty != null)
                {
                    // The truth comes out: the faction that was blamed has a grievance too.
                    RelationState blamed = comp.GetRelation(op.thirdParty, player);
                    blamed.grievance += 25f;
                    comp.GetRelation(op.target, op.thirdParty).grievance *= 0.5f;
                }
                EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, op.target, player, detail: op.def.label);
                text += "\n\n" + (success ? "Dominion_Ops_ExposedAfterSuccess" : "Dominion_Ops_Exposed").Translate(op.target.Name);
                if (agents != null && agents.Count > 0 && Rand.Chance(0.4f))
                {
                    Pawn detained = agents.RandomElement();
                    agents.Remove(detained);
                    if (Find.WorldPawns.Contains(detained)) Find.WorldPawns.RemovePawn(detained);
                    op.target.kidnapped.Kidnap(detained, null);
                    text += "\n\n" + "Dominion_Ops_Detained".Translate(detained.LabelShortCap, op.target.Name);
                }
                // Assassins, saboteurs and plotters caught red-handed can mean war.
                if (op.def.hostileAct && !op.target.HostileTo(player) && !op.target.def.permanentEnemy
                    && Rand.Chance(0.15f + comp.GetState(op.target).personality.aggression * 0.3f))
                {
                    DiplomacyUtility.SetRelationKind(op.target, player, FactionRelationKind.Hostile, op.target, "Dominion_Reason_CovertExposed".Translate());
                    text += "\n\n" + "Dominion_Ops_ExposedWar".Translate(op.target.Name);
                }
            }
            Find.LetterStack.ReceiveLetter("Dominion_Ops_ReportLabel".Translate(op.def.LabelCap), text,
                success && !exposed ? LetterDefOf.PositiveEvent : exposed ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent, null, op.target);
        }

        /// <summary>Agents come home, walking in at the capital.</summary>
        private static void ReturnAgents(List<Pawn> agents)
        {
            if (agents.Count == 0) return;
            Map map = VisitorUtility.PlayerCapitalMap();
            if (map == null)
            {
                foreach (Pawn p in agents)
                    if (!p.Dead && !Svc.returning.Contains(p)) Svc.returning.Add(p);
                return;
            }
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
                float chance = 0.01f + s.personality.aggression * 0.01f + (op < -30f ? 0.01f : 0f)
                               + (ColdWarSystem.InColdWar(f, player) || f.HostileTo(player) ? 0.015f : 0f);
                if (Rand.Chance(chance)) AiOperationOnPlayer(f, s);
            }

            // Interest in its main rival.
            if (Rand.Chance(0.005f + s.personality.paranoia * 0.005f))
            {
                Faction rival = comp.Cache.Simulated.Where(o => o != f).OrderBy(o => comp.GetRelation(f, o).opinion).FirstOrDefault();
                if (rival != null && comp.GetRelation(f, rival).opinion < 0f)
                {
                    if (Rand.Chance(0.3f + comp.GetState(rival).personality.paranoia * 0.3f))
                    {
                        comp.GetRelation(rival, f).grievance += 15f;
                        EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, rival, f, secrecy: Secrecy.Secret);
                    }
                    else if (DominionMod.Settings.aiCovertOps)
                        AiCovertAction(f, s, rival, comp);
                }
            }
        }

        /// <summary>
        /// An AI intelligence service that got its agents in acts against the rival. Rivals in a cold war or at war try
        /// harder things. Every action is a secret event: the player hears of it only with good intel.
        /// </summary>
        private static void AiCovertAction(Faction f, FactionState s, Faction rival, WorldComponent_Dominion comp)
        {
            FactionState rs = comp.GetState(rival);
            bool bitter = f.HostileTo(rival) || ColdWarSystem.InColdWar(f, rival);
            float roll = Rand.Value;
            if (roll < 0.35f)
            {
                rs.stability = Mathf.Max(0f, rs.stability - 6f);
                EventBus.Raise(DominionEventDefOf.Dominion_Riots, rival, magnitude: 0.5f);
            }
            else if (roll < 0.7f)
            {
                EconomyEffects.Shock(rival, -0.04f, days: Operation_Sabotage.SabotageDays);
                rs.sabotagedUntilTick = Find.TickManager.TicksGame + Operation_Sabotage.SabotageDays * GenDate.TicksPerDay;
                WarUtility.AddScore(f, rival, 4f);
                EventBus.Raise(DominionEventDefOf.Dominion_Sabotage, rival, f, secrecy: Secrecy.Secret);
            }
            else if (roll < 0.85f || !bitter)
            {
                // A false flag: blame the rival's other enemy.
                Faction scapegoat = comp.Cache.Simulated.FirstOrDefault(o => o != f && o != rival && comp.GetRelation(rival, o).opinion < 0f && !rival.HostileTo(o));
                if (scapegoat == null) return;
                comp.GetRelation(rival, scapegoat).grievance += 25f;
                EventBus.Raise(DominionEventDefOf.Dominion_AgentsExposed, rival, scapegoat);
                EventBus.Raise(DominionEventDefOf.Dominion_FalseFlag, rival, f, detail: scapegoat.Name, secrecy: Secrecy.BlackOps);
            }
            else if (roll < 0.95f && rs.stability < 25f && rs.legitimacy < 45f)
                EventBus.Raise(DominionEventDefOf.Dominion_Coup, rival);
            else if (VisitorUtility.LeaderIsFree(rival.leader))
            {
                EventBus.Raise(DominionEventDefOf.Dominion_Assassination, rival, f, secrecy: Secrecy.BlackOps);
                rival.leader.Kill(null);
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
            if (!propaganda && DominionMod.Settings.aiCovertOps && Rand.Chance(0.4f) && SabotagePlayer(f))
                return;
            if (propaganda && DominionMod.Settings.colonistNewsThoughts && !comp.Broadcast.CounterProgrammingActive)
            {
                foreach (Map map in Find.Maps)
                    if (map.IsPlayerHome)
                        foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                            p.needs?.mood?.thoughts?.memories?.TryGainMemory(DominionThoughtDefOf.Dominion_ForeignPropaganda, null, null);
            }
        }

        /// <summary>Foreign saboteurs break something in the colony. True if they found something to break.</summary>
        private static bool SabotagePlayer(Faction f)
        {
            var candidates = new List<Building>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    CompBreakdownable c = b.TryGetComp<CompBreakdownable>();
                    if (c != null && !c.BrokenDown) candidates.Add(b);
                }
            }
            if (candidates.Count == 0) return false;
            Building target = candidates.RandomElement();
            target.TryGetComp<CompBreakdownable>().DoBreakdown();
            Find.LetterStack.ReceiveLetter("Dominion_Ops_SabotageLabel".Translate(), "Dominion_Ops_SabotageText".Translate(target.LabelShort),
                LetterDefOf.NegativeEvent, target);
            EventBus.Raise(DominionEventDefOf.Dominion_Sabotage, Faction.OfPlayer, null);
            return true;
        }
    }
}
