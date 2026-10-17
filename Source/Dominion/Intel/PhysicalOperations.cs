using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Dominion
{
    public class WorldObjectCompProperties_CovertOps : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_CovertOps()
        {
            compClass = typeof(WorldObjectComp_CovertOps);
        }
    }

    /// <summary>
    /// On every settlement: a player caravan can travel there to run an operation in person. On arrival the settlement's
    /// map is opened and the agents walk in; the target is marked. Unless the faction is already at war with you, its
    /// guards stay calm until they notice what is going on.
    /// </summary>
    public class WorldObjectComp_CovertOps : WorldObjectComp
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (!(parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer || !caravan.IsPlayerControlled) yield break;
            if (!DominionResearchDefOf.Dominion_IntelligenceNetworks.IsFinished || !DominionScheduler.IsSimulated(s.Faction)) yield break;
            foreach (OperationDef def in DefDatabase<OperationDef>.AllDefsListForReading)
            {
                if (!def.allowPhysical) continue;
                OperationDef d = def;
                string label = "Dominion_Ops_PhysicalOption".Translate(d.label, s.Label);
                foreach (FloatMenuOption o in CaravanArrivalActionUtility.GetFloatMenuOptions(
                             () => PhysicalOps.CanStart(d, s, caravan),
                             () => new CaravanArrivalAction_CovertOp(s, d),
                             label, caravan, s.Tile, s, null))
                    yield return o;
            }
        }
    }

    public class CaravanArrivalAction_CovertOp : CaravanArrivalAction
    {
        private Settlement settlement;
        private OperationDef def;

        public CaravanArrivalAction_CovertOp() { }

        public CaravanArrivalAction_CovertOp(Settlement settlement, OperationDef def)
        {
            this.settlement = settlement;
            this.def = def;
        }

        public override string Label => "Dominion_Ops_PhysicalOption".Translate(def?.label ?? "?", settlement?.Label ?? "?");

        public override string ReportString => "Dominion_Ops_PhysicalReport".Translate(settlement?.Label ?? "?");

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            if (settlement == null || settlement.Destroyed || settlement.Tile != destinationTile) return false;
            return PhysicalOps.CanStart(def, settlement, caravan);
        }

        public override void Arrived(Caravan caravan)
        {
            FloatMenuAcceptanceReport ok = PhysicalOps.CanStart(def, settlement, caravan);
            if (!ok.Accepted)
            {
                Messages.Message(ok.FailReason ?? "Dominion_Mission_NoLongerPossible".Translate(settlement?.Faction?.Name ?? "?").ToString(),
                    caravan, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            PhysicalOps.Begin(def, settlement, caravan);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Defs.Look(ref def, "def");
        }
    }

    /// <summary>Physical operations: map, objective, alarm and the outcome when the agents leave.</summary>
    public static class PhysicalOps
    {
        private static IntelService Svc => WorldComponent_Dominion.Instance.Intel;

        public static FloatMenuAcceptanceReport CanStart(OperationDef def, Settlement s, Caravan caravan)
        {
            if (def == null || s == null || s.Destroyed || s.Faction == null || s.Faction.IsPlayer) return false;
            // A settlement you are visiting through Visit Settlements is fine: the caravan joins the visit.
            if (s.HasMap && s.Map.mapPawns.FreeColonistsSpawnedCount > 0 && VisitSettlements?.IsVisit(s.Map) != true)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Ops_AlreadyThere".Translate());
            return Validate(def, s, Agents(caravan), CaravanSilver(caravan) + DiplomacyHelpers.AvailableSilver());
        }

        /// <summary>Colonists already on a visited map (Visit Settlements) can run the operation there and then.</summary>
        public static FloatMenuAcceptanceReport CanStartOnVisit(OperationDef def, Settlement s, Map map)
        {
            if (def == null || !def.allowPhysical || s == null || s.Destroyed || s.Faction == null || map == null) return false;
            return Validate(def, s, VisitAgents(map), DiplomacyHelpers.AvailableSilver());
        }

        private static FloatMenuAcceptanceReport Validate(OperationDef def, Settlement s, List<Pawn> agents, int silver)
        {
            Faction f = s.Faction;
            if (Svc.ActivePhysical != null) return FloatMenuAcceptanceReport.WithFailReason("Dominion_Ops_PhysicalBusy".Translate());
            if (agents.Count == 0) return FloatMenuAcceptanceReport.WithFailReason("Dominion_Ops_NoAgents".Translate());
            PlayerState p = WorldComponent_Dominion.Instance.Player;
            if (p.influence < def.influenceCost)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Action_NeedsInfluence".Translate(def.influenceCost.ToString("F0")));
            if (def.silverCost > 0 && silver < def.silverCost)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Action_NeedsSilver".Translate(def.silverCost));
            if (Svc.Network(f) < def.minNetwork)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Ops_NeedsNetwork".Translate(def.minNetwork.ToString("F0")));
            string reason = def.Worker.CannotTargetReason(f);
            if (reason != null) return FloatMenuAcceptanceReport.WithFailReason(reason);
            return true;
        }

        private static Integration_VisitSettlements VisitSettlements => IntegrationRegistry.Get<Integration_VisitSettlements>();

        public static List<Pawn> VisitAgents(Map map) =>
            map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Dead && !p.InMentalState).ToList();

        private static List<Pawn> Agents(Caravan caravan) =>
            caravan.PawnsListForReading.Where(p => p.IsColonist && !p.Downed && !p.Dead).ToList();

        private static int CaravanSilver(Caravan caravan)
        {
            int n = 0;
            foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                if (t.def == ThingDefOf.Silver) n += t.stackCount;
            return n;
        }

        /// <summary>Pays from the caravan first, then from home.</summary>
        private static void PaySilver(Caravan caravan, int amount)
        {
            foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan).ToList())
            {
                if (amount <= 0) return;
                if (t.def != ThingDefOf.Silver) continue;
                int n = Mathf.Min(amount, t.stackCount);
                t.SplitOff(n).Destroy();
                amount -= n;
            }
            if (amount > 0) DiplomacyHelpers.TakeSilver(amount);
        }

        public static void Begin(OperationDef def, Settlement s, Caravan caravan)
        {
            List<Pawn> agents = Agents(caravan);
            Map map;
            try
            {
                // With Visit Settlements, a settlement being visited already has its map: the caravan joins the visit.
                map = GetOrGenerateMapUtility.GetOrGenerateMap(s.Tile, null, null);
            }
            catch (System.Exception e)
            {
                DLog.Warning("Could not open the map of " + s.Label + " for an operation: " + e);
                return;
            }
            if (def.silverCost > 0)
            {
                PaySilver(caravan, def.silverCost);
                EconomyEffects.Receive(s.Faction, def.silverCost);
            }
            CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop, false, null);
            Start(def, s, map, agents);
        }

        /// <summary>Visit Settlements: the colonists on a visited map run the operation where they stand.</summary>
        public static void BeginOnVisit(OperationDef def, Settlement s, Map map)
        {
            if (!CanStartOnVisit(def, s, map).Accepted) return;
            if (def.silverCost > 0) EconomyEffects.Transfer(Faction.OfPlayer, s.Faction, def.silverCost);
            Start(def, s, map, VisitAgents(map));
        }

        private static void Start(OperationDef def, Settlement s, Map map, List<Pawn> agents)
        {
            Faction f = s.Faction;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.Player.AddInfluence(-def.influenceCost);
            var op = new Operation
            {
                id = Svc.NewId(),
                def = def,
                target = f,
                agents = agents,
                startTick = Find.TickManager.TicksGame,
                endTick = int.MaxValue,
                physical = true,
                settlement = s,
                tile = s.Tile.tileId,
                alerted = f.HostileTo(Faction.OfPlayer),
            };
            Svc.operations.Add(op);
            MarkObjective(op, map);

            LookTargets look = op.objective != null ? new LookTargets(op.objective) : new LookTargets(op.agents);
            string text = ("Dominion_Ops_PhysicalBrief_" + def.objective).Translate(f.Name, s.Label, op.objective?.LabelShort ?? "?");
            text += "\n\n" + (op.alerted ? "Dominion_Ops_PhysicalHostile" : "Dominion_Ops_PhysicalStealth").Translate(f.Name);
            DominionLetters.Send("Dominion_Ops_PhysicalBriefLabel".Translate(def.LabelCap), text, LetterDefOf.NeutralEvent, look, f, important: true);
        }

        /// <summary>Picks (or brings in) what the agents are after.</summary>
        private static void MarkObjective(Operation op, Map map)
        {
            Faction f = op.target;
            Lord lord = map.lordManager.lords.FirstOrDefault(l => l.faction == f);
            IntVec3 heart = BaseHeart(map, f);
            switch (op.def.objective)
            {
                case PhysicalObjective.KillLeader:
                {
                    Pawn leader = f.leader;
                    if (VisitorUtility.LeaderIsFree(leader))
                    {
                        Find.WorldPawns.RemovePawn(leader);
                        GenSpawn.Spawn(leader, CellFinder.RandomClosewalkCellNear(heart, map, 5, null), map);
                        lord?.AddPawn(leader);
                        op.objective = leader;
                        op.targetIsLeader = true;
                    }
                    else
                    {
                        // The leader is away: a senior official will have to do.
                        op.objective = map.mapPawns.SpawnedPawnsInFaction(f).Where(p => p.RaceProps.Humanlike && !p.Downed)
                            .OrderByDescending(p => p.kindDef.combatPower).FirstOrDefault();
                    }
                    break;
                }
                case PhysicalObjective.DestroyBuilding:
                case PhysicalObjective.ReachTarget:
                    op.objective = PickBuilding(map, f, op.def.objective == PhysicalObjective.DestroyBuilding);
                    // A settlement with no buildings worth the name: the job cannot be done here.
                    if (op.objective == null && op.def.objective == PhysicalObjective.ReachTarget)
                        op.objective = map.mapPawns.SpawnedPawnsInFaction(f).FirstOrDefault(p => p.RaceProps.Humanlike);
                    break;
                case PhysicalObjective.RescueHostage:
                {
                    Pawn hostage = HostageUtility.HeldByThem(f).FirstOrDefault();
                    if (hostage == null) break;
                    try
                    {
                        f.kidnapped.RemoveKidnappedPawn(hostage);
                        if (Find.WorldPawns.Contains(hostage)) Find.WorldPawns.RemovePawn(hostage);
                        GenSpawn.Spawn(hostage, CellFinder.RandomClosewalkCellNear(heart, map, 5, null), map);
                        hostage.guest?.SetGuestStatus(f, GuestStatus.Prisoner);
                        op.hostage = hostage;
                        op.objective = hostage;
                    }
                    catch (System.Exception e)
                    {
                        DLog.Warning("Could not place the hostage for a rescue: " + e.Message);
                    }
                    break;
                }
            }
        }

        private static IntVec3 BaseHeart(Map map, Faction f)
        {
            List<Building> list = map.listerBuildings.allBuildingsNonColonist;
            int x = 0, z = 0, n = 0;
            foreach (Building b in list)
            {
                if (b.Faction != f) continue;
                x += b.Position.x;
                z += b.Position.z;
                n++;
            }
            IntVec3 c = n > 0 ? new IntVec3(x / n, 0, z / n) : map.Center;
            return CellFinder.TryFindRandomCellNear(c, map, 8, cell => cell.Standable(map) && !cell.Fogged(map), out IntVec3 found) ? found : c;
        }

        /// <summary>Power and storage first for sabotage; for theft, whatever is worth the most.</summary>
        private static Building PickBuilding(Map map, Faction f, bool sabotage)
        {
            Building best = null;
            float bestScore = 0f;
            foreach (Building b in map.listerBuildings.allBuildingsNonColonist)
            {
                if (b.Faction != f || b.Destroyed || !b.def.useHitPoints || b.def.building == null) continue;
                if (b.def.building.isNaturalRock || b.def.IsEdifice() && b.def.passability == Traversability.Impassable && b.def.fillPercent >= 1f && b.def.size == IntVec2.One)
                    continue; // walls
                float score = b.MarketValue;
                if (sabotage && (b.TryGetComp<CompPowerPlant>() != null || b.TryGetComp<CompPowerBattery>() != null)) score *= 3f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }

        /// <summary>Every scheduler slot: objective reached, alarm, and the end once no agent is left on the map.</summary>
        public static void Check(Operation op, bool hourly)
        {
            Map map = op.settlement?.Map;
            if (map == null && op.tile >= 0)
                map = Find.Maps.FirstOrDefault(m => m.Tile.tileId == op.tile);
            // The base was wiped out with the captive still alive: they are free.
            bool baseGone = op.settlement == null || op.settlement.Destroyed;
            if (baseGone && !op.achieved && op.def.objective == PhysicalObjective.RescueHostage && op.hostage != null && op.hostage.Spawned && !op.hostage.Dead)
            {
                op.hostage.guest?.SetGuestStatus(null, GuestStatus.Guest);
                Achieve(op);
            }
            List<Pawn> onMap = map == null ? new List<Pawn>() : op.agents.Where(p => p != null && !p.Dead && p.Spawned && p.Map == map).ToList();
            Faction f = op.target;

            if (map != null && onMap.Count > 0 && !op.achieved)
            {
                switch (op.def.objective)
                {
                    case PhysicalObjective.KillLeader:
                        if (op.objective is Pawn victim && victim.Dead) Achieve(op);
                        break;
                    case PhysicalObjective.DestroyBuilding:
                        if (op.objective != null && op.objective.Destroyed) Achieve(op);
                        break;
                    case PhysicalObjective.ReachTarget:
                        if (op.objective != null && op.objective.Spawned && onMap.Any(p => !p.Downed && Near(p, op.objective))) Achieve(op);
                        break;
                    case PhysicalObjective.RescueHostage:
                        if (op.hostage != null && op.hostage.Spawned && !op.hostage.Dead && onMap.Any(p => !p.Downed && Near(p, op.hostage)))
                        {
                            op.hostage.guest?.SetGuestStatus(null, GuestStatus.Guest);
                            Achieve(op);
                        }
                        break;
                }
            }

            if (!op.alerted && f.HostileTo(Faction.OfPlayer)) op.alerted = true; // shots were fired, or a war broke out
            if (hourly && !op.alerted && onMap.Count > 0) RollDetection(op, onMap.Count);

            if (onMap.Count == 0) Resolve(op);
        }

        private static bool Near(Pawn p, Thing t) => t.OccupiedRect().ExpandedBy(1).Contains(p.Position);

        private static void Achieve(Operation op)
        {
            op.achieved = true;
            Messages.Message(("Dominion_Ops_PhysicalDone_" + op.def.objective).Translate(), op.objective != null ? new LookTargets(op.objective) : null,
                MessageTypeDefOf.PositiveEvent, historical: true);
        }

        private static void RollDetection(Operation op, int agentsOnMap)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(op.target);
            float chance = 0.015f * agentsOnMap * (0.5f + s.personality.paranoia) * DominionMod.Settings.physicalDetection;
            if (DominionResearchDefOf.Dominion_IntelligenceAgency.IsFinished) chance *= 0.7f;
            if (op.achieved) chance *= 2f; // a body, a fire or an empty cell gets noticed
            if (!Rand.Chance(chance)) return;
            op.alerted = true;
            Faction f = op.target;
            DiplomacyUtility.SetRelationKind(f, Faction.OfPlayer, FactionRelationKind.Hostile, f, "Dominion_Reason_CovertDiscovered".Translate());
            DominionLetters.Send("Dominion_Ops_AlarmLabel".Translate(), "Dominion_Ops_AlarmText".Translate(f.Name, op.settlement?.Label ?? "?"),
                LetterDefOf.ThreatBig, op.agents.FirstOrDefault(p => p.Spawned), f, important: true);
        }

        private static void Resolve(Operation op)
        {
            Svc.operations.Remove(op);
            // A captive left behind goes back to their cell.
            Pawn hostage = op.hostage;
            if (hostage != null && !hostage.Dead && !op.achieved && op.target != null && !op.target.defeated)
            {
                try
                {
                    if (hostage.Spawned) hostage.DeSpawn();
                    if (Find.WorldPawns.Contains(hostage)) Find.WorldPawns.RemovePawn(hostage);
                    hostage.guest?.SetGuestStatus(null, GuestStatus.Guest);
                    op.target.kidnapped.Kidnap(hostage, null);
                }
                catch (System.Exception e)
                {
                    DLog.Warning("Could not return the hostage to " + op.target.Name + ": " + e.Message);
                }
            }
            if (op.target == null || op.target.defeated) return;
            bool exposed = op.alerted || (op.achieved && Rand.Chance(op.def.exposureOnSuccess));
            IntelOpsSystem.Conclude(op, op.achieved, exposed, null);
        }

        /// <summary>The agents' current objective, for the Intelligence tab.</summary>
        public static string Status(Operation op)
        {
            if (op.achieved) return "Dominion_Ops_PhysicalStatus_Done".Translate();
            return (op.alerted ? "Dominion_Ops_PhysicalStatus_Alarm" : "Dominion_Ops_PhysicalStatus_Stealth").Translate();
        }
    }
}
