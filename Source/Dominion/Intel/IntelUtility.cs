using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>What the player knows. Intel is 0..100 per faction; secrecy levels need a minimum intel to be seen.</summary>
    public static class IntelUtility
    {
        public const float RestrictedIntel = 25f;
        public const float SecretIntel = 50f;
        public const float BlackOpsIntel = 80f;

        /// <summary>Dev toggle: treat the player as knowing everything. Not saved.</summary>
        public static bool RevealAll;

        public static float RequiredIntel(Secrecy s)
        {
            switch (s)
            {
                case Secrecy.Restricted: return RestrictedIntel;
                case Secrecy.Secret: return SecretIntel;
                case Secrecy.BlackOps: return BlackOpsIntel;
                default: return 0f;
            }
        }

        /// <summary>The player's intel on a faction (100 for the player's own faction).</summary>
        public static float IntelOn(Faction f)
        {
            if (f == null) return 0f;
            if (f.IsPlayer || RevealAll) return 100f;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            return comp != null && comp.TryGetState(f, out FactionState s) ? s.playerIntel : 0f;
        }

        /// <summary>The best intel the player has on any faction involved.</summary>
        public static float IntelOn(Faction a, Faction b) => System.Math.Max(IntelOn(a), IntelOn(b));

        public static bool Knows(Faction f, float level) => IntelOn(f) >= level;

        public static bool InvolvesPlayer(Faction a, Faction b) => (a != null && a.IsPlayer) || (b != null && b.IsPlayer);

        /// <summary>Shown in place of a value the player has not learned yet.</summary>
        public static string Unknown(float needed) => "Dominion_Intel_Unknown".Translate(needed.ToString("F0"));
    }
}
