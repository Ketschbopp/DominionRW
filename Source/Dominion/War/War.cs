using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>What the side that declared a war wants out of it. Decides the terms when the war ends in its favour.</summary>
    public enum WarGoal { Punitive, Conquest, Vassalisation, Tribute, RegimeChange, Liberation }

    /// <summary>How the player fights a war: set from the War tab.</summary>
    public enum WarStance { Balanced, Aggressive, Defensive, RaidSupply }

    /// <summary>One line in a war's chronicle: what happened and where the score stood after. Saved.</summary>
    public class WarChronicleEntry : IExposable
    {
        public int tick;
        public string text;
        public float score;

        /// <summary>The event behind it, to find the newspaper story; -1 for orders the player gave.</summary>
        public int eventId = -1;

        /// <summary>How secret the event was: other people's wars only show what your intel can see.</summary>
        public Secrecy secrecy = Secrecy.Public;

        public void ExposeData()
        {
            Scribe_Values.Look(ref secrecy, "secrecy", Secrecy.Public);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref score, "score");
            Scribe_Values.Look(ref eventId, "eventId", -1);
        }
    }

    /// <summary>
    /// One war between two factions, from the declaration to the peace. Score runs from -100 (the defender has won)
    /// to 100 (the attacker has won). A faction that joins someone else's war gets its own war record, marked with the
    /// ally it fights for; its successes count toward that ally's war too. Saved.
    /// </summary>
    public class War : IExposable
    {
        public int id = -1;
        public Faction attacker;
        public Faction defender;
        public WarGoal goal;

        /// <summary>For conquest: the settlement the attacker is after. Optional.</summary>
        public Settlement goalSettlement;

        public int startTick;

        /// <summary>-100..100, positive when the attacker is winning.</summary>
        public float score;

        public int battles;

        /// <summary>Set when this war was joined on behalf of another faction (joint war, defensive pact).</summary>
        public Faction alliedTo;

        /// <summary>Factions already asked to join, by load id, so nobody is asked twice.</summary>
        public List<int> asked = new List<int>();

        /// <summary>Last tick a capitulation was offered to or demanded of the player.</summary>
        public int lastOfferTick = -999999;

        /// <summary>People killed on each side (humanlike), counted from the day it was declared.</summary>
        public int deadAttacker;
        public int deadDefender;

        /// <summary>The player's stance in this war (only meaningful when the player fights in it).</summary>
        public WarStance stance = WarStance.Balanced;

        /// <summary>Last tick the player hired mercenaries for this war.</summary>
        public int lastMercenaryTick = -999999;

        /// <summary>What happened in this war, oldest first (capped).</summary>
        public List<WarChronicleEntry> chronicle = new List<WarChronicleEntry>();
        public const int MaxChronicle = 80;

        /// <summary>Terms drafted at the peace table, applied instead of the default terms when this peace is made.</summary>
        [Unsaved] public PeaceDeal deal;

        public void Chronicle(string text, int eventId = -1, Secrecy secrecy = Secrecy.Public)
        {
            chronicle.Add(new WarChronicleEntry { tick = Find.TickManager.TicksGame, text = text, score = score, eventId = eventId, secrecy = secrecy });
            if (chronicle.Count > MaxChronicle) chronicle.RemoveAt(0);
        }

        public int DeadOf(Faction f) => f == attacker ? deadAttacker : f == defender ? deadDefender : 0;

        /// <summary>Set just before a forced peace so the terms are applied as a capitulation.</summary>
        [Unsaved] public Faction capitulating;

        // Kill score is capped per day so one bloody raid cannot decide a war. Not saved.
        [Unsaved] public int killDay = -1;
        [Unsaved] public float killScoreToday;

        public bool Involves(Faction f) => f != null && (attacker == f || defender == f);

        public bool Between(Faction x, Faction y) => (attacker == x && defender == y) || (attacker == y && defender == x);

        public Faction Other(Faction f) => attacker == f ? defender : attacker;

        public bool InvolvesPlayer => (attacker?.IsPlayer ?? false) || (defender?.IsPlayer ?? false);

        /// <summary>The score from <paramref name="f"/>'s point of view: positive when it is winning.</summary>
        public float ScoreFor(Faction f) => f == attacker ? score : -score;

        public void AddScore(Faction winner, float amount)
        {
            if (winner == attacker) score += amount;
            else if (winner == defender) score -= amount;
            score = Mathf.Clamp(score, -100f, 100f);
        }

        /// <summary>The side ahead by a clear margin, or null while it is still close.</summary>
        public Faction Leader(float margin = PeaceTerms.DecisiveScore)
        {
            if (score >= margin) return attacker;
            if (score <= -margin) return defender;
            return null;
        }

        public int DurationTicks => Find.TickManager.TicksGame - startTick;

        public string Label => "Dominion_War_Label".Translate(attacker?.Name ?? "?", defender?.Name ?? "?");

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_References.Look(ref attacker, "attacker");
            Scribe_References.Look(ref defender, "defender");
            Scribe_Values.Look(ref goal, "goal");
            if (Scribe.mode == LoadSaveMode.Saving && goalSettlement != null && goalSettlement.Destroyed) goalSettlement = null;
            Scribe_References.Look(ref goalSettlement, "goalSettlement");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref score, "score");
            Scribe_Values.Look(ref battles, "battles");
            Scribe_References.Look(ref alliedTo, "alliedTo");
            Scribe_Collections.Look(ref asked, "asked", LookMode.Value);
            Scribe_Values.Look(ref lastOfferTick, "lastOfferTick", -999999);
            Scribe_Values.Look(ref deadAttacker, "deadAttacker");
            Scribe_Values.Look(ref deadDefender, "deadDefender");
            Scribe_Values.Look(ref stance, "stance");
            Scribe_Values.Look(ref lastMercenaryTick, "lastMercenaryTick", -999999);
            Scribe_Collections.Look(ref chronicle, "chronicle", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                asked ??= new List<int>();
                chronicle ??= new List<WarChronicleEntry>();
                chronicle.RemoveAll(c => c == null);
            }
        }
    }

    /// <summary>A player caravan sitting on an enemy settlement, cutting it off. Saved.</summary>
    public class Blockade : IExposable
    {
        public Caravan caravan;
        public Settlement settlement;
        public int startTick;

        public bool StillValid =>
            caravan != null && !caravan.Destroyed && caravan.PawnsListForReading.Count > 0 && settlement != null && !settlement.Destroyed
            && settlement.Faction != null && caravan.Tile == settlement.Tile && !caravan.pather.Moving
            && settlement.Faction.HostileTo(Faction.OfPlayer);

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (caravan != null && caravan.Destroyed) caravan = null;
                if (settlement != null && settlement.Destroyed) settlement = null;
            }
            Scribe_References.Look(ref caravan, "caravan");
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Values.Look(ref startTick, "startTick");
        }
    }

    /// <summary>Every war in progress, settlements held by an occupier, and the player's blockades. Saved.</summary>
    public class WarRegistry : IExposable
    {
        public List<War> wars = new List<War>();
        public List<Blockade> blockades = new List<Blockade>();

        /// <summary>Caravans chasing a delegation or warband.</summary>
        public List<Pursuit> pursuits = new List<Pursuit>();

        /// <summary>Battles with a player-intercepted warband, scored when they are decided.</summary>
        public List<WarbandFight> fights = new List<WarbandFight>();

        /// <summary>
        /// Occupied settlements: world tile → load id of the faction that held it before the first occupation. Keyed by tile,
        /// because other mods (FTV invasions) replace the settlement object when it changes hands.
        /// </summary>
        public Dictionary<int, int> originalOwner = new Dictionary<int, int>();

        /// <summary>Strength of the Dominion warband behind an FTV invasion, by tile, so its outcome uses it.</summary>
        public Dictionary<int, float> siegePoints = new Dictionary<int, float>();

        /// <summary>The goal the player chose for the war they are about to declare.</summary>
        public WarGoal pendingPlayerGoal = WarGoal.Punitive;
        public Settlement pendingPlayerTarget;

        private int nextId;

        public int NewId() => nextId++;

        public War Find(Faction x, Faction y)
        {
            for (int i = 0; i < wars.Count; i++)
                if (wars[i].Between(x, y)) return wars[i];
            return null;
        }

        public void WarsOf(Faction f, List<War> into)
        {
            into.Clear();
            for (int i = 0; i < wars.Count; i++)
                if (wars[i].Involves(f)) into.Add(wars[i]);
        }

        public bool AnyWar(Faction f)
        {
            for (int i = 0; i < wars.Count; i++)
                if (wars[i].Involves(f)) return true;
            return false;
        }

        public int OriginalOwnerOf(Settlement s) => s != null && DominionGeo.OnSurface(s.Tile) && originalOwner.TryGetValue(s.Tile.tileId, out int id) ? id : -1;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                blockades.RemoveAll(b => b == null || !b.StillValid);
                if (pendingPlayerTarget != null && pendingPlayerTarget.Destroyed) pendingPlayerTarget = null;
            }
            Scribe_Collections.Look(ref wars, "wars", LookMode.Deep);
            Scribe_Collections.Look(ref blockades, "blockades", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving) pursuits.RemoveAll(x => x == null || x.caravan == null || x.caravan.Destroyed || x.target == null || x.target.Destroyed);
            Scribe_Collections.Look(ref pursuits, "pursuits", LookMode.Deep);
            Scribe_Collections.Look(ref fights, "warbandFights", LookMode.Deep);
            Scribe_Collections.Look(ref originalOwner, "originalOwnerByTile", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref siegePoints, "siegePoints", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref pendingPlayerGoal, "pendingPlayerGoal");
            Scribe_References.Look(ref pendingPlayerTarget, "pendingPlayerTarget");
            Scribe_Values.Look(ref nextId, "nextId");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                wars ??= new List<War>();
                blockades ??= new List<Blockade>();
                pursuits ??= new List<Pursuit>();
                fights ??= new List<WarbandFight>();
                fights.RemoveAll(x => x == null || x.faction == null);
                pursuits.RemoveAll(x => x == null || x.caravan == null || x.target == null);
                originalOwner ??= new Dictionary<int, int>();
                siegePoints ??= new Dictionary<int, float>();
                wars.RemoveAll(w => w == null || w.attacker == null || w.defender == null);
                blockades.RemoveAll(b => b == null || b.caravan == null || b.settlement == null);
            }
        }
    }
}
