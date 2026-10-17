using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    [DefOf]
    public static class CouncilMotionDefOf
    {
        public static CouncilMotionDef Dominion_Motion_Sanctions;
        public static CouncilMotionDef Dominion_Motion_NonProliferation;
        public static CouncilMotionDef Dominion_Motion_ArmsEmbargo;
        public static CouncilMotionDef Dominion_Motion_Peacekeeping;
        public static CouncilMotionDef Dominion_Motion_Tribunal;
        public static CouncilMotionDef Dominion_Motion_RecogniseConquest;
        public static CouncilMotionDef Dominion_Motion_Liberation;
        public static CouncilMotionDef Dominion_Motion_AdmitMember;
        public static CouncilMotionDef Dominion_Motion_ExpelMember;
        public static CouncilMotionDef Dominion_Motion_NextHost;
        public static CouncilMotionDef Dominion_Motion_ReliefFund;
        public static CouncilMotionDef Dominion_Motion_FreeTrade;
        public static CouncilMotionDef Dominion_Motion_DebtRelief;
        public static CouncilMotionDef Dominion_Motion_CommonCurrency;
        public static CouncilMotionDef Dominion_Motion_Quarantine;
        public static CouncilMotionDef Dominion_Motion_RefugeeProtection;
        public static CouncilMotionDef Dominion_Motion_BanOrganTrade;
        public static CouncilMotionDef Dominion_Motion_BanRaiding;
        public static CouncilMotionDef Dominion_Motion_CulturalExchange;

        static CouncilMotionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CouncilMotionDefOf));
        }
    }

    /// <summary>Shared helpers for the expanded motions.</summary>
    public abstract class CouncilMotionWorkerEx : CouncilMotionWorker
    {
        protected static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        protected static FactionState S(Faction f) => Comp.GetState(f);

        protected static bool Meme(Faction f, string defName) => !f.IsPlayer && GovernmentUtility.HasMeme(f, defName);

        protected static bool AnyMeme(Faction f, params string[] defNames) => !f.IsPlayer && GovernmentUtility.HasAnyMeme(f, defNames);

        protected static bool HasPrecept(Faction f, string prefix)
        {
            if (f.IsPlayer || !ModsConfig.IdeologyActive) return false;
            Ideo ideo = f.ideos?.PrimaryIdeo;
            if (ideo == null) return false;
            foreach (Precept p in ideo.PreceptsListForReading)
                if (p.def.defName.StartsWith(prefix)) return true;
            return false;
        }

        /// <summary>Whether <paramref name="f"/> was the primary party of one of these events in the past days.</summary>
        protected static bool Recently(Faction f, int days, params DominionEventDef[] defs)
        {
            int since = Find.TickManager.TicksGame - days * GenDate.TicksPerDay;
            IReadOnlyList<DominionEvent> events = Comp.EventLog.Events;
            for (int i = events.Count - 1; i >= 0 && events[i].tick > since; i--)
                if ((f == null || events[i].primary == f) && defs.Contains(events[i].def)) return true;
            return false;
        }

        protected string AlreadyInForce() => CouncilUtility.ActiveDecree(def) != null ? (string)"Dominion_Council_AlreadyInForce".Translate() : null;

        protected static string NotForOutlaws(Faction f) => f.def.permanentEnemy ? (string)"Dominion_Council_NotForOutlaws".Translate() : null;

        /// <summary>The council asks the player to act if the player is a member; a refusal is a breach.</summary>
        protected static void AskMember(Decree d, string text, string accept, System.Action onAccept)
        {
            if (CouncilUtility.IsMember(Faction.OfPlayer)) CouncilUtility.AskPlayer(d, text, accept, onAccept);
        }

        protected static void Duty(string text, Faction look = null)
        {
            if (CouncilUtility.IsMember(Faction.OfPlayer))
                Find.LetterStack.ReceiveLetter("Dominion_Decree_DutyLabel".Translate(), text, LetterDefOf.NeutralEvent, null, look != null && !look.IsPlayer ? look : null);
        }

        /// <summary>Pairs of factions at war that the council can name, attacker first.</summary>
        protected static IEnumerable<List<Faction>> WarPairs(System.Func<List<Faction>, string> invalid)
        {
            foreach (War w in Comp.Wars.wars)
            {
                var pair = new List<Faction> { w.attacker, w.defender };
                if (invalid(pair) == null) yield return pair;
            }
        }
    }

    // ===================================================================================================== security

    /// <summary>No member may use ballistic missiles while it holds (needs InterRim Ballistic Missile).</summary>
    public class CouncilMotion_NonProliferation : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? AlreadyInForce() ?? (Integration_IRBM.Instance == null ? (string)"Dominion_Council_NeedsIRBM".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            if (Integration_IRBM.Instance == null || CouncilUtility.ActiveDecree(def) != null) return false;
            return (P(proposer).honour > 0.45f || Meme(proposer, "VME_Pacifist")) && Recently(null, 90, DominionEventDefOf.Dominion_MissileStrike);
        }

        public override float Utility(Faction voter, Motion m)
        {
            float u = P(voter).honour * 0.5f - P(voter).aggression * 0.4f;
            if (Meme(voter, "VME_Pacifist")) u += 0.5f;
            if (AnyMeme(voter, "Supremacist", "Raider", "VME_BloodCourt")) u -= 0.3f;
            // Those who were struck want it; those with silos of their own are slower to give them up.
            int since = Find.TickManager.TicksGame - 120 * GenDate.TicksPerDay;
            foreach (DominionEvent ev in Comp.EventLog.Events)
                if (ev.tick > since && ev.def == DominionEventDefOf.Dominion_MissileStrike && ev.secondary == voter) { u += 0.5f; break; }
            if (voter.def.techLevel >= TechLevel.Spacer) u -= 0.15f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_NonProliferation".Translate();

        public override void OnPassed(Decree d) => Duty("Dominion_Decree_NonProliferation_Duty".Translate());

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            if (ev.def == DominionEventDefOf.Dominion_MissileStrike && ev.primary != null && CouncilUtility.IsMember(ev.primary))
                CouncilSystem.Violate(d, ev.primary);
        }
    }

    /// <summary>Nobody arms a faction at war: its strength wastes away, and its allies must drop it.</summary>
    public class CouncilMotion_ArmsEmbargo : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? NotForOutlaws(t[0]) ?? (!Comp.Wars.AnyWar(t[0]) ? (string)"Dominion_Council_NotAtWar".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().Where(f => f != proposer && InvalidReason(new List<Faction> { f }) == null && Op(proposer, f) < -10f
                    && Comp.Wars.wars.Any(w => w.attacker == f))
                .OrderBy(f => Op(proposer, f)).FirstOrDefault();
            targets = x != null ? new List<Faction> { x } : null;
            return x != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -2f;
            FactionState s = S(voter);
            return -Op(voter, x) / 100f + s.Bloc(Bloc.Doves) * 0.5f - (Ally(voter, x) ? 1f : 0f) + (voter.HostileTo(x) ? 0.6f : 0f) - P(voter).greed * 0.2f - 0.05f;
        }

        public override string DutyFor(Motion m, Faction f) =>
            f == m.targets[0] ? (string)"Dominion_Duty_YouAreTarget".Translate() : "Dominion_Duty_ArmsEmbargo".Translate(m.targets[0].Name);

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            if (!x.IsPlayer) S(x).militaryStrength *= 0.92f;
            if (!x.IsPlayer && !x.HostileTo(Faction.OfPlayer))
                Duty("Dominion_Decree_ArmsEmbargo_Duty".Translate(x.Name, d.def.graceDays), x);
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            if (!x.IsPlayer) S(x).militaryStrength *= 0.996f;
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || d.violators.Contains(m)) continue;
                RelationState r = Comp.GetRelation(m, x);
                bool armsFlow = m.IsPlayer ? !r.embargo && !m.HostileTo(x) : Ally(m, x);
                if (!armsFlow) continue;
                if (!m.IsPlayer && d.InGrace && Rand.Chance(0.05f + P(m).honour * 0.15f))
                {
                    // An ally keeps to the decree by stepping back from the alliance.
                    DiplomacyUtility.SetRelationKind(m, x, FactionRelationKind.Neutral, m, d.def.label);
                    if (!d.complied.Contains(m)) d.complied.Add(m);
                }
                else if (!d.InGrace) CouncilSystem.Violate(d, m);
            }
        }

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            // Sending troops to the embargoed side is arming it.
            if (ev.def == DominionEventDefOf.Dominion_Reinforcements && ev.secondary == d.targets[0] && ev.primary != null && ev.primary != d.targets[0])
                CouncilSystem.Violate(d, ev.primary);
        }
    }

    /// <summary>Members send forces to the side that was attacked until the aggressor stands down.</summary>
    public class CouncilMotion_Peacekeeping : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            if (t[0] == t[1] || Comp.Wars.Find(t[0], t[1]) == null) return "Dominion_Council_NotAtWar".Translate();
            return NotForOutlaws(t[0]) ?? NotForOutlaws(t[1]);
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions() => WarPairs(InvalidReason);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = null;
            FactionState s = S(proposer);
            if (s.Bloc(Bloc.Doves) < 0.25f && P(proposer).honour < 0.6f && !Meme(proposer, "VME_Pacifist")) return false;
            foreach (War w in Comp.Wars.wars)
            {
                if (w.attacker == proposer || Ally(proposer, w.attacker) || !CouncilUtility.IsMember(w.defender)) continue;
                var pair = new List<Faction> { w.attacker, w.defender };
                if (InvalidReason(pair) != null) continue;
                targets = pair;
                return true;
            }
            return false;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction aggressor = m.targets[0], victim = m.targets[1];
            if (voter == aggressor) return -2f;
            if (voter == victim) return 1.5f;
            FactionState s = S(voter);
            float u = s.Bloc(Bloc.Doves) * 0.8f + P(voter).honour * 0.3f - s.Bloc(Bloc.Hawks) * 0.3f - s.warWeariness / 200f;
            if (Ally(voter, aggressor)) u -= 1f;
            if (Ally(voter, victim)) u += 0.5f;
            if (Meme(voter, "VME_Pacifist")) u += 0.4f;
            return u - 0.15f;
        }

        public override string DutyFor(Motion m, Faction f)
        {
            if (f == m.targets[0]) return "Dominion_Duty_Peacekeeping_Aggressor".Translate(m.targets[1].Name);
            return f == m.targets[1] ? (string)"Dominion_Duty_YouAreProtected".Translate() : "Dominion_Duty_Peacekeeping".Translate(PeacekeepingDues);
        }

        public const int PeacekeepingDues = 400;

        public override void OnPassed(Decree d)
        {
            Faction aggressor = d.targets[0], victim = d.targets[1];
            // The strongest willing members march to the victim's side.
            int sent = 0;
            foreach (Faction m in CouncilUtility.Members().Where(f => !f.IsPlayer && f != aggressor && f != victim && !Ally(f, aggressor) && !f.HostileTo(victim))
                         .OrderByDescending(f => S(f).militaryStrength))
            {
                if (sent >= 2) break;
                if (WarbandUtility.CountActive(m) >= WarbandUtility.MaxFor(m)) continue;
                if (WarbandUtility.Launch(m, aggressor, WarbandMission.Reinforce, null, victim) == null) continue;
                d.complied.Add(m);
                d.imposed.Add(m);
                sent++;
            }
            if (aggressor.IsPlayer)
                Duty("Dominion_Decree_Peacekeeping_YouAggressor".Translate(victim.Name), victim);
            else if (!victim.IsPlayer)
                AskMember(d, "Dominion_Decree_Peacekeeping_Ask".Translate(victim.Name, aggressor.Name, PeacekeepingDues), "Dominion_Decree_Peacekeeping_Pay".Translate(PeacekeepingDues), () =>
                {
                    EconomyEffects.Transfer(Faction.OfPlayer, victim, PeacekeepingDues);
                    d.complied.Add(Faction.OfPlayer);
                });
        }

        public override void DailyCheck(Decree d)
        {
            Faction aggressor = d.targets[0], victim = d.targets[1];
            War w = Comp.Wars.Find(aggressor, victim);
            if (w == null) return;
            // The coalition's pressure tells on the battlefield and at home.
            WarUtility.AddScore(victim, aggressor, 1.5f);
            if (!aggressor.IsPlayer) S(aggressor).warWeariness = Mathf.Min(100f, S(aggressor).warWeariness + 1f);
            if (!d.InGrace && CouncilUtility.IsMember(aggressor)) CouncilSystem.Violate(d, aggressor);
        }
    }

    /// <summary>A tribunal judges an aggressor or a serial breaker of decrees: a fine paid to its victims, and disgrace.</summary>
    public class CouncilMotion_Tribunal : CouncilMotionWorkerEx
    {
        private static readonly DominionEventDef[] Crimes =
        {
            DominionEventDefOf.Dominion_Assassination, DominionEventDefOf.Dominion_Sabotage, DominionEventDefOf.Dominion_FalseFlag,
            DominionEventDefOf.Dominion_MissileStrike, DominionEventDefOf.Dominion_EnvoyCaptured, DominionEventDefOf.Dominion_DelegationIntercepted,
        };

        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t) ?? NotForOutlaws(t[0]);
            if (r != null) return r;
            Faction x = t[0];
            bool guilty = Comp.Wars.wars.Any(w => w.attacker == x) || Recently(x, 90, Crimes) || Violations(x) > 0;
            return guilty ? null : (string)"Dominion_Council_NoCrime".Translate();
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().Where(f => f != proposer && Op(proposer, f) < -15f && InvalidReason(new List<Faction> { f }) == null)
                .OrderBy(f => Op(proposer, f) - Violations(f) * 15f).FirstOrDefault();
            if (x == null && CouncilUtility.IsMember(Faction.OfPlayer) && Op(proposer, Faction.OfPlayer) < -30f
                && InvalidReason(new List<Faction> { Faction.OfPlayer }) == null) x = Faction.OfPlayer;
            targets = x != null ? new List<Faction> { x } : null;
            return x != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -2f;
            return -Op(voter, x) / 100f + P(voter).honour * 0.4f + Violations(x) * 0.2f - (Ally(voter, x) ? 1f : 0f) - 0.1f;
        }

        public static int Fine(Faction x) => x.IsPlayer ? 600 : Mathf.RoundToInt(Mathf.Clamp(S(x).wealth * 0.03f, 300f, 2500f));

        private static List<Faction> Victims(Faction x)
        {
            List<Faction> victims = Comp.Wars.wars.Where(w => w.attacker == x).Select(w => w.defender).Where(f => !f.defeated).ToList();
            if (victims.Count == 0) victims = CouncilUtility.Members().Where(f => f != x && !f.IsPlayer).ToList();
            return victims;
        }

        public override string DutyFor(Motion m, Faction f) => f == m.targets[0] ? "Dominion_Duty_Tribunal_You".Translate(Fine(f)) : null;

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            foreach (Faction m in CouncilUtility.Members())
                if (m != x && !m.IsPlayer) Comp.GetRelation(m, x).favour -= 10f;
            List<Faction> victims = Victims(x);
            if (x.IsPlayer)
            {
                AskMember(d, "Dominion_Decree_Tribunal_Ask".Translate(Fine(x)), "Dominion_Decree_Tribunal_Pay".Translate(Fine(x)), () =>
                {
                    int each = Fine(x) / Mathf.Max(1, victims.Count);
                    foreach (Faction v in victims) EconomyEffects.Transfer(Faction.OfPlayer, v, each);
                    d.complied.Add(Faction.OfPlayer);
                });
                return;
            }
            FactionState s = S(x);
            s.legitimacy = Mathf.Max(0f, s.legitimacy - 15f);
            s.stability = Mathf.Max(0f, s.stability - 5f);
            // The guilty pay if they have the honour (or fear) to; otherwise it is a breach.
            if (Rand.Chance(0.3f + P(x).honour * 0.5f))
            {
                int each = Fine(x) / Mathf.Max(1, victims.Count);
                foreach (Faction v in victims) EconomyEffects.Transfer(x, v, each);
                d.complied.Add(x);
            }
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            if (!d.InGrace && !d.complied.Contains(x) && CouncilUtility.IsMember(x)) CouncilSystem.Violate(d, x);
        }
    }

    // ===================================================================================================== territory and members

    /// <summary>Occupied settlements pass to their occupier for good.</summary>
    public class CouncilMotion_RecogniseConquest : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            return t[0] == t[1] || !WarUtility.HoldsTakenSettlements(t[0], t[1]) ? (string)"Dominion_Council_NothingOccupied".Translate() : null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions() => OccupationPairs(InvalidReason);

        /// <summary>Every (occupier, former owner) pair with settlements changed hands.</summary>
        public static IEnumerable<List<Faction>> OccupationPairs(System.Func<List<Faction>, string> invalid)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var seen = new HashSet<long>();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                int orig = comp.Wars.OriginalOwnerOf(s);
                Faction former = orig >= 0 ? comp.Cache.FactionById(orig) : null;
                if (former == null || s.Faction == null || s.Faction == former) continue;
                if (!seen.Add(RelationState.Key(s.Faction.loadID, former.loadID))) continue;
                var pair = new List<Faction> { s.Faction, former };
                if (invalid(pair) == null) yield return pair;
            }
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = OccupationPairs(InvalidReason).FirstOrDefault(p => p[0] == proposer || (Ally(proposer, p[0]) && Op(proposer, p[1]) < 0f));
            return targets != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction holder = m.targets[0], former = m.targets[1];
            if (voter == holder) return 1.5f;
            if (voter == former) return -2f;
            FactionState s = S(voter);
            return (Op(voter, holder) - Op(voter, former)) / 100f + s.Bloc(Bloc.Hawks) * 0.3f - P(voter).honour * 0.3f - 0.05f;
        }

        public override string DutyFor(Motion m, Faction f) =>
            f == m.targets[1] ? "Dominion_Duty_Recognise_You".Translate(m.targets[0].Name) : f == m.targets[0] ? "Dominion_Duty_Recognise_Holder".Translate() : null;

        public override void OnPassed(Decree d)
        {
            Faction holder = d.targets[0], former = d.targets[1];
            foreach (Settlement s in Find.WorldObjects.Settlements.ToList())
                // Already the holder's: Cede would do nothing, so the occupation is simply struck from the register.
                if (s.Faction == holder && Comp.Wars.OriginalOwnerOf(s) == former.loadID) Comp.Wars.originalOwner.Remove(s.Tile.tileId);
            if (!holder.IsPlayer) S(holder).legitimacy = Mathf.Min(100f, S(holder).legitimacy + 5f);
            if (!former.IsPlayer)
                foreach (Faction m in CouncilUtility.Members())
                    if (m != former) Comp.GetRelation(former, m).grievance += 5f;
        }
    }

    /// <summary>The occupier must hand occupied settlements back to their owner within the grace period.</summary>
    public class CouncilMotion_Liberation : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            return t[0] == t[1] || !WarUtility.HoldsTakenSettlements(t[0], t[1]) ? (string)"Dominion_Council_NothingOccupied".Translate() : null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions() => CouncilMotion_RecogniseConquest.OccupationPairs(InvalidReason);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = CouncilMotion_RecogniseConquest.OccupationPairs(InvalidReason)
                .FirstOrDefault(p => p[1] == proposer || (Ally(proposer, p[1]) && Op(proposer, p[0]) < 0f) || (P(proposer).honour > 0.7f && Op(proposer, p[0]) < -20f));
            return targets != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction holder = m.targets[0], former = m.targets[1];
            if (voter == holder) return -2f;
            if (voter == former) return 1.5f;
            FactionState s = S(voter);
            return (Op(voter, former) - Op(voter, holder)) / 100f + s.Bloc(Bloc.Doves) * 0.3f + P(voter).honour * 0.2f - 0.1f;
        }

        public override string DutyFor(Motion m, Faction f) => f == m.targets[0] ? "Dominion_Duty_Liberation_You".Translate(m.targets[1].Name, def.graceDays) : null;

        private static List<Settlement> Held(Faction holder, Faction former) =>
            Find.WorldObjects.Settlements.Where(s => s.Faction == holder && WorldComponent_Dominion.Instance.Wars.OriginalOwnerOf(s) == former.loadID).ToList();

        public override void OnPassed(Decree d)
        {
            Faction holder = d.targets[0], former = d.targets[1];
            if (holder.IsPlayer)
                AskMember(d, "Dominion_Decree_Liberation_Ask".Translate(former.Name, string.Join(", ", Held(holder, former).Select(s => s.Label))),
                    "Dominion_Decree_Liberation_Accept".Translate(), () =>
                    {
                        foreach (Settlement s in Held(holder, former)) WarUtility.ReturnSettlement(s, former);
                        d.complied.Add(holder);
                    });
        }

        public override void DailyCheck(Decree d)
        {
            Faction holder = d.targets[0], former = d.targets[1];
            if (!WarUtility.HoldsTakenSettlements(holder, former))
            {
                if (!d.complied.Contains(holder)) d.complied.Add(holder);
                return;
            }
            if (!holder.IsPlayer && d.InGrace && Rand.Chance(0.03f + P(holder).honour * 0.12f))
            {
                foreach (Settlement s in Held(holder, former)) WarUtility.ReturnSettlement(s, former);
                d.complied.Add(holder);
            }
            else if (!d.InGrace && CouncilUtility.IsMember(holder)) CouncilSystem.Violate(d, holder);
        }

        public override float EconomyBonus(Faction f, Decree d) =>
            f == d.targets[0] && !d.complied.Contains(f) && !d.InGrace ? -0.03f : 0f;
    }

    /// <summary>Gives a faction below the technology threshold a seat at the council.</summary>
    public class CouncilMotion_AdmitMember : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            Faction x = t[0];
            if (x.IsPlayer || x.def.permanentEnemy || !DominionScheduler.IsSimulated(x)) return "Dominion_Council_CannotAdmit".Translate();
            return CouncilUtility.IsMember(x) || CouncilUtility.Admitted(x) ? (string)"Dominion_Council_AlreadyMember".Translate() : null;
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().Where(f => Op(proposer, f) > 30f && InvalidReason(new List<Faction> { f }) == null).OrderByDescending(f => Op(proposer, f)).FirstOrDefault();
            targets = x != null && P(proposer).honour > 0.4f ? new List<Faction> { x } : null;
            return targets != null;
        }

        public override float Utility(Faction voter, Motion m) =>
            Op(voter, m.targets[0]) / 100f + 0.05f - (AnyMeme(voter, "Supremacist", "VFEA_Isolationist") ? 0.4f : 0f) + (Meme(voter, "VME_Egalitarian") ? 0.3f : 0f);

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            foreach (Faction m in CouncilUtility.Members())
                if (m != x) Comp.GetRelation(x, m).favour += 6f;
        }
    }

    /// <summary>Suspends a repeat breaker of decrees: no seat, no vote and no protection while it holds.</summary>
    public class CouncilMotion_ExpelMember : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            Faction x = t[0];
            if (!CouncilUtility.IsMember(x)) return "Dominion_Council_NotAMember".Translate();
            return Violations(x) < 2 ? (string)"Dominion_Council_NotRepeatOffender".Translate() : null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions()
        {
            foreach (Faction f in CouncilUtility.Members())
                if (!f.IsPlayer && InvalidReason(new List<Faction> { f }) == null) yield return new List<Faction> { f };
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.Members().Where(f => f != proposer && Op(proposer, f) < 0f && InvalidReason(new List<Faction> { f }) == null)
                .OrderByDescending(f => Violations(f)).FirstOrDefault();
            targets = x != null ? new List<Faction> { x } : null;
            return x != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -3f;
            return -Op(voter, x) / 100f + Violations(x) * 0.3f + P(voter).honour * 0.2f - (Ally(voter, x) ? 1f : 0f) - 0.3f;
        }

        public override string DutyFor(Motion m, Faction f) => f == m.targets[0] ? "Dominion_Duty_Expel_You".Translate(def.durationDays) : null;

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            if (x.IsPlayer)
                Find.LetterStack.ReceiveLetter("Dominion_Decree_ExpelledLabel".Translate(), "Dominion_Decree_Expelled_You".Translate(d.expiresTick >= 0 ? (d.expiresTick - d.passedTick) / GenDate.TicksPerDay : d.def.durationDays), LetterDefOf.NegativeEvent);
            else S(x).legitimacy = Mathf.Max(0f, S(x).legitimacy - 10f);
        }
    }

    /// <summary>Names the host of the next session.</summary>
    public class CouncilMotion_NextHost : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            Faction x = t[0];
            if (!CouncilUtility.IsMember(x) || !DelegationUtility.HomeTile(x).Valid) return "Dominion_Council_CannotHost".Translate();
            if (x.IsPlayer && LeadershipUtility.BestDesk() == null) return "Dominion_Council_CannotHost".Translate();
            return Comp.Council.host == x ? (string)"Dominion_Council_AlreadyHost".Translate() : null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions()
        {
            foreach (Faction f in CouncilUtility.Members())
                if (InvalidReason(new List<Faction> { f }) == null) yield return new List<Faction> { f };
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction> { proposer };
            return S(proposer).legitimacy < 55f && InvalidReason(targets) == null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return 1f;
            return Op(voter, x) / 100f - (voter.HostileTo(x) ? 0.8f : 0f) - 0.05f;
        }

        public override string DutyFor(Motion m, Faction f) => f == m.targets[0] ? "Dominion_Duty_Host_You".Translate() : null;

        public override void OnPassed(Decree d)
        {
            Comp.Council.nextHost = d.targets[0];
            if (!d.targets[0].IsPlayer) S(d.targets[0]).legitimacy = Mathf.Min(100f, S(d.targets[0]).legitimacy + 4f);
        }
    }

    // ===================================================================================================== economy

    /// <summary>Members pay into a fund for a faction in crisis.</summary>
    public class CouncilMotion_ReliefFund : CouncilMotionWorkerEx
    {
        public const int PlayerShare = 300;

        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            return t[0].IsPlayer || !S(t[0]).NeedsAid ? (string)"Dominion_Council_NoCrisis".Translate() : null;
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().FirstOrDefault(f => (f == proposer || Op(proposer, f) > 10f) && InvalidReason(new List<Faction> { f }) == null);
            targets = x != null ? new List<Faction> { x } : null;
            return x != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return 2f;
            FactionState s = S(voter);
            float u = Op(voter, x) / 160f + P(voter).honour * 0.4f - P(voter).greed * 0.4f + (s.InCrisis ? -0.5f : 0f);
            if (AnyMeme(voter, "VME_Healthcare", "VME_Egalitarian")) u += 0.3f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) =>
            f == m.targets[0] ? (string)"Dominion_Duty_YouAreProtected".Translate() : "Dominion_Duty_ReliefFund".Translate(PlayerShare, m.targets[0].Name);

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            FactionState s = S(x);
            s.stability = Mathf.Min(100f, s.stability + 5f);
            EconomyEffects.Shock(x, 0.04f);
            AskMember(d, "Dominion_Decree_ReliefFund_Ask".Translate(x.Name, PlayerShare), "Dominion_Decree_ReliefFund_Pay".Translate(PlayerShare), () =>
            {
                EconomyEffects.Transfer(Faction.OfPlayer, x, PlayerShare);
                Comp.GetRelation(x, Faction.OfPlayer).favour += 8f;
                d.complied.Add(Faction.OfPlayer);
            });
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || m.IsPlayer || d.complied.Contains(m) || d.violators.Contains(m)) continue;
                FactionState ms = S(m);
                // The poor and those in their own crisis are excused.
                if (ms.economyIndex < 85f || ms.InCrisis) continue;
                if (d.InGrace)
                {
                    if (!Rand.Chance(0.25f + P(m).honour * 0.25f)) continue;
                    EconomyEffects.Transfer(m, x, Mathf.RoundToInt(Mathf.Clamp(ms.wealth * 0.01f, 100f, 800f)));
                    Comp.GetRelation(x, m).favour += 5f;
                    d.complied.Add(m);
                }
                else CouncilSystem.Violate(d, m);
            }
        }
    }

    /// <summary>A free-trade zone: low tariffs between members, and no embargoes among them.</summary>
    public class CouncilMotion_FreeTrade : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return (P(proposer).greed > 0.55f || Meme(proposer, "VME_Trader")) && CouncilUtility.ActiveDecree(def) == null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            float u = P(voter).greed * 0.9f - 0.3f + S(voter).Bloc(Bloc.Merchants) * 0.4f;
            if (AnyMeme(voter, "VME_Trader", "VME_Structure_Corporate")) u += 0.3f;
            if (AnyMeme(voter, "VFEA_Isolationist", "VME_Nationalist")) u -= 0.6f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_FreeTrade".Translate();

        public override float EconomyBonus(Faction f, Decree d) => CouncilUtility.IsMember(f) ? 0.03f : -0.01f;

        public override float TradeBonus(Faction a, Faction b, Decree d) => CouncilUtility.IsMember(a) && CouncilUtility.IsMember(b) ? 0.08f : 0f;

        public override void DailyCheck(Decree d)
        {
            if (d.InGrace) return;
            List<Faction> members = CouncilUtility.Members().ToList();
            foreach (Faction a in members)
                foreach (Faction b in members)
                {
                    if (a == b || d.violators.Contains(a)) continue;
                    RelationState r = Comp.TryGetRelation(a, b);
                    // Embargoes the council itself ordered (sanctions) do not count.
                    if (r != null && r.embargo && !a.HostileTo(b) && !CouncilUtility.Named(CouncilMotionDefOf.Dominion_Motion_Sanctions, b))
                        CouncilSystem.Violate(d, a);
                }
        }
    }

    /// <summary>Forgives a faction's tribute: every tribute treaty it pays ends.</summary>
    public class CouncilMotion_DebtRelief : CouncilMotionWorkerEx
    {
        private static List<Treaty> Debts(Faction x) => WorldComponent_Dominion.Instance.Treaties.All.Where(t => t.def.tribute > 0f && t.a == x).ToList();

        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            return Debts(t[0]).Count == 0 ? (string)"Dominion_Council_NoDebts".Translate() : null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions()
        {
            var known = CouncilUtility.KnownFactions().ToList();
            known.Add(Faction.OfPlayer);
            foreach (Faction f in known)
                if (InvalidReason(new List<Faction> { f }) == null) yield return new List<Faction> { f };
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().FirstOrDefault(f => (f == proposer || (Op(proposer, f) > 20f && P(proposer).honour > 0.5f)) && InvalidReason(new List<Faction> { f }) == null);
            targets = x != null ? new List<Faction> { x } : null;
            return x != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return 2f;
            if (Debts(x).Any(t => t.b == voter)) return -1.5f; // a creditor loses
            return Op(voter, x) / 120f + S(voter).Bloc(Bloc.Doves) * 0.3f - P(voter).greed * 0.3f;
        }

        public override string DutyFor(Motion m, Faction f)
        {
            if (f == m.targets[0]) return "Dominion_Duty_DebtRelief_You".Translate();
            return Debts(m.targets[0]).Any(t => t.b == f) ? "Dominion_Duty_DebtRelief_Creditor".Translate(m.targets[0].Name) : null;
        }

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            if (!x.IsPlayer)
            {
                S(x).stability = Mathf.Min(100f, S(x).stability + 5f);
                EconomyEffects.Shock(x, 0.03f);
            }
            foreach (Treaty t in Debts(x))
            {
                if (t.b.IsPlayer)
                {
                    Treaty debt = t;
                    AskMember(d, "Dominion_Decree_DebtRelief_Ask".Translate(x.Name), "Dominion_Decree_DebtRelief_Forgive".Translate(), () =>
                    {
                        Comp.Treaties.Cancel(debt, Faction.OfPlayer);
                        Comp.GetRelation(x, Faction.OfPlayer).favour += 10f;
                        d.complied.Add(Faction.OfPlayer);
                    });
                }
                else if (Rand.Chance(0.4f + P(t.b).honour * 0.5f))
                {
                    Comp.Treaties.Cancel(t, t.b);
                    d.complied.Add(t.b);
                }
            }
        }

        public override void DailyCheck(Decree d)
        {
            if (d.InGrace) return;
            foreach (Treaty t in Debts(d.targets[0]))
                if (CouncilUtility.IsMember(t.b)) CouncilSystem.Violate(d, t.b);
        }
    }

    /// <summary>A common currency: steadier prices and a little more trade between members.</summary>
    public class CouncilMotion_CommonCurrency : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return P(proposer).greed > 0.5f && P(proposer).paranoia < 0.5f && CouncilUtility.ActiveDecree(def) == null && Rand.Chance(0.5f);
        }

        public override float Utility(Faction voter, Motion m)
        {
            float u = P(voter).greed * 0.5f + S(voter).Bloc(Bloc.Merchants) * 0.5f - P(voter).paranoia * 0.4f - 0.1f;
            if (AnyMeme(voter, "VFEA_Isolationist", "VME_Nationalist")) u -= 0.5f;
            return u;
        }

        public override float EconomyBonus(Faction f, Decree d) => CouncilUtility.IsMember(f) ? 0.02f : 0f;

        public override float TradeBonus(Faction a, Faction b, Decree d) => CouncilUtility.IsMember(a) && CouncilUtility.IsMember(b) ? 0.03f : 0f;

        public override void DailyCheck(Decree d)
        {
            // Price stability: shocks to members' economies settle faster.
            foreach (Faction m in CouncilUtility.Members())
                if (!m.IsPlayer) S(m).economyShock *= 0.97f;
        }
    }

    // ===================================================================================================== society

    /// <summary>Members close their borders to a faction with plague, and the plague does not spread to them.</summary>
    public class CouncilMotion_Quarantine : CouncilMotionWorkerEx
    {
        private static bool HasPlague(Faction f) => !f.IsPlayer && S(f).InCrisis && S(f).crisis?.defName == "Dominion_PlagueOutbreak";

        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            return !HasPlague(t[0]) ? (string)"Dominion_Council_NoPlague".Translate() : null;
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction x = CouncilUtility.KnownFactions().FirstOrDefault(f => f != proposer && InvalidReason(new List<Faction> { f }) == null
                && Comp.GetRelation(proposer, f).distance is float dist && dist >= 0f && dist < 40f);
            targets = x != null && P(proposer).paranoia > 0.35f ? new List<Faction> { x } : null;
            return targets != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -1.5f;
            float dist = Comp.GetRelation(voter, x).distance;
            float u = P(voter).paranoia * 0.5f + (dist >= 0f && dist < 40f ? 0.3f : 0f) - Op(voter, x) / 300f;
            if (Meme(voter, "VME_HolyDiseases")) u -= 0.6f;
            if (Meme(voter, "VME_Healthcare")) u += 0.3f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => f == m.targets[0] ? (string)"Dominion_Duty_YouAreTarget".Translate() : "Dominion_Duty_Quarantine".Translate(m.targets[0].Name);

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            EconomyEffects.Shock(x, -0.03f, destroyGoods: false);
            RelationState mine = Comp.GetRelation(Faction.OfPlayer, x);
            if (mine.border != BorderStatus.Closed)
                AskMember(d, "Dominion_Decree_Quarantine_Ask".Translate(x.Name), "Dominion_Decree_Quarantine_Close".Translate(), () =>
                {
                    mine.border = BorderStatus.Closed;
                    d.complied.Add(Faction.OfPlayer);
                });
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || d.violators.Contains(m) || m.HostileTo(x)) continue;
                RelationState r = Comp.GetRelation(m, x);
                if (r.border == BorderStatus.Closed)
                {
                    if (!d.complied.Contains(m)) d.complied.Add(m);
                    continue;
                }
                if (!m.IsPlayer && d.InGrace && Rand.Chance(0.3f + P(m).paranoia * 0.4f))
                {
                    r.border = BorderStatus.Closed;
                    d.complied.Add(m);
                    d.imposed.Add(m);
                }
                else if (!d.InGrace) CouncilSystem.Violate(d, m);
            }
        }

        public override void OnExpired(Decree d)
        {
            Faction x = d.targets[0];
            foreach (Faction m in d.imposed)
                if (!m.IsPlayer) Comp.GetRelation(m, x).border = BorderStatus.Open;
        }
    }

    /// <summary>Members share the burden when a faction's people flee: they take refugees in and must not shut them out.</summary>
    public class CouncilMotion_RefugeeProtection : CouncilMotionWorkerEx
    {
        public const int PlayerShare = 150;
        private static DominionEventDef refugeeWave;

        private static DominionEventDef RefugeeWave => refugeeWave ??= DefDatabase<DominionEventDef>.GetNamedSilentFail("Dominion_RefugeeWave");

        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return CouncilUtility.ActiveDecree(def) == null && P(proposer).honour > 0.5f
                   && (Recently(null, 60, RefugeeWave) || Recently(null, 60, DominionEventDefOf.Dominion_Emigration));
        }

        public override float Utility(Faction voter, Motion m)
        {
            float u = P(voter).honour * 0.5f + S(voter).Bloc(Bloc.Doves) * 0.3f - P(voter).paranoia * 0.4f;
            if (AnyMeme(voter, "VFEA_Isolationist", "Supremacist")) u -= 0.6f;
            if (AnyMeme(voter, "VME_Egalitarian", "VME_Healthcare", "VME_Emancipation")) u += 0.3f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_Refugees".Translate(PlayerShare);

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            if (ev.def != RefugeeWave || ev.primary == null || ev.primary.IsPlayer) return;
            Faction fleeing = ev.primary;
            var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == fleeing || m.HostileTo(fleeing)) continue;
                RelationState r = Comp.GetRelation(m, fleeing);
                if (r.border == BorderStatus.Closed)
                {
                    CouncilSystem.Violate(d, m);
                    continue;
                }
                if (m.IsPlayer)
                {
                    AskMember(d, "Dominion_Decree_Refugees_Ask".Translate(fleeing.Name, PlayerShare), "Dominion_Decree_Refugees_Pay".Translate(PlayerShare), () =>
                    {
                        EconomyEffects.Transfer(Faction.OfPlayer, fleeing, PlayerShare);
                        Comp.GetRelation(fleeing, Faction.OfPlayer).favour += 6f;
                    });
                    continue;
                }
                // Taking people in: a few more mouths, a little unrest, and gratitude.
                if (ed != null && ed.IsActive) ed.ModifyPopulation(m, Rand.RangeInclusive(1, 4));
                S(m).stability = Mathf.Max(0f, S(m).stability - 1f);
                Comp.GetRelation(fleeing, m).favour += 4f;
            }
        }
    }

    /// <summary>No member harvests or sells organs.</summary>
    public class CouncilMotion_BanOrganTrade : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        private bool Harvesters(Faction f) => AnyMeme(f, "Cannibal", "VME_Fleshcrafters") || HasPrecept(f, "OrganUse_Acceptable");

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return CouncilUtility.ActiveDecree(def) == null && !Harvesters(proposer) && P(proposer).honour > 0.6f && Rand.Chance(0.3f);
        }

        public override float Utility(Faction voter, Motion m) =>
            Harvesters(voter) ? -0.7f : P(voter).honour * 0.4f - P(voter).greed * 0.2f + (Meme(voter, "VME_Healthcare") ? 0.2f : 0f) - 0.05f;

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_Organs".Translate();

        public override void DailyCheck(Decree d)
        {
            if (d.InGrace) return;
            if (CouncilUtility.IsMember(Faction.OfPlayer) && !d.violators.Contains(Faction.OfPlayer))
            {
                HistoryEventsManager history = Find.HistoryEventsManager;
                if (history.GetRecentCountWithinTicks(HistoryEventDefOf.HarvestedOrgan, GenDate.TicksPerDay, Faction.OfPlayer) > 0
                    || history.GetRecentCountWithinTicks(HistoryEventDefOf.HarvestedOrganFromGuest, GenDate.TicksPerDay, Faction.OfPlayer) > 0)
                    CouncilSystem.Violate(d, Faction.OfPlayer);
            }
            // Members whose ways allow it get caught now and then.
            foreach (Faction m in CouncilUtility.Members())
                if (!m.IsPlayer && Harvesters(m) && !d.violators.Contains(m) && Rand.Chance(0.01f))
                    CouncilSystem.Violate(d, m);
        }
    }

    /// <summary>No member raids caravans, convoys or stores.</summary>
    public class CouncilMotion_BanRaiding : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return CouncilUtility.ActiveDecree(def) == null && !AnyMeme(proposer, "Raider", "VME_BloodCourt") && P(proposer).greed > 0.4f
                   && (Recently(null, 60, IntegrationEventDefOf.Dominion_ConvoyLost, IntegrationEventDefOf.Dominion_PirateTheft, DominionEventDefOf.Dominion_SettlementLooted));
        }

        public override float Utility(Faction voter, Motion m)
        {
            if (AnyMeme(voter, "Raider", "VME_BloodCourt") || HasPrecept(voter, "VME_Raiding_Honorable")) return -1f;
            float u = P(voter).greed * 0.3f + P(voter).honour * 0.3f - P(voter).aggression * 0.3f;
            if (Meme(voter, "VME_Trader") || HasPrecept(voter, "VME_Raiding_Abhorrent")) u += 0.4f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_Raiding".Translate();

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            Faction raider = null;
            if (ev.def == IntegrationEventDefOf.Dominion_ConvoyLost || ev.def == DominionEventDefOf.Dominion_SettlementLooted) raider = ev.secondary;
            else if (ev.def == IntegrationEventDefOf.Dominion_PirateTheft) raider = ev.primary;
            if (raider != null && CouncilUtility.IsMember(raider)) CouncilSystem.Violate(d, raider);
        }
    }

    /// <summary>Members open to each other's culture: opinion warms, and your broadcasts reach further.</summary>
    public class CouncilMotion_CulturalExchange : CouncilMotionWorkerEx
    {
        public override string InvalidReason(List<Faction> t) => base.InvalidReason(t) ?? AlreadyInForce();

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return CouncilUtility.ActiveDecree(def) == null && P(proposer).paranoia < 0.4f && Rand.Chance(0.4f);
        }

        public override float Utility(Faction voter, Motion m)
        {
            float u = S(voter).Bloc(Bloc.Reformists) * 0.5f + (1f - P(voter).paranoia) * 0.4f - 0.25f;
            if (AnyMeme(voter, "VFEA_Isolationist", "VME_Anonymity", "Supremacist")) u -= 0.6f;
            if (AnyMeme(voter, "VME_Cultured", "VME_Egalitarian", "VME_Trader", "VME_PartyLife")) u += 0.3f;
            return u;
        }

        public override string DutyFor(Motion m, Faction f) => "Dominion_Duty_Culture".Translate();

        public override void DailyCheck(Decree d)
        {
            List<Faction> members = CouncilUtility.Members().ToList();
            foreach (Faction a in members)
            {
                if (a.IsPlayer) continue;
                foreach (Faction b in members)
                {
                    if (a == b) continue;
                    RelationState r = Comp.TryGetRelation(a, b);
                    if (r == null) continue;
                    if (r.favour < 15f) r.favour += 0.15f;
                    if (!d.InGrace && r.border == BorderStatus.Closed && !a.HostileTo(b) && !CouncilUtility.Quarantined(b)) CouncilSystem.Violate(d, a);
                }
                if (CouncilUtility.IsMember(Faction.OfPlayer)) S(a).playerCulture = Mathf.Min(100f, S(a).playerCulture + 0.05f);
            }
        }
    }
}

namespace Dominion
{
    /// <summary>Tells the IRBM integration which factions a non-proliferation decree forbids from launching.</summary>
    [StaticConstructorOnStartup]
    public static class CouncilIntegrationLinks
    {
        static CouncilIntegrationLinks()
        {
            Integration_IRBM.NonProliferationBinds = CouncilUtility.BindsNonProliferation;
        }
    }
}
