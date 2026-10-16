using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>A signed treaty between two factions. Saved.</summary>
    public class Treaty : IExposable
    {
        public int id = -1;
        public TreatyDef def;
        public Faction a;
        public Faction b;
        public int signedTick;

        /// <summary>-1 if it does not expire.</summary>
        public int expiresTick = -1;

        public bool Involves(Faction f) => f != null && (a == f || b == f);

        public bool Between(Faction x, Faction y) => (a == x && b == y) || (a == y && b == x);

        public Faction Other(Faction f) => a == f ? b : a;

        public string Label => def.LabelCap;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Defs.Look(ref def, "def");
            Scribe_References.Look(ref a, "a");
            Scribe_References.Look(ref b, "b");
            Scribe_Values.Look(ref signedTick, "signedTick");
            Scribe_Values.Look(ref expiresTick, "expiresTick", -1);
        }
    }
}
