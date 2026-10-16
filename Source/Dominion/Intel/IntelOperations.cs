using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>An intelligence operation the player can run against a faction. Numbers are data; effects are in the worker.</summary>
    public class OperationDef : Def
    {
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

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref target, "target");
            Scribe_Collections.Look(ref agents, "agents", LookMode.Reference);
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref endTick, "endTick");
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
                if (op.agents.Contains(p)) return true;
            return false;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref operations, "operations", LookMode.Deep);
            Scribe_Collections.Look(ref networks, "networks", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref counterIntelUntil, "counterIntelUntil", -1);
            Scribe_Values.Look(ref nextId, "nextId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                operations ??= new List<Operation>();
                networks ??= new Dictionary<int, float>();
                operations.RemoveAll(o => o?.def == null || o.target == null);
            }
        }
    }

    // ------------------------------------------------------------------ workers

    public class Operation_EstablishNetwork : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            IntelService svc = WorldComponent_Dominion.Instance.Intel;
            svc.SetNetwork(op.target, svc.Network(op.target) + 25f);
        }
    }

    public class Operation_GatherIntel : OperationWorker
    {
        public override void Succeed(Operation op)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(op.target);
            s.playerIntel = Mathf.Min(100f, s.playerIntel + 20f + comp.Intel.Network(op.target) * 0.1f);
            // Reports about them that had not reached you yet come in now.
            int now = Find.TickManager.TicksGame;
            foreach (NewsItem n in comp.News.Pending)
            {
                if (n.primary != op.target && n.secondary != op.target) continue;
                n.source = NewsSource.IntelReport;
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
