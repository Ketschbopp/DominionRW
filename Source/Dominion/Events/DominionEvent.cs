using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>One thing that happened in the world. Plain data, saved in the event log.</summary>
    public class DominionEvent : IExposable
    {
        public int id = -1;
        public DominionEventDef def;
        public int tick;

        /// <summary>The faction that acted, or that the event happened to.</summary>
        public Faction primary;

        /// <summary>The other side, if any.</summary>
        public Faction secondary;

        /// <summary>World tile where it happened, or -1.</summary>
        public int tile = -1;

        /// <summary>Scale of the event, default 1. Effects multiply by this.</summary>
        public float magnitude = 1f;

        /// <summary>Package id of the integration that raised it, or null for Dominion and vanilla.</summary>
        public string source;

        /// <summary>Extra text for headlines, such as the treaty involved. Available to news grammar as DETAIL.</summary>
        public string detail;

        /// <summary>Secrecy for this instance; starts as the def's value and can be raised or lowered.</summary>
        public Secrecy secrecy;

        public DominionEvent() { }

        public DominionEvent(DominionEventDef def, Faction primary = null, Faction secondary = null)
        {
            this.def = def;
            this.primary = primary;
            this.secondary = secondary;
            secrecy = def.secrecy;
        }

        public string Headline => def?.Worker.GetHeadline(this) ?? "?";

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref tick, "tick");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Factions removed from the game (quests) would leave unresolvable references.
                if (!SaveUtil.StillExists(primary)) primary = null;
                if (!SaveUtil.StillExists(secondary)) secondary = null;
            }
            Scribe_References.Look(ref primary, "primary");
            Scribe_References.Look(ref secondary, "secondary");
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref magnitude, "magnitude", 1f);
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref secrecy, "secrecy");
            Scribe_Values.Look(ref detail, "detail");
        }

        public override string ToString() => $"#{id} {def?.defName} [{primary?.Name} / {secondary?.Name}] t={tick}";
    }
}
