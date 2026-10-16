using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>Root of all saved Dominion state for a world, and the entry point for the simulation tick.</summary>
    public class WorldComponent_Dominion : WorldComponent
    {
        public static WorldComponent_Dominion Instance { get; private set; }

        private EventLog eventLog = new EventLog();
        private NewsLog newsLog = new NewsLog();
        private TreatyRegistry treaties = new TreatyRegistry();
        private PlayerState player = new PlayerState();
        private SummitRegistry summits = new SummitRegistry();
        private CouncilState council = new CouncilState();

        /// <summary>0..100. How well the player's radio receivers cover the airwaves; raised by monitoring, decays daily.</summary>
        public float radioCoverage;
        private Dictionary<int, FactionState> factionStates = new Dictionary<int, FactionState>();
        private Dictionary<long, RelationState> relations = new Dictionary<long, RelationState>();

        [Unsaved] private readonly DominionScheduler scheduler = new DominionScheduler();
        [Unsaved] private readonly WorldCache cache = new WorldCache();

        // Save-time buffers.
        private List<FactionState> statesList;
        private List<RelationState> relationsList;

        public EventLog EventLog => eventLog;
        public NewsLog News => newsLog;
        public TreatyRegistry Treaties => treaties;
        public PlayerState Player => player;
        public SummitRegistry Summits => summits;
        public CouncilState Council => council;
        public DominionScheduler Scheduler => scheduler;

        /// <summary>Lookups refreshed at most once a day. Always call through here.</summary>
        public WorldCache Cache
        {
            get
            {
                cache.EnsureFresh();
                return cache;
            }
        }

        public IEnumerable<FactionState> AllStates => factionStates.Values;

        public WorldComponent_Dominion(World world) : base(world)
        {
            Instance = this;
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            Instance = this;
            cache.Rebuild();
            scheduler.Rebuild();
            IntegrationRegistry.NotifyWorldLoaded();
        }

        public override void WorldComponentTick()
        {
            scheduler.Tick(Find.TickManager.TicksGame);
        }

        /// <summary>The state for a faction, created on first use. Null for null.</summary>
        public FactionState GetState(Faction f)
        {
            if (f == null) return null;
            if (!factionStates.TryGetValue(f.loadID, out FactionState s))
            {
                s = new FactionState(f);
                factionStates[f.loadID] = s;
            }
            return s;
        }

        public bool TryGetState(Faction f, out FactionState s)
        {
            s = null;
            return f != null && factionStates.TryGetValue(f.loadID, out s);
        }

        /// <summary>How <paramref name="from"/> sees <paramref name="to"/>, created on first use.</summary>
        public RelationState GetRelation(Faction from, Faction to)
        {
            long key = RelationState.Key(from.loadID, to.loadID);
            if (!relations.TryGetValue(key, out RelationState r))
            {
                r = new RelationState(from.loadID, to.loadID);
                relations[key] = r;
            }
            return r;
        }

        public RelationState TryGetRelation(Faction from, Faction to) =>
            from != null && to != null && relations.TryGetValue(RelationState.Key(from.loadID, to.loadID), out RelationState r) ? r : null;

        /// <summary>Removes state for factions that no longer exist (removed by another mod or the game).</summary>
        public void PruneMissingFactions()
        {
            WorldCache c = Cache;
            List<int> dead = null;
            foreach (KeyValuePair<int, FactionState> kv in factionStates)
                if (kv.Value.faction == null || c.FactionById(kv.Key) == null)
                    (dead ??= new List<int>()).Add(kv.Key);
            if (dead == null) return;
            foreach (int id in dead)
                factionStates.Remove(id);
            List<long> deadRel = new List<long>();
            foreach (KeyValuePair<long, RelationState> kv in relations)
                if (dead.Contains(kv.Value.fromId) || dead.Contains(kv.Value.toId))
                    deadRel.Add(kv.Key);
            foreach (long k in deadRel)
                relations.Remove(k);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref eventLog, "eventLog");
            Scribe_Deep.Look(ref newsLog, "newsLog");
            Scribe_Deep.Look(ref treaties, "treaties");
            Scribe_Deep.Look(ref player, "player");
            Scribe_Deep.Look(ref summits, "summits");
            Scribe_Deep.Look(ref council, "council");
            Scribe_Values.Look(ref radioCoverage, "radioCoverage");

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                statesList = new List<FactionState>(factionStates.Values);
                relationsList = new List<RelationState>(relations.Values);
            }
            Scribe_Collections.Look(ref statesList, "factionStates", LookMode.Deep);
            Scribe_Collections.Look(ref relationsList, "relations", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                eventLog ??= new EventLog();
                newsLog ??= new NewsLog();
                treaties ??= new TreatyRegistry();
                player ??= new PlayerState();
                summits ??= new SummitRegistry();
                council ??= new CouncilState();
                factionStates = new Dictionary<int, FactionState>();
                if (statesList != null)
                    foreach (FactionState s in statesList)
                        if (s?.faction != null)
                            factionStates[s.faction.loadID] = s;
                relations = new Dictionary<long, RelationState>();
                if (relationsList != null)
                    foreach (RelationState r in relationsList)
                        if (r != null)
                            relations[RelationState.Key(r.fromId, r.toId)] = r;
            }
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                statesList = null;
                relationsList = null;
            }
        }
    }
}
