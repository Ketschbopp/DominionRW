using RimWorld;
using Verse;

namespace Dominion
{
    public enum NewsConfidence { Rumor, Confirmed }

    /// <summary>How the news reached the colony.</summary>
    public enum NewsSource { TravelerGossip, RadioIntercept, DiplomaticDispatch, IntelReport, Leak, Correction }

    /// <summary>
    /// One report as the player receives it. It may be late, uncertain, or (for rumors) false.
    /// Text is resolved once at delivery and saved, so it never changes afterwards.
    /// </summary>
    public class NewsItem : IExposable
    {
        public int id = -1;
        public int eventId = -1;
        public DominionEventDef def;
        public int occurredTick;

        /// <summary>When it reaches the player. -1 while the event is still hidden from the player.</summary>
        public int arrivesTick = -1;

        /// <summary>Hidden items are dropped after this tick if they never surfaced.</summary>
        public int expiresTick = -1;

        public Faction primary;
        public Faction secondary;
        public int tile = -1;
        public Secrecy secrecy;

        public NewsConfidence confidence = NewsConfidence.Rumor;
        public NewsSource source;

        /// <summary>False for disinformation and baseless rumors. The player is not told.</summary>
        public bool truthful = true;

        /// <summary>For false rumors: a correction has been published.</summary>
        public bool corrected;

        /// <summary>For corrections: the item being corrected.</summary>
        public int correctsId = -1;

        /// <summary>The colony checked it over the radio and got a definite answer.</summary>
        public bool investigated;

        public bool delivered;
        public bool read;

        public string detail;

        public string headline;
        public string story;

        public bool Hidden => arrivesTick < 0;

        public EventCategory Category => def?.category ?? EventCategory.General;

        public static NewsItem FromEvent(DominionEvent ev) => new NewsItem
        {
            eventId = ev.id,
            def = ev.def,
            occurredTick = ev.tick,
            primary = ev.primary,
            secondary = ev.secondary,
            tile = ev.tile,
            secrecy = ev.secrecy,
            detail = ev.detail,
        };

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Values.Look(ref eventId, "eventId", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref occurredTick, "occurredTick");
            Scribe_Values.Look(ref arrivesTick, "arrivesTick", -1);
            Scribe_Values.Look(ref expiresTick, "expiresTick", -1);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Factions removed from the game (quests) would leave unresolvable references.
                if (!SaveUtil.StillExists(primary)) primary = null;
                if (!SaveUtil.StillExists(secondary)) secondary = null;
            }
            Scribe_References.Look(ref primary, "primary");
            Scribe_References.Look(ref secondary, "secondary");
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref secrecy, "secrecy");
            Scribe_Values.Look(ref confidence, "confidence");
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref truthful, "truthful", true);
            Scribe_Values.Look(ref corrected, "corrected");
            Scribe_Values.Look(ref correctsId, "correctsId", -1);
            Scribe_Values.Look(ref investigated, "investigated");
            Scribe_Values.Look(ref delivered, "delivered");
            Scribe_Values.Look(ref read, "read");
            Scribe_Values.Look(ref detail, "detail");
            Scribe_Values.Look(ref headline, "headline");
            Scribe_Values.Look(ref story, "story");
        }
    }
}
