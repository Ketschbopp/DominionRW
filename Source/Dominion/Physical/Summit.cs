using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    public enum SummitAgenda { Relations, Peace, Treaty, Hostages }

    public enum SummitVenue { PlayerCapital, TheirCapital }

    public enum SummitStatus { Arranged, LeaderPresent, InSession }

    /// <summary>A meeting between the player's head of state and another faction's leader. Saved.</summary>
    public class Summit : IExposable
    {
        public int id = -1;
        public Faction faction;
        public SummitAgenda agenda;
        public TreatyDef treaty;
        public SummitVenue venue;
        public SummitStatus status;
        public int expiresTick;

        /// <summary>For summits at the player's capital: the visiting leader (or envoy) on the map.</summary>
        public Pawn visitor;

        public string AgendaLabel => agenda == SummitAgenda.Treaty && treaty != null
            ? treaty.LabelCap.ToString()
            : ("Dominion_SummitAgenda_" + agenda).Translate().ToString();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref agenda, "agenda");
            Scribe_Defs.Look(ref treaty, "treaty");
            Scribe_Values.Look(ref venue, "venue");
            Scribe_Values.Look(ref status, "status");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_References.Look(ref visitor, "visitor");
        }
    }

    public class SummitRegistry : IExposable
    {
        private List<Summit> summits = new List<Summit>();
        private int nextId;

        public List<Summit> All => summits;

        public Summit Get(int id) => summits.Find(s => s.id == id);

        public Summit For(Faction f) => summits.Find(s => s.faction == f);

        public Summit Add(Summit s)
        {
            s.id = nextId++;
            summits.Add(s);
            return s;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextId, "nextId");
            Scribe_Collections.Look(ref summits, "summits", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                summits ??= new List<Summit>();
                summits.RemoveAll(s => s?.faction == null);
                // A dialog cannot survive a reload; resume waiting instead.
                foreach (Summit s in summits)
                    if (s.status == SummitStatus.InSession)
                        s.status = s.venue == SummitVenue.PlayerCapital ? SummitStatus.LeaderPresent : SummitStatus.Arranged;
            }
        }
    }
}
