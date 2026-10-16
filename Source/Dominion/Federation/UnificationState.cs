using System.Collections.Generic;
using Verse;

namespace Dominion
{
    /// <summary>How a major faction became part of the player's Federation.</summary>
    public enum UnifyPath { None, Federation, Subjugated, Conquered }

    /// <summary>Saved progress toward the unification ending.</summary>
    public class UnificationState : IExposable
    {
        /// <summary>When the Federation Charter came into force; -1 if it has not.</summary>
        public int charterTick = -1;

        /// <summary>Major factions that no longer exist, and how they were absorbed (None = fell to someone else).</summary>
        public Dictionary<int, UnifyPath> fallen = new Dictionary<int, UnifyPath>();

        /// <summary>Names of fallen factions, for the tracker.</summary>
        public Dictionary<int, string> fallenNames = new Dictionary<int, string>();

        /// <summary>Major factions seen alive yesterday, with how they stood with the player.</summary>
        public Dictionary<int, UnifyPath> lastStatus = new Dictionary<int, UnifyPath>();
        public HashSet<int> lastHostile = new HashSet<int>();

        /// <summary>The player was told the Federation can be proclaimed.</summary>
        public bool offered;

        /// <summary>The ending has been shown.</summary>
        public bool proclaimed;
        public int proclaimedTick = -1;

        public bool CharterInForce => charterTick >= 0;

        public void ExposeData()
        {
            Scribe_Values.Look(ref charterTick, "charterTick", -1);
            Scribe_Collections.Look(ref fallen, "fallen", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref fallenNames, "fallenNames", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastStatus, "lastStatus", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastHostile, "lastHostile", LookMode.Value);
            Scribe_Values.Look(ref offered, "offered");
            Scribe_Values.Look(ref proclaimed, "proclaimed");
            Scribe_Values.Look(ref proclaimedTick, "proclaimedTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                fallen ??= new Dictionary<int, UnifyPath>();
                fallenNames ??= new Dictionary<int, string>();
                lastStatus ??= new Dictionary<int, UnifyPath>();
                lastHostile ??= new HashSet<int>();
            }
        }
    }
}
