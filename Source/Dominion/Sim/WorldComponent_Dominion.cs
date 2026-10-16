using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>Root of all saved Dominion state for a world, and the entry point for the simulation tick.</summary>
    public class WorldComponent_Dominion : WorldComponent
    {
        public static WorldComponent_Dominion Instance { get; private set; }

        private EventLog eventLog = new EventLog();

        [Unsaved]
        private readonly DominionScheduler scheduler = new DominionScheduler();

        public EventLog EventLog => eventLog;
        public DominionScheduler Scheduler => scheduler;

        public WorldComponent_Dominion(World world) : base(world)
        {
            Instance = this;
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            Instance = this;
            scheduler.Rebuild();
            IntegrationRegistry.NotifyWorldLoaded();
        }

        public override void WorldComponentTick()
        {
            scheduler.Tick(Find.TickManager.TicksGame);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref eventLog, "eventLog");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && eventLog == null)
                eventLog = new EventLog();
        }
    }
}
