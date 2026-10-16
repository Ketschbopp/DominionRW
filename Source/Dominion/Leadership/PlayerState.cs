using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>The player's side of Dominion: influence, head of state, capital and action cooldowns. Saved.</summary>
    public class PlayerState : IExposable
    {
        public const float MaxInfluence = 100f;

        /// <summary>Political capital, earned by the head of state at the Leadership Desk and spent on diplomacy.</summary>
        public float influence = 20f;

        /// <summary>Statecraft has been switched on for existing colonists (once per game).</summary>
        public bool statecraftEnabled;

        /// <summary>Head of state picked at the desk (used when the setting says so).</summary>
        public Pawn chosenHeadOfState;

        public Settlement capital;

        /// <summary>Diplomats picked at the desk (used when the setting says so).</summary>
        public List<Pawn> chosenDiplomats = new List<Pawn>();

        /// <summary>Next tick an action may be used again, keyed "actionDefName:factionLoadId".</summary>
        private Dictionary<string, int> cooldowns = new Dictionary<string, int>();

        /// <summary>Last tick each AI faction made the player an offer, by faction load id.</summary>
        private Dictionary<int, int> lastOfferTick = new Dictionary<int, int>();

        public void AddInfluence(float amount) => influence = System.Math.Max(0f, System.Math.Min(MaxInfluence, influence + amount));

        public int CooldownUntil(string action, int factionId) =>
            cooldowns.TryGetValue(action + ":" + factionId, out int t) ? t : -1;

        public void SetCooldown(string action, int factionId, int until) => cooldowns[action + ":" + factionId] = until;

        public int LastOfferTick(int factionId) => lastOfferTick.TryGetValue(factionId, out int t) ? t : -999999;

        public void SetLastOffer(int factionId, int tick) => lastOfferTick[factionId] = tick;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Never save references to things that are gone: they warn on every load.
                if (chosenHeadOfState != null && chosenHeadOfState.Discarded)
                    chosenHeadOfState = null;
                if (capital != null && capital.Destroyed) capital = null;
                chosenDiplomats?.RemoveAll(p => p == null || p.Discarded);
            }
            Scribe_Values.Look(ref influence, "influence", 20f);
            Scribe_Values.Look(ref statecraftEnabled, "statecraftEnabled");
            Scribe_References.Look(ref chosenHeadOfState, "chosenHeadOfState");
            Scribe_References.Look(ref capital, "capital");
            Scribe_Collections.Look(ref chosenDiplomats, "chosenDiplomats", LookMode.Reference);
            Scribe_Collections.Look(ref cooldowns, "cooldowns", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastOfferTick, "lastOfferTick", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cooldowns ??= new Dictionary<string, int>();
                chosenDiplomats ??= new List<Pawn>();
                chosenDiplomats.RemoveAll(p => p == null);
                lastOfferTick ??= new Dictionary<int, int>();
            }
        }
    }
}
