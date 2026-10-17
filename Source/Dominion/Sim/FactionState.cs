using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    public enum GovernmentType { Tribal, Autocracy, Monarchy, Theocracy, Council, Republic, Junta, Corporate }

    /// <summary>Internal political groups. Their support shares always sum to 1.</summary>
    public enum Bloc { Hawks, Doves, Merchants, Zealots, Reformists }

    /// <summary>Everything Dominion tracks about one faction. Saved.</summary>
    public class FactionState : IExposable
    {
        public const int BlocCount = 5;

        public Faction faction;

        // Politics (0..100)
        public float stability = 60f;
        public float legitimacy = 60f;
        public float warWeariness;

        public GovernmentType government;
        public bool governmentInitialized;
        public float[] blocs = new float[BlocCount];

        public AIPersonality personality = new AIPersonality();

        // Economy and strength. economyIndex: 100 = world average wealth per head.
        public float economyIndex = 100f;

        /// <summary>A lasting economic shock (-0.6..0.4) from wars, disasters, sabotage or booms. Fades over about a month.</summary>
        public float economyShock;

        /// <summary>Short economic blows (sabotage, disasters) that wear off on a fixed day instead of fading slowly.</summary>
        public List<TimedShock> timedShocks = new List<TimedShock>();
        public float militaryStrength;
        public int population;
        public int totalLiving;
        public float wealth;

        // Territory (from FTV)
        public int territoryTiles;
        public int vassalOutposts;
        public int settlementCount;

        public Settlement capital;

        /// <summary>The capital's tile, saved separately so a capital destroyed just before a save is still noticed.</summary>
        public int capitalTile = -1;

        /// <summary>0..100. How much the player knows about this faction. Gates news and the dossier.</summary>
        public float playerIntel;

        /// <summary>0..100. The player's cultural influence on this faction (broadcasts, treaties). Raises its opinion of the player.</summary>
        public float playerCulture;

        // Timers, in ticks
        public int leaderSinceTick = -1;
        public int lastRiotTick = -999999;
        public int lastCoupTick = -999999;
        public int nextAiActionTick;
        public int lastIdeoReformTick = -999999;
        public int lowStabilityDays;
        public int lastWorldEventTick = -999999;
        public DominionEventDef lastWorldEvent;

        /// <summary>A famine, plague or disaster: until this tick others can send aid.</summary>
        public int crisisUntilTick = -1;
        public DominionEventDef crisis;

        /// <summary>Last tick one of this faction's warbands raided the player.</summary>
        public int lastRaidOnPlayerTick = -999999;

        /// <summary>Sabotage keeps the faction's military weakened until this tick.</summary>
        public int sabotagedUntilTick = -1;

        public bool InCrisis => crisis != null && crisisUntilTick > Find.TickManager.TicksGame;

        /// <summary>In crisis, or out of it for less than <see cref="AidGraceDays"/>: aid still helps with the recovery.</summary>
        public bool NeedsAid => crisis != null && crisisUntilTick + AidGraceDays * GenDate.TicksPerDay > Find.TickManager.TicksGame;

        public const int AidGraceDays = 10;

        /// <summary>Tracks leader changes so legitimacy resets for a new leader.</summary>
        public Pawn lastKnownLeader;

        /// <summary>Daily snapshots of stability, economy and military strength, oldest first (a week), for trend arrows.</summary>
        public List<float> histStability = new List<float>();
        public List<float> histEconomy = new List<float>();
        public List<float> histMilitary = new List<float>();
        public const int HistoryDays = 7;

        public void RecordHistory()
        {
            Push(histStability, stability);
            Push(histEconomy, economyIndex);
            Push(histMilitary, militaryStrength);
        }

        private static void Push(List<float> list, float v)
        {
            list.Add(v);
            while (list.Count > HistoryDays) list.RemoveAt(0);
        }

        /// <summary>Change since the oldest snapshot (up to a week ago), or 0 with no history.</summary>
        public static float Trend(List<float> hist, float now) => hist == null || hist.Count == 0 ? 0f : now - hist[0];

        // Recomputed daily, not saved.
        [Unsaved] public int activeWars;
        [Unsaved] public float effectiveAggression;
        [Unsaved] public int coldWars;

        /// <summary>Today's economic climate (see EconomyEffects): scales E&amp;D production and the economy index.</summary>
        [Unsaved] public float climate;

        public FactionState() { }

        public FactionState(Faction faction)
        {
            this.faction = faction;
            personality.SeedFrom(faction);
        }

        public float Bloc(Bloc b) => blocs[(int)b];

        public void SetBlocs(float hawks, float doves, float merchants, float zealots, float reformists)
        {
            blocs[0] = hawks;
            blocs[1] = doves;
            blocs[2] = merchants;
            blocs[3] = zealots;
            blocs[4] = reformists;
            NormalizeBlocs();
        }

        public void NormalizeBlocs()
        {
            float sum = 0f;
            for (int i = 0; i < BlocCount; i++)
            {
                if (blocs[i] < 0.01f) blocs[i] = 0.01f;
                sum += blocs[i];
            }
            for (int i = 0; i < BlocCount; i++)
                blocs[i] /= sum;
        }

        public Bloc DominantBloc
        {
            get
            {
                int best = 0;
                for (int i = 1; i < BlocCount; i++)
                    if (blocs[i] > blocs[best]) best = i;
                return (Bloc)best;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref stability, "stability", 60f);
            Scribe_Values.Look(ref legitimacy, "legitimacy", 60f);
            Scribe_Values.Look(ref warWeariness, "warWeariness");
            Scribe_Values.Look(ref government, "government");
            Scribe_Values.Look(ref governmentInitialized, "governmentInitialized");

            List<float> blocList = Scribe.mode == LoadSaveMode.Saving ? new List<float>(blocs) : null;
            Scribe_Collections.Look(ref blocList, "blocs", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                blocs = new float[BlocCount];
                if (blocList != null)
                    for (int i = 0; i < BlocCount && i < blocList.Count; i++)
                        blocs[i] = blocList[i];
            }

            Scribe_Deep.Look(ref personality, "personality");
            Scribe_Values.Look(ref economyIndex, "economyIndex", 100f);
            Scribe_Values.Look(ref economyShock, "economyShock");
            Scribe_Collections.Look(ref timedShocks, "timedShocks", LookMode.Deep);
            Scribe_Values.Look(ref militaryStrength, "militaryStrength");
            Scribe_Values.Look(ref population, "population");
            Scribe_Values.Look(ref totalLiving, "totalLiving");
            Scribe_Values.Look(ref wealth, "wealth");
            Scribe_Values.Look(ref territoryTiles, "territoryTiles");
            Scribe_Values.Look(ref vassalOutposts, "vassalOutposts");
            Scribe_Values.Look(ref settlementCount, "settlementCount");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (capital != null && capital.Destroyed) capital = null;
                if (lastKnownLeader != null && lastKnownLeader.Discarded) lastKnownLeader = null;
            }
            Scribe_References.Look(ref capital, "capital");
            Scribe_Values.Look(ref capitalTile, "capitalTile", -1);
            Scribe_Values.Look(ref playerIntel, "playerIntel");
            Scribe_Values.Look(ref playerCulture, "playerCulture");
            Scribe_Values.Look(ref leaderSinceTick, "leaderSinceTick", -1);
            Scribe_Values.Look(ref lastRiotTick, "lastRiotTick", -999999);
            Scribe_Values.Look(ref lastCoupTick, "lastCoupTick", -999999);
            Scribe_Values.Look(ref nextAiActionTick, "nextAiActionTick");
            Scribe_Values.Look(ref lastIdeoReformTick, "lastIdeoReformTick", -999999);
            Scribe_Values.Look(ref lowStabilityDays, "lowStabilityDays");
            Scribe_Values.Look(ref lastWorldEventTick, "lastWorldEventTick", -999999);
            Scribe_Defs.Look(ref lastWorldEvent, "lastWorldEvent");
            Scribe_Values.Look(ref crisisUntilTick, "crisisUntilTick", -1);
            Scribe_Defs.Look(ref crisis, "crisis");
            Scribe_References.Look(ref lastKnownLeader, "lastKnownLeader");
            Scribe_Values.Look(ref lastRaidOnPlayerTick, "lastRaidOnPlayerTick", -999999);
            Scribe_Values.Look(ref sabotagedUntilTick, "sabotagedUntilTick", -1);
            Scribe_Collections.Look(ref histStability, "histStability", LookMode.Value);
            Scribe_Collections.Look(ref histEconomy, "histEconomy", LookMode.Value);
            Scribe_Collections.Look(ref histMilitary, "histMilitary", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (personality == null)
                {
                    personality = new AIPersonality();
                    if (faction != null) personality.SeedFrom(faction);
                }
                if (blocs == null || blocs.Length != BlocCount) blocs = new float[BlocCount];
                timedShocks ??= new List<TimedShock>();
                histStability ??= new List<float>();
                histEconomy ??= new List<float>();
                histMilitary ??= new List<float>();
                // The effective personality is not saved; rebuild it now so it is right before this faction's slot runs.
                if (faction != null)
                {
                    try { personality.Refresh(faction); }
                    catch (System.Exception e) { DLog.WarningOnce("Refreshing personality after load failed: " + e.Message, faction.loadID ^ 0x7E1); }
                }
            }
        }
    }

    /// <summary>An economic blow that is strongest at first and gone after its duration. Saved.</summary>
    public class TimedShock : IExposable
    {
        public float amount;
        public int startTick;
        public int untilTick;

        /// <summary>What is left of it now: the full amount at the start, nothing at the end.</summary>
        public float Current(int now)
        {
            if (now >= untilTick || untilTick <= startTick) return 0f;
            return amount * (untilTick - now) / (float)(untilTick - startTick);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref untilTick, "untilTick");
        }
    }
}
