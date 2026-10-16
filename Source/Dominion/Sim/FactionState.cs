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
        public float militaryStrength;
        public int population;
        public int totalLiving;
        public float wealth;

        // Territory (from FTV)
        public int territoryTiles;
        public int vassalOutposts;
        public int settlementCount;

        public Settlement capital;

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

        public bool InCrisis => crisis != null && crisisUntilTick > Find.TickManager.TicksGame;

        /// <summary>Tracks leader changes so legitimacy resets for a new leader.</summary>
        public Pawn lastKnownLeader;

        // Recomputed daily, not saved.
        [Unsaved] public int activeWars;
        [Unsaved] public float effectiveAggression;

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
            Scribe_Values.Look(ref militaryStrength, "militaryStrength");
            Scribe_Values.Look(ref population, "population");
            Scribe_Values.Look(ref totalLiving, "totalLiving");
            Scribe_Values.Look(ref wealth, "wealth");
            Scribe_Values.Look(ref territoryTiles, "territoryTiles");
            Scribe_Values.Look(ref vassalOutposts, "vassalOutposts");
            Scribe_Values.Look(ref settlementCount, "settlementCount");
            Scribe_References.Look(ref capital, "capital");
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (personality == null)
                {
                    personality = new AIPersonality();
                    if (faction != null) personality.SeedFrom(faction);
                }
                if (blocs == null || blocs.Length != BlocCount) blocs = new float[BlocCount];
            }
        }
    }
}
