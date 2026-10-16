using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Arranges and runs summits. At the player's capital, the other leader really travels there (as a delegation)
    /// and walks in with guards; the summit opens when your head of state is on the same map. Abroad, your head
    /// of state travels by caravan to their capital and opens the summit on arrival.
    /// </summary>
    public class SummitSystem : SimSystem
    {
        public override int Order => 40;

        public const int AbroadWindowDays = 25;
        public const int VisitDurationTicks = GenDate.TicksPerDay;

        private static SummitRegistry Registry => WorldComponent_Dominion.Instance.Summits;

        /// <summary>Asks a faction to meet. Returns false (with a reason) if they refuse to come.</summary>
        public static bool Arrange(Faction f, SummitAgenda agenda, TreatyDef treaty, SummitVenue venue, out string reason, bool force = false)
        {
            reason = null;
            if (Registry.For(f) != null)
            {
                reason = "Dominion_Summit_AlreadyArranged".Translate(f.Name);
                return false;
            }
            if (!force && !Rand.Chance(AttendChance(f, agenda)))
            {
                reason = "Dominion_Summit_Declined".Translate(f.Name);
                return false;
            }

            var s = Registry.Add(new Summit { faction = f, agenda = agenda, treaty = treaty, venue = venue, status = SummitStatus.Arranged });
            if (venue == SummitVenue.PlayerCapital)
            {
                s.expiresTick = Find.TickManager.TicksGame + 60 * GenDate.TicksPerDay; // until they arrive
                Delegation d = DelegationUtility.Dispatch(f, Faction.OfPlayer, DelegationPurpose.SummitToPlayer);
                if (d != null) d.summitId = s.id;
                else DelegationArrived(s.id, f);
            }
            else
                s.expiresTick = Find.TickManager.TicksGame + AbroadWindowDays * GenDate.TicksPerDay;
            return true;
        }

        public static float AttendChance(Faction f, SummitAgenda agenda)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            float opinion = comp.GetRelation(f, Faction.OfPlayer).opinion;
            if (f.HostileTo(Faction.OfPlayer))
                return agenda == SummitAgenda.Peace ? Mathf.Clamp(0.25f + s.warWeariness / 120f + s.Bloc(Bloc.Doves) * 0.4f, 0.05f, 0.9f) : 0f;
            return Mathf.Clamp(0.5f + opinion / 150f + LeadershipUtility.AcceptanceBonus(), 0.05f, 0.97f);
        }

        /// <summary>The visiting leader's delegation reaches the player's capital.</summary>
        public static void DelegationArrived(int summitId, Faction f)
        {
            Summit s = Registry.Get(summitId);
            if (s == null) return;
            Map map = VisitorUtility.PlayerCapitalMap();
            Pawn principal = null;
            Pawn leader = f.leader;
            if (leader != null && !leader.Dead && !leader.Spawned && Find.WorldPawns.Contains(leader))
            {
                Find.WorldPawns.RemovePawn(leader);
                principal = leader;
            }
            List<Pawn> party = VisitorUtility.MakeEnvoyParty(f, principal, 3);
            if (party.Count == 0 || !VisitorUtility.SpawnVisitors(f, map, party, VisitDurationTicks + GenDate.TicksPerHour * 4))
            {
                Cancel(s.id, "Dominion_Summit_CancelledNoRoute".Translate());
                return;
            }
            s.visitor = party[0];
            s.status = SummitStatus.LeaderPresent;
            s.expiresTick = Find.TickManager.TicksGame + VisitDurationTicks;
            Find.LetterStack.ReceiveLetter("Dominion_Summit_ArrivedLabel".Translate(), "Dominion_Summit_ArrivedText".Translate(s.visitor.LabelShortCap, f.Name, s.AgendaLabel),
                LetterDefOf.PositiveEvent, s.visitor, f);
        }

        public override void Hourly()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            List<Summit> all = comp.Summits.All;
            int now = Find.TickManager.TicksGame;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Summit s = all[i];
                if (s.status == SummitStatus.InSession) continue;
                if (s.status == SummitStatus.LeaderPresent)
                {
                    if (s.visitor == null || s.visitor.Dead || !s.visitor.Spawned)
                    {
                        Cancel(s.id, "Dominion_Summit_CancelledVisitorGone".Translate());
                        continue;
                    }
                    Pawn hos = LeadershipUtility.HeadOfState();
                    if (hos != null && hos.Spawned && hos.Map == s.visitor.Map && !hos.Downed && !s.visitor.Downed && !hos.InMentalState)
                    {
                        Open(s, hos, s.visitor);
                        continue;
                    }
                }
                if (now > s.expiresTick)
                {
                    comp.GetRelation(s.faction, Faction.OfPlayer).favour -= 10f;
                    Cancel(s.id, "Dominion_Summit_CancelledNoShow".Translate(s.faction.Name));
                }
            }
        }

        /// <summary>A summit waiting for the player at this settlement, if it is the faction's capital.</summary>
        public static Summit SummitAbroadAt(Settlement settlement)
        {
            if (settlement?.Faction == null) return null;
            Summit s = Registry.For(settlement.Faction);
            if (s == null || s.venue != SummitVenue.TheirCapital || s.status != SummitStatus.Arranged) return null;
            return DelegationUtility.HomeTile(settlement.Faction) == settlement.Tile ? s : null;
        }

        public static void AttendAbroad(Settlement settlement, Caravan caravan, Pawn envoy)
        {
            Summit s = SummitAbroadAt(settlement);
            Pawn hos = LeadershipUtility.HeadOfState();
            if (s == null) return;
            if (envoy != hos || !caravan.PawnsListForReading.Contains(hos))
            {
                Messages.Message("Dominion_Summit_NeedsHeadOfState".Translate(), caravan, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Open(s, hos, settlement.Faction.leader);
        }

        private static void Open(Summit s, Pawn hos, Pawn theirs)
        {
            s.status = SummitStatus.InSession;
            Find.WindowStack.Add(new Dialog_Summit(s, hos, theirs));
        }

        public static void Finish(Summit s, bool success, string outcome)
        {
            Registry.All.Remove(s);
            EventBus.Raise(success ? DominionEventDefOf.Dominion_SummitHeld : DominionEventDefOf.Dominion_SummitFailed,
                Faction.OfPlayer, s.faction, detail: s.AgendaLabel.ToLower());
        }

        public static void Cancel(int id, string reason)
        {
            Summit s = Registry.Get(id);
            if (s == null) return;
            Registry.All.Remove(s);
            Messages.Message("Dominion_Summit_Cancelled".Translate(s.faction.Name, reason), MessageTypeDefOf.NegativeEvent, historical: true);
        }
    }

    /// <summary>Statecraft action: propose a summit, choosing the agenda and where to meet.</summary>
    public class DiplomacyAction_ProposeSummit : DiplomacyActionWorker
    {
        public override string DisabledReason(Faction f)
        {
            if (WorldComponent_Dominion.Instance.Summits.For(f) != null) return "Dominion_Summit_AlreadyArranged".Translate(f.Name);
            if (f.HostileTo(Faction.OfPlayer) && f.def.permanentEnemy) return "Dominion_Action_PermanentEnemy".Translate();
            return null;
        }

        public override void Execute(Faction f)
        {
            bool hostile = f.HostileTo(Faction.OfPlayer);
            var options = new List<FloatMenuOption>();
            if (hostile)
                options.Add(AgendaOption(f, SummitAgenda.Peace, null));
            else
            {
                options.Add(AgendaOption(f, SummitAgenda.Relations, null));
                foreach (TreatyDef t in DefDatabase<TreatyDef>.AllDefsListForReading)
                    if (WorldComponent_Dominion.Instance.Treaties.CannotSignReason(t, Faction.OfPlayer, f) == null
                        && (t.requiredResearch == null || t.requiredResearch.IsFinished))
                        options.Add(AgendaOption(f, SummitAgenda.Treaty, t));
            }
            if (HostageUtility.AnyHostages(f))
                options.Add(AgendaOption(f, SummitAgenda.Hostages, null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private FloatMenuOption AgendaOption(Faction f, SummitAgenda agenda, TreatyDef treaty)
        {
            string label = agenda == SummitAgenda.Treaty ? treaty.LabelCap.ToString() : ("Dominion_SummitAgenda_" + agenda).Translate().ToString();
            return new FloatMenuOption(label + " (" + "Dominion_Summit_AttendChance".Translate(SummitSystem.AttendChance(f, agenda).ToStringPercent()) + ")", () =>
            {
                var venues = new List<FloatMenuOption>();
                if (!f.HostileTo(Faction.OfPlayer))
                    venues.Add(new FloatMenuOption("Dominion_SummitVenue_PlayerCapital".Translate(), () => Arrange(f, agenda, treaty, SummitVenue.PlayerCapital)));
                venues.Add(new FloatMenuOption("Dominion_SummitVenue_TheirCapital".Translate(f.Name), () => Arrange(f, agenda, treaty, SummitVenue.TheirCapital)));
                Find.WindowStack.Add(new FloatMenu(venues));
            });
        }

        private void Arrange(Faction f, SummitAgenda agenda, TreatyDef treaty, SummitVenue venue)
        {
            Commit(f);
            if (SummitSystem.Arrange(f, agenda, treaty, venue, out string reason))
                Find.LetterStack.ReceiveLetter("Dominion_Summit_ArrangedLabel".Translate(), ("Dominion_Summit_Arranged_" + venue).Translate(f.Name,
                    SummitSystem.AbroadWindowDays), LetterDefOf.NeutralEvent, null, f);
            else
                Report(reason, false);
        }
    }
}
