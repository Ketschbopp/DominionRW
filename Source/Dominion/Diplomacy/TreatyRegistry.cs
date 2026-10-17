using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// All treaties in force. Signing, cancelling and breaking go through here so every change raises the
    /// matching event (and therefore news, opinion changes and so on).
    /// </summary>
    public class TreatyRegistry : IExposable
    {
        private List<Treaty> treaties = new List<Treaty>();
        private int nextId;

        public IReadOnlyList<Treaty> All => treaties;

        public Treaty Find(Faction x, Faction y, TreatyDef def)
        {
            for (int i = 0; i < treaties.Count; i++)
                if (treaties[i].def == def && treaties[i].Between(x, y)) return treaties[i];
            return null;
        }

        public Treaty FindById(int id)
        {
            for (int i = 0; i < treaties.Count; i++)
                if (treaties[i].id == id) return treaties[i];
            return null;
        }

        public bool Has(Faction x, Faction y, TreatyDef def) => Find(x, y, def) != null;

        /// <summary>True if any treaty between the pair forbids them fighting.</summary>
        public bool AnyNonAggression(Faction x, Faction y)
        {
            for (int i = 0; i < treaties.Count; i++)
            {
                Treaty t = treaties[i];
                if ((t.def.nonAggression || t.def.alliance || t.def.defensive) && t.Between(x, y)) return true;
            }
            return false;
        }

        public void Between(Faction x, Faction y, List<Treaty> into)
        {
            into.Clear();
            for (int i = 0; i < treaties.Count; i++)
                if (treaties[i].Between(x, y)) into.Add(treaties[i]);
        }

        /// <summary>Why the pair cannot sign this treaty, or null if they can.</summary>
        public string CannotSignReason(TreatyDef def, Faction x, Faction y)
        {
            if (def.imposed) return "Dominion_Treaty_Imposed".Translate();
            if (Has(x, y, def)) return "Dominion_Treaty_AlreadySigned".Translate();
            if (x.HostileTo(y)) return "Dominion_Treaty_AtWar".Translate();
            if (def.federation)
            {
                string why = FederationUtility.CannotJoinReason(x.IsPlayer ? y : x, x.IsPlayer || y.IsPlayer);
                if (why != null) return why;
            }
            if (def.incompatibleWith != null)
                foreach (TreatyDef other in def.incompatibleWith)
                    if (Has(x, y, other)) return "Dominion_Treaty_Incompatible".Translate(other.label);
            return null;
        }

        /// <param name="durationDays">The agreed term; below 0 means the treaty's usual length.</param>
        public Treaty Sign(TreatyDef def, Faction x, Faction y, Faction proposer, int durationDays = -1)
        {
            Treaty existing = Find(x, y, def);
            if (existing != null) return existing;
            int now = Verse.Find.TickManager.TicksGame;
            int days = !def.Expires ? 0 : durationDays > 0 ? durationDays : def.durationDays;
            var t = new Treaty
            {
                id = nextId++,
                def = def,
                a = x,
                b = y,
                signedTick = now,
                expiresTick = def.Expires ? now + days * GenDate.TicksPerDay : -1,
                termDays = days,
            };
            treaties.Add(t);
            if (def.alliance)
            {
                // The vanilla alliance raises its own "alliance formed" event; no second report.
                if (x.RelationKindWith(y) != FactionRelationKind.Ally)
                    DiplomacyUtility.SetRelationKind(x, y, FactionRelationKind.Ally, proposer, def.label);
                return t;
            }
            if (def.federation)
                EventBus.Raise(DominionEventDefOf.Dominion_FederationJoined, x.IsPlayer ? y : x, Faction.OfPlayer);
            else
                EventBus.Raise(DominionEventDefOf.Dominion_TreatySigned, proposer, proposer == x ? y : x, detail: def.label, secrecy: def.secrecy);
            return t;
        }

        /// <summary>Renews or amends a treaty: it runs for <paramref name="days"/> from today.</summary>
        public void Renew(Treaty t, int days)
        {
            if (!t.def.Expires || days <= 0) return;
            t.expiresTick = Verse.Find.TickManager.TicksGame + days * GenDate.TicksPerDay;
            t.termDays = days;
            t.renewals++;
            t.reminded = false;
            EventBus.Raise(DominionEventDefOf.Dominion_TreatySigned, t.a.IsPlayer ? t.a : t.b, t.a.IsPlayer ? t.b : t.a, detail: t.def.label, secrecy: t.def.secrecy);
        }

        /// <summary>Ends a treaty by agreement or notice. Not a violation, but the other side is displeased.</summary>
        public void Cancel(Treaty t, Faction by)
        {
            // An imposed treaty can be ended by the side it favours (releasing a puppet, forgiving tribute);
            // walking out of it as the side that owes is a violation.
            if (t.def.imposed && by == t.a)
            {
                Break(t, by);
                return;
            }
            if (!treaties.Remove(t)) return;
            WorldComponent_Dominion.Instance.GetRelation(t.Other(by), by).grievance += 5f;
            if (t.def.alliance)
            {
                // Ending the vanilla alliance raises "alliance broken"; no second report.
                if (t.a.RelationKindWith(t.b) == FactionRelationKind.Ally)
                    DiplomacyUtility.SetRelationKind(t.a, t.b, FactionRelationKind.Neutral, by, t.def.label);
                return;
            }
            if (t.def.federation)
                EventBus.Raise(DominionEventDefOf.Dominion_FederationLeft, t.a.IsPlayer ? t.b : t.a, Faction.OfPlayer);
            else
                EventBus.Raise(DominionEventDefOf.Dominion_TreatyCancelled, by, t.Other(by), detail: t.def.label, secrecy: t.def.secrecy);
        }

        /// <summary>A treaty violated by <paramref name="breaker"/>. Everyone thinks less of them.</summary>
        public void Break(Treaty t, Faction breaker)
        {
            if (!treaties.Remove(t)) return;
            EventBus.Raise(DominionEventDefOf.Dominion_TreatyBroken, breaker, t.Other(breaker), detail: t.def.label,
                secrecy: t.def.secrecy == Secrecy.Public ? Secrecy.Public : Secrecy.Restricted);
        }

        /// <summary>Records a treaty without raising an event, for agreements that already announced themselves.</summary>
        public Treaty Record(TreatyDef def, Faction x, Faction y)
        {
            Treaty existing = Find(x, y, def);
            if (existing != null) return existing;
            var t = new Treaty { id = nextId++, def = def, a = x, b = y, signedTick = Verse.Find.TickManager.TicksGame };
            if (def.Expires) t.expiresTick = t.signedTick + def.durationDays * GenDate.TicksPerDay;
            treaties.Add(t);
            return t;
        }

        /// <summary>Removes a treaty quietly (expiry, a faction vanishing, an alliance ended elsewhere).</summary>
        public void Remove(Treaty t) => treaties.Remove(t);

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextId, "nextId");
            Scribe_Collections.Look(ref treaties, "treaties", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                treaties ??= new List<Treaty>();
                treaties.RemoveAll(t => t?.def == null || t.a == null || t.b == null);
            }
        }
    }
}
