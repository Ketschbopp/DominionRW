using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>The player's side of Dominion: influence, head of state, capital and action cooldowns. Saved.</summary>
    public class PlayerState : IExposable
    {
        public const float MaxInfluence = 100f;

        /// <summary>Political capital, earned by the head of state at the Leadership Desk and spent on diplomacy.</summary>
        public float influence = 20f;

        /// <summary>Statecraft has been switched on for existing colonists (once per game).</summary>
        public bool statecraftEnabled;

        /// <summary>Head of state picked at the desk (used when the setting says so).</summary>
        public Pawn chosenHeadOfState;

        public Settlement capital;

        /// <summary>Diplomats picked at the desk (used when the setting says so).</summary>
        public List<Pawn> chosenDiplomats = new List<Pawn>();

        /// <summary>Next tick an action may be used again, keyed "actionDefName:factionLoadId".</summary>
        private Dictionary<string, int> cooldowns = new Dictionary<string, int>();

        /// <summary>Last tick each AI faction made the player an offer, by faction load id.</summary>
        private Dictionary<int, int> lastOfferTick = new Dictionary<int, int>();

        /// <summary>What passed between the player and each faction, newest last (capped). Shown in the Diplomacy tab.</summary>
        public List<DiplomacyLogEntry> diplomacyLog = new List<DiplomacyLogEntry>();
        public const int MaxLog = 400;

        /// <summary>Factions pinned to the top of the Diplomacy list, by load id.</summary>
        public List<int> pinnedFactions = new List<int>();

        /// <summary>Federation dues set for each member (0 none, 1 light, 2 heavy) and silver paid so far, by load id.</summary>
        public Dictionary<int, int> federationDues = new Dictionary<int, int>();
        public Dictionary<int, int> duesPaid = new Dictionary<int, int>();

        /// <summary>Council members the player promised to back on their next motion, by load id.</summary>
        public List<int> councilPromises = new List<int>();

        public void Log(Faction f, string text, int outcome = 0, bool byThem = false)
        {
            if (f == null || text.NullOrEmpty()) return;
            diplomacyLog.Add(new DiplomacyLogEntry { tick = Find.TickManager.TicksGame, factionId = f.loadID, text = text, outcome = outcome, byThem = byThem });
            if (diplomacyLog.Count > MaxLog) diplomacyLog.RemoveRange(0, diplomacyLog.Count - MaxLog);
        }

        public bool IsPinned(Faction f) => f != null && pinnedFactions.Contains(f.loadID);

        public void TogglePin(Faction f)
        {
            if (!pinnedFactions.Remove(f.loadID)) pinnedFactions.Add(f.loadID);
        }

        public int DuesOf(Faction f) => f != null && federationDues.TryGetValue(f.loadID, out int d) ? d : 0;

        public void AddInfluence(float amount) => influence = System.Math.Max(0f, System.Math.Min(MaxInfluence, influence + amount));

        public int CooldownUntil(string action, int factionId) =>
            cooldowns.TryGetValue(action + ":" + factionId, out int t) ? t : -1;

        public void SetCooldown(string action, int factionId, int until) => cooldowns[action + ":" + factionId] = until;

        public int LastOfferTick(int factionId) => lastOfferTick.TryGetValue(factionId, out int t) ? t : -999999;

        public void SetLastOffer(int factionId, int tick) => lastOfferTick[factionId] = tick;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Never save references to things that are gone: they warn on every load.
                if (chosenHeadOfState != null && chosenHeadOfState.Discarded)
                    chosenHeadOfState = null;
                if (capital != null && capital.Destroyed) capital = null;
                chosenDiplomats?.RemoveAll(p => p == null || p.Discarded);
            }
            Scribe_Values.Look(ref influence, "influence", 20f);
            Scribe_Values.Look(ref statecraftEnabled, "statecraftEnabled");
            Scribe_References.Look(ref chosenHeadOfState, "chosenHeadOfState");
            Scribe_References.Look(ref capital, "capital");
            Scribe_Collections.Look(ref chosenDiplomats, "chosenDiplomats", LookMode.Reference);
            Scribe_Collections.Look(ref cooldowns, "cooldowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastOfferTick, "lastOfferTick", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref diplomacyLog, "diplomacyLog", LookMode.Deep);
            Scribe_Collections.Look(ref pinnedFactions, "pinnedFactions", LookMode.Value);
            Scribe_Collections.Look(ref federationDues, "federationDues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref duesPaid, "duesPaid", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref councilPromises, "councilPromises", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cooldowns ??= new Dictionary<string, int>();
                chosenDiplomats ??= new List<Pawn>();
                chosenDiplomats.RemoveAll(p => p == null);
                lastOfferTick ??= new Dictionary<int, int>();
                diplomacyLog ??= new List<DiplomacyLogEntry>();
                diplomacyLog.RemoveAll(e => e == null);
                pinnedFactions ??= new List<int>();
                federationDues ??= new Dictionary<int, int>();
                duesPaid ??= new Dictionary<int, int>();
                councilPromises ??= new List<int>();
            }
        }
    }

    /// <summary>One line of the diplomatic record. Saved.</summary>
    public class DiplomacyLogEntry : IExposable
    {
        public int tick;
        public int factionId;
        public string text;

        /// <summary>1 went well, -1 went badly, 0 neither.</summary>
        public int outcome;

        /// <summary>Something they did, rather than something the player did.</summary>
        public bool byThem;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref factionId, "factionId");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref outcome, "outcome");
            Scribe_Values.Look(ref byThem, "byThem");
        }
    }
}
