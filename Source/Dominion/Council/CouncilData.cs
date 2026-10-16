using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>A kind of motion the Rim Council can vote on. Behaviour lives in the worker class.</summary>
    public class CouncilMotionDef : Def
    {
        public Type workerClass;

        /// <summary>How many factions the motion names (0, 1 or 2).</summary>
        public int targetCount;

        /// <summary>How long the resulting decree lasts. 0 = it acts once when passed.</summary>
        public int durationDays;

        /// <summary>The decree never expires.</summary>
        public bool permanent;

        /// <summary>Days members get to comply before they are in violation.</summary>
        public int graceDays = 5;

        /// <summary>Influence the player spends to put it on the agenda.</summary>
        public float proposeCost = 15f;

        /// <summary>Base chance per session that an AI member with a reason proposes it.</summary>
        public float aiProposeChance = 0.3f;

        /// <summary>Headline-style description; {0} and {1} are the targets.</summary>
        [MustTranslate] public string summary;

        [Unsaved] private CouncilMotionWorker workerInt;

        public CouncilMotionWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (CouncilMotionWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public string Summarize(List<Faction> targets)
        {
            string a = targets.Count > 0 ? targets[0]?.Name ?? "?" : "";
            string b = targets.Count > 1 ? targets[1]?.Name ?? "?" : "";
            return summary.NullOrEmpty() ? (string)LabelCap : string.Format(summary, a, b);
        }
    }

    /// <summary>A motion on the agenda of the coming session. Saved.</summary>
    public class Motion : IExposable
    {
        public CouncilMotionDef def;
        public Faction sponsor;
        public List<Faction> targets = new List<Faction>();

        /// <summary>Extra vote leaning per member from lobbying (positive = toward yes), by faction load id.</summary>
        public Dictionary<int, float> lobbying = new Dictionary<int, float>();

        /// <summary>Set by a dev-mode veto.</summary>
        public bool vetoed;

        public string Summary => def.Summarize(targets);

        public float LobbyFor(Faction f) => lobbying.TryGetValue(f.loadID, out float v) ? v : 0f;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref sponsor, "sponsor");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref lobbying, "lobbying", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref vetoed, "vetoed");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targets ??= new List<Faction>();
                targets.RemoveAll(t => t == null);
                lobbying ??= new Dictionary<int, float>();
            }
        }
    }

    /// <summary>A passed motion in force. Every council member is bound by it. Saved.</summary>
    public class Decree : IExposable
    {
        public int id = -1;
        public CouncilMotionDef def;
        public List<Faction> targets = new List<Faction>();
        public int passedTick;
        public int expiresTick = -1;

        /// <summary>Factions found in violation (each counted once per decree).</summary>
        public List<Faction> violators = new List<Faction>();

        /// <summary>Factions that complied after the decree passed (for decrees that need an action).</summary>
        public List<Faction> complied = new List<Faction>();

        public string Summary => def.Summarize(targets);

        public bool InGrace => Find.TickManager.TicksGame < passedTick + def.graceDays * GenDate.TicksPerDay;

        public bool Targets(Faction f) => targets.Contains(f);

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
            Scribe_Values.Look(ref passedTick, "passedTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick", -1);
            Scribe_Collections.Look(ref violators, "violators", LookMode.Reference);
            Scribe_Collections.Look(ref complied, "complied", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                targets ??= new List<Faction>();
                violators ??= new List<Faction>();
                complied ??= new List<Faction>();
                targets.RemoveAll(t => t == null);
                violators.RemoveAll(t => t == null);
                complied.RemoveAll(t => t == null);
            }
        }
    }

    public enum CouncilPhase { Idle, Announced }

    /// <summary>The council's saved state: next session, the agenda, who has arrived, decrees in force.</summary>
    public class CouncilState : IExposable
    {
        public CouncilPhase phase = CouncilPhase.Idle;
        public int nextSessionTick = -1;
        public int sessionNumber;
        public Faction host;
        public List<Motion> agenda = new List<Motion>();
        public List<Faction> attending = new List<Faction>();

        /// <summary>The player's envoy registered at an AI-hosted session (null if not registered).</summary>
        public Pawn playerDelegate;

        public List<Decree> decrees = new List<Decree>();
        private int nextDecreeId;

        public int NewDecreeId() => nextDecreeId++;

        public int ViolationsBy(Faction f)
        {
            int n = 0;
            foreach (Decree d in decrees)
                if (d.violators.Contains(f)) n++;
            return n + (pastViolations.TryGetValue(f?.loadID ?? -1, out int past) ? past : 0);
        }

        /// <summary>Violations of decrees that have since expired, by faction load id.</summary>
        public Dictionary<int, int> pastViolations = new Dictionary<int, int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref nextSessionTick, "nextSessionTick", -1);
            Scribe_Values.Look(ref sessionNumber, "sessionNumber");
            Scribe_References.Look(ref host, "host");
            Scribe_Collections.Look(ref agenda, "agenda", LookMode.Deep);
            Scribe_Collections.Look(ref attending, "attending", LookMode.Reference);
            Scribe_References.Look(ref playerDelegate, "playerDelegate");
            Scribe_Collections.Look(ref decrees, "decrees", LookMode.Deep);
            Scribe_Values.Look(ref nextDecreeId, "nextDecreeId");
            Scribe_Collections.Look(ref pastViolations, "pastViolations", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                agenda ??= new List<Motion>();
                attending ??= new List<Faction>();
                decrees ??= new List<Decree>();
                pastViolations ??= new Dictionary<int, int>();
                agenda.RemoveAll(m => m?.def == null);
                attending.RemoveAll(f => f == null);
                decrees.RemoveAll(d => d?.def == null);
            }
        }
    }
}
