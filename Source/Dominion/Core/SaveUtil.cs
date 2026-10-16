using RimWorld;
using Verse;

namespace Dominion
{
    public static class SaveUtil
    {
        /// <summary>False for a faction that was removed from the game (null counts as fine: nothing to save).</summary>
        public static bool StillExists(Faction f) =>
            f == null || Find.FactionManager.AllFactionsListForReading.Contains(f);
    }
}
