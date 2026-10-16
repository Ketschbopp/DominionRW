using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>What agents must do on the target's map in a physical operation.</summary>
    public enum PhysicalObjective
    {
        None,
        /// <summary>Kill the faction leader (or, if they are away, a senior official).</summary>
        KillLeader,
        /// <summary>Destroy a marked building.</summary>
        DestroyBuilding,
        /// <summary>Reach a marked building (archives, a workshop) and get out.</summary>
        ReachTarget,
        /// <summary>Reach a colonist they hold prisoner and get them out.</summary>
        RescueHostage,
    }

    /// <summary>An intelligence operation the player can run against a faction. Numbers are data; effects are in the worker.</summary>
    public class OperationDef : Def
    {
        /// <summary>Can be run off-map: agents leave, and the outcome is rolled from their skills.</summary>
        public bool allowAbstract = true;

        /// <summary>Can be run on the target's own map: agents travel there by caravan and do the job themselves.</summary>
        public bool allowPhysical;

        public PhysicalObjective objective = PhysicalObjective.None;

        /// <summary>Extra reward for doing it in person (0.5 = effects × 1.5).</summary>
        public float physicalBonus = 0.5f;

        /// <summary>Needs a third faction (false flag: the one to blame).</summary>
        public bool needsThirdParty;

        /// <summary>Your network in the target must be at least this strong.</summary>
        public float minNetwork;

        /// <summary>Only against factions at or below this stability (coups).</summary>
        public float maxTargetStability = 101f;

        /// <summary>Raised when the operation succeeds; secret unless the operation was exposed.</summary>
        public DominionEventDef successEvent;

        /// <summary>A hostile act: exposure hurts twice as much and may start a war.</summary>
        public bool hostileAct;

        public Type workerClass;
        public int durationDays = 4;
        public float baseSuccess = 0.5f;
        public float exposureOnSuccess = 0.05f;
        public float exposureOnFail = 0.35f;
        public SkillDef skill;
        public float influenceCost = 10f;
        public int silverCost;
        public int maxAgents = 3;

        [Unsaved] private OperationWorker workerInt;

        public OperationWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (OperationWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }
    }

    public abstract class OperationWorker
    {
        public OperationDef def;

        public virtual string CannotTargetReason(Faction f) => null;

        /// <summary>The effect scale: doing it in person pays better.</summary>
        protected static float Mult(Operation op) => op.physical ? 1f + op.def.physicalBonus : 1f;

        public abstract void Succeed(Operation op);
    }

    /// <summary>Agents abroad on an operation. They are away from the colony until it ends. Saved.</summary>
    public class Operation : IExposable
    {
        public int id = -1;
        public OperationDef def;
        public Faction target;
        public List<Pawn> agents = new List<Pawn>();
        public int startTick;
        public int endTick;

        /// <summary>The faction to blame (false flag).</summary>
        public Faction thirdParty;

        // Physical operations: played on the target settlement's map.
        public bool physical;
        public Settlement settlement;

        /// <summary>The settlement's tile: its map outlives the settlement if the player wipes the base out.</summary>
        public int tile = -1;
        public Thing objective;
        public Pawn hostage;
        public bool targetIsLeader;
        public bool alerted;
        public bool achieved;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (objective != null && objective.Destroyed && !(objective is Pawn)) objective = null;
                if (settlement != null && settlement.Destroyed) settlement = null;
            }
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref target, "target");
            Scribe_Collections.Look(ref agents, "agents", LookMode.Reference);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick");
            Scribe_References.Look(ref thirdParty, "thirdParty");
            Scribe_Values.Look(ref physical, "physical");
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_References.Look(ref objective, "objective");
            if (Scribe.mode == LoadSaveMode.Saving && hostage != null && hostage.Discarded) hostage = null;
            Scribe_References.Look(ref hostage, "hostage");
            Scribe_Values.Look(ref targetIsLeader, "targetIsLeader");
            Scribe_Values.Look(ref alerted, "alerted");
            Scribe_Values.Look(ref achieved, "achieved");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                agents ??= new List<Pawn>();
                agents.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>The player's intelligence service: networks per faction, operations under way, counter-intelligence. Saved.</summary>
    public class IntelService : IExposable
    {
        public List<Operation> operations = new List<Operation>();

        /// <summary>Agents back from an operation who could not come home yet (no colony map). Retried hourly.</summary>
        public List<Pawn> returning = new List<Pawn>();
        private Dictionary<int, float> networks = new Dictionary<int, float>();
        public int counterIntelUntil = -1;
        private int nextId;

        public int NewId() => nextId++;

        public float Network(Faction f) => f != null && networks.TryGetValue(f.loadID, out float v) ? v : 0f;

        public void SetNetwork(Faction f, float v) => networks[f.loadID] = Mathf.Clamp(v, 0f, 100f);

        public IEnumerable<KeyValuePair<int, float>> Networks => networks;

        public bool CounterIntelActive => Find.TickManager.TicksGame < counterIntelUntil;

        public bool IsAway(Pawn p)
        {
            foreach (Operation op in operations)
                if (!op.physical && op.agents.Contains(p)) return true;
            return false;
        }

        public Operation ActivePhysical
        {
            get
            {
                foreach (Operation op in operations)
                    if (op.physical) return op;
                return null;
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref operations, "operations", LookMode.Deep);
            Scribe_Collections.Look(ref networks, "networks", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref counterIntelUntil, "counterIntelUntil", -1);
            Scribe_Values.Look(ref nextId, "nextId");
            Scribe_Collections.Look(ref returning, "returning", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                operations ??= new List<Operation>();
                networks ??= new Dictionary<int, float>();
                returning ??= new List<Pawn>();
                // An operation against a faction that no longer exists is over: its agents head home.
                foreach (Operation o in operations)
                    if (o != null && !o.physical && (o.def == null || o.target == null) && o.agents != null)
                        returning.AddRange(o.agents);
                operations.RemoveAll(o => o?.def == null || o.target == null);
                returning.RemoveAll(p => p == null || p.Dead);
            }
        }
    }

    // ------------------------------------------------------------------ workers

    public class Operation_EstablishNetwork : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            IntelService svc = WorldComponent_Dominion.Instance.Intel;
            svc.SetNetwork(op.target, svc.Network(op.target) + 25f * Mult(op));
        }
    }

    public class Operation_GatherIntel : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(op.target);
            s.playerIntel = Mathf.Min(100f, s.playerIntel + 20f * Mult(op) + comp.Intel.Network(op.target) * 0.1f);
            // Reports about them that had not reached you yet come in now.
            int now = Find.TickManager.TicksGame;
            foreach (NewsItem n in comp.News.Pending)
            {
                if (n.primary != op.target && n.secondary != op.target) continue;
                if (!n.truthful) continue; // agents on the ground see through rumours; they stay what they are
                n.source = NewsSource.IntelReport;
                n.confidence = NewsConfidence.Confirmed;
                n.arrivesTick = now;
                n.expiresTick = -1;
            }
        }
    }

    public class Operation_Propaganda : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.GetRelation(op.target, Faction.OfPlayer).favour += 10f;
            FactionState s = comp.GetState(op.target);
            s.blocs[(int)Bloc.Doves] += 0.05f;
            s.NormalizeBlocs();
        }
    }

    public class Operation_BribeOfficial : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.GetRelation(op.target, Faction.OfPlayer).favour += 15f;
            FactionState s = comp.GetState(op.target);
            s.playerIntel = Mathf.Min(100f, s.playerIntel + 10f);
        }
    }
}
