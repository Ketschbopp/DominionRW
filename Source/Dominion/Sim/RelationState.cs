using Verse;

namespace Dominion
{
    /// <summary>
    /// How one faction sees another (directional: A's view of B can differ from B's view of A).
    /// Factions are stored by load id so the record survives without holding references. Saved.
    /// </summary>
    public class RelationState : IExposable
    {
        public int fromId;
        public int toId;

        /// <summary>-100..100. Dominion's view of the relationship; drives goodwill and AI choices.</summary>
        public float opinion;

        /// <summary>0..100. Accumulated wrongs, decays over time.</summary>
        public float grievance;

        /// <summary>Tick the current war started, or -1 if not at war. Hostility present at world start counts as dormant (-1).</summary>
        public int warSince = -1;

        /// <summary>True if <see cref="fromId"/> started the current war.</summary>
        public bool wasAggressor;

        /// <summary>No war may be declared on the other side before this tick.</summary>
        public int truceUntil = -1;

        /// <summary>Approximate distance in tiles between the nearest settlements, or -1 if unknown.</summary>
        public float distance = -1f;

        /// <summary>Number of the from-faction's former settlements the to-faction holds as FTV vassal outposts.</summary>
        public int outpostsHeldByOther;

        // Recomputed daily, not saved. Shown in the dossier tooltip.
        [Unsaved] public float targetOpinion;
        [Unsaved] public int sharedEnemies;
        [Unsaved] public float ideoCompatibility;

        public RelationState() { }

        public RelationState(int fromId, int toId)
        {
            this.fromId = fromId;
            this.toId = toId;
        }

        public bool AtWar => warSince >= 0;

        public static long Key(int fromId, int toId) => ((long)fromId << 32) | (uint)toId;

        public void ExposeData()
        {
            Scribe_Values.Look(ref fromId, "from");
            Scribe_Values.Look(ref toId, "to");
            Scribe_Values.Look(ref opinion, "opinion");
            Scribe_Values.Look(ref grievance, "grievance");
            Scribe_Values.Look(ref warSince, "warSince", -1);
            Scribe_Values.Look(ref wasAggressor, "wasAggressor");
            Scribe_Values.Look(ref truceUntil, "truceUntil", -1);
            Scribe_Values.Look(ref distance, "distance", -1f);
            Scribe_Values.Look(ref outpostsHeldByOther, "outpostsHeldByOther");
        }
    }
}
