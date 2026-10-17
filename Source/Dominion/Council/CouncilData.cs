using System.Linq;
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

        /// <summary>What happens if it carries, and if it fails; {0} and {1} are the targets.</summary>
        [MustTranslate] public string ifPassed;
        [MustTranslate] public string ifFailed;

        /// <summary>The pack it belongs to, for grouping in the propose menu (security, territory, economy, society).</summary>
        public string pack;

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

        /// <summary>God mode: 1 forces it to carry, -1 to fail, 0 lets the vote decide.</summary>
        public int devForce;

        public string Summary => def.Summarize(targets);

        public float LobbyFor(Faction f) => lobbying.TryGetValue(f.loadID, out float v) ? v : 0f;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref sponsor, "sponsor");
            Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
            Scribe_Collections.Look(ref lobbying, "lobbying", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref vetoed, "vetoed");
            Scribe_Values.Look(ref devForce, "devForce");
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

        /// <summary>Members the decree itself made act (e.g. embargoes imposed for sanctions), undone when it expires.</summary>
        public List<Faction> imposed = new List<Faction>();

        /// <summary>The compliance ledger: every breach and the penalty it drew, oldest first.</summary>
        public List<LedgerEntry> ledger = new List<LedgerEntry>();

        /// <summary>Set for decrees the council imposed as a penalty (sanctions or expulsion for repeated breaches).</summary>
        public bool penalty;

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
            Scribe_Collections.Look(ref imposed, "imposed", LookMode.Reference);
            Scribe_Collections.Look(ref ledger, "ledger", LookMode.Deep);
            Scribe_Values.Look(ref penalty, "penalty");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ledger ??= new List<LedgerEntry>();
                ledger.RemoveAll(e => e?.faction == null);
                // Saves from before this list existed: the members who complied are the best guess.
                imposed ??= complied.Where(m => m != null && !m.IsPlayer).ToList();
                imposed.RemoveAll(t => t == null);
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

        /// <summary>The member a decree named to host the next session, if any. Cleared once it hosts.</summary>
        public Faction nextHost;

        /// <summary>Past sessions, newest last, at most <see cref="MaxRecords"/>.</summary>
        public List<SessionRecord> records = new List<SessionRecord>();
        public const int MaxRecords = 30;

        /// <summary>Favours between the player and other members.</summary>
        public List<Favour> favours = new List<Favour>();

                /// <summary>The council is gone for good (a global nuclear war ended it). Only god mode can restore it.</summary>
        public bool disbanded;
        public int disbandedTick = -1;
        public string disbandedReason;

        public void ExposeData()
        {
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref nextSessionTick, "nextSessionTick", -1);
            Scribe_Values.Look(ref sessionNumber, "sessionNumber");
            Scribe_References.Look(ref host, "host");
            Scribe_Collections.Look(ref agenda, "agenda", LookMode.Deep);
            Scribe_Collections.Look(ref attending, "attending", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.Saving && playerDelegate != null && playerDelegate.Discarded) playerDelegate = null;
            Scribe_References.Look(ref playerDelegate, "playerDelegate");
            Scribe_Collections.Look(ref decrees, "decrees", LookMode.Deep);
            Scribe_Values.Look(ref nextDecreeId, "nextDecreeId");
            Scribe_Collections.Look(ref pastViolations, "pastViolations", LookMode.Value, LookMode.Value);
            Scribe_References.Look(ref nextHost, "nextHost");
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            Scribe_Collections.Look(ref favours, "favours", LookMode.Deep);
            Scribe_Values.Look(ref disbanded, "disbanded");
            Scribe_Values.Look(ref disbandedTick, "disbandedTick", -1);
            Scribe_Values.Look(ref disbandedReason, "disbandedReason");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                agenda ??= new List<Motion>();
                attending ??= new List<Faction>();
                decrees ??= new List<Decree>();
                pastViolations ??= new Dictionary<int, int>();
                records ??= new List<SessionRecord>();
                favours ??= new List<Favour>();
                records.RemoveAll(r => r == null);
                favours.RemoveAll(f => f?.faction == null);
                // A motion or decree whose target faction is gone (mod removed, faction deleted) cannot be voted on or enforced.
                agenda.RemoveAll(m => m?.def == null || m.targets.Count != m.def.targetCount);
                attending.RemoveAll(f => f == null);
                decrees.RemoveAll(d => d?.def == null || d.targets.Count != d.def.targetCount);
            }
        }
    }
}

namespace Dominion
{
    /// <summary>How hard the council comes down on a breach, by how many the member has on its record.</summary>
    public enum Penalty { Warning, Fine, Sanctions, Expulsion }

    /// <summary>A line in a decree's compliance ledger. Saved.</summary>
    public class LedgerEntry : IExposable
    {
        public Faction faction;
        public int tick;
        public Penalty penalty;

        /// <summary>Silver (or its worth in goods) taken as a fine.</summary>
        public int fine;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref penalty, "penalty");
            Scribe_Values.Look(ref fine, "fine");
        }
    }

    /// <summary>One motion as the session voted on it. Saved.</summary>
    public class MotionRecord : IExposable
    {
        public CouncilMotionDef def;
        public string summary;
        public Faction sponsor;
        public bool passed;
        public bool vetoed;
        public float yes, no;
        public List<Faction> ayes = new List<Faction>();
        public List<Faction> nays = new List<Faction>();
        public List<Faction> abstained = new List<Faction>();

        /// <summary>The decree it became (-1 if none), and, once it has run its course, how it turned out.</summary>
        public int decreeId = -1;
        public string outcome;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref summary, "summary");
            Scribe_References.Look(ref sponsor, "sponsor");
            Scribe_Values.Look(ref passed, "passed");
            Scribe_Values.Look(ref vetoed, "vetoed");
            Scribe_Values.Look(ref yes, "yes");
            Scribe_Values.Look(ref no, "no");
            Scribe_Collections.Look(ref ayes, "ayes", LookMode.Reference);
            Scribe_Collections.Look(ref nays, "nays", LookMode.Reference);
            Scribe_Collections.Look(ref abstained, "abstained", LookMode.Reference);
            Scribe_Values.Look(ref decreeId, "decreeId", -1);
            Scribe_Values.Look(ref outcome, "outcome");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ayes ??= new List<Faction>();
                nays ??= new List<Faction>();
                abstained ??= new List<Faction>();
                ayes.RemoveAll(f => f == null);
                nays.RemoveAll(f => f == null);
                abstained.RemoveAll(f => f == null);
            }
        }
    }

    /// <summary>A past session: who hosted, who came, and every motion with every member's vote. Saved.</summary>
    public class SessionRecord : IExposable
    {
        public int number;
        public int tick;
        public Faction host;
        public List<Faction> attended = new List<Faction>();
        public List<MotionRecord> motions = new List<MotionRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref number, "number");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_References.Look(ref host, "host");
            Scribe_Collections.Look(ref attended, "attended", LookMode.Reference);
            Scribe_Collections.Look(ref motions, "motions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                attended ??= new List<Faction>();
                motions ??= new List<MotionRecord>();
                attended.RemoveAll(f => f == null);
                motions.RemoveAll(m => m?.def == null);
            }
        }
    }

    /// <summary>
    /// A favour between the player and another member: one they owe you (you backed their motion and it carried) or
    /// one you owe them (they voted your way because you asked). Saved.
    /// </summary>
    public class Favour : IExposable
    {
        public Faction faction;

        /// <summary>True if the member owes the player; false if the player owes the member.</summary>
        public bool owedToPlayer;
        public int tick;
        public string reason;

        public const int LastsDays = 120;

        public int ExpiresTick => tick + LastsDays * GenDate.TicksPerDay;

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref owedToPlayer, "owedToPlayer");
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref reason, "reason");
        }
    }
}
