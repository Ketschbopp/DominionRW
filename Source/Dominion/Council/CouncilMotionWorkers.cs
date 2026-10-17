using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>What a motion does: who it may target, how members vote on it, and how its decree is enforced.</summary>
    public abstract class CouncilMotionWorker
    {
        public CouncilMotionDef def;

        /// <summary>Why these targets are not valid for this motion, or null.</summary>
        public virtual string InvalidReason(List<Faction> targets)
        {
            if (targets.Count != def.targetCount) return "Dominion_Council_WrongTargets".Translate();
            if (def.targetCount > 0 && targets.Any(t => t == null || t.defeated)) return "Dominion_Council_WrongTargets".Translate();
            return null;
        }

        /// <summary>Target lists the player can choose from when proposing.</summary>
        public virtual IEnumerable<List<Faction>> PlayerTargetOptions()
        {
            if (def.targetCount == 0)
            {
                yield return new List<Faction>();
                yield break;
            }
            foreach (Faction f in CouncilUtility.KnownFactions())
                if (!f.IsPlayer && InvalidReason(new List<Faction> { f }) == null)
                    yield return new List<Faction> { f };
        }

        /// <summary>Whether an AI member wants to propose this, and against whom.</summary>
        public abstract bool AIWants(Faction proposer, out List<Faction> targets);

        /// <summary>A member's leaning: above 0.1 votes yes, below −0.1 votes no, between abstains.</summary>
        public abstract float Utility(Faction voter, Motion m);

        /// <summary>
        /// What happens if the motion carries and if it fails, for the chamber and the ballot: the def's own text, then
        /// what it would ask of <paramref name="forWhom"/> (normally the player), then how it would sit with their record.
        /// </summary>
        public virtual string Consequences(Motion m, Faction forWhom)
        {
            string a = m.targets.Count > 0 ? m.targets[0]?.Name ?? "?" : "";
            string b = m.targets.Count > 1 ? m.targets[1]?.Name ?? "?" : "";
            var sb = new System.Text.StringBuilder();
            if (!def.ifPassed.NullOrEmpty()) sb.AppendLine("Dominion_Council_IfPassed".Translate(string.Format(def.ifPassed, a, b)));
            string duty = forWhom != null ? DutyFor(m, forWhom) : null;
            if (!duty.NullOrEmpty()) sb.AppendLine("Dominion_Council_YourDuty".Translate(duty));
            if (def.durationDays > 0 || def.permanent)
                sb.AppendLine(def.permanent ? "Dominion_Council_LastsForever".Translate().ToString() : "Dominion_Council_LastsDays".Translate(def.durationDays, def.graceDays).ToString());
            if (!def.ifFailed.NullOrEmpty()) sb.AppendLine("Dominion_Council_IfFailed".Translate(string.Format(def.ifFailed, a, b)));
            if (forWhom != null && CouncilUtility.IsMember(forWhom))
            {
                int v = Violations(forWhom);
                if (v > 0) sb.AppendLine("Dominion_Council_YourRecord".Translate(v, CouncilSystem.PenaltyLabel(CouncilSystem.NextPenalty(forWhom))));
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>What the motion would ask of this member if it carried, or null if nothing.</summary>
        public virtual string DutyFor(Motion m, Faction f) => null;

        public virtual void OnPassed(Decree d) { }

        public virtual void DailyCheck(Decree d) { }

        public virtual void OnEvent(Decree d, DominionEvent ev) { }

        public virtual void OnExpired(Decree d) { }

        /// <summary>Economy index change for a faction while the decree holds (0.02 = +2%).</summary>
        public virtual float EconomyBonus(Faction f, Decree d) => 0f;

        /// <summary>Price bonus between two factions while the decree holds.</summary>
        public virtual float TradeBonus(Faction a, Faction b, Decree d) => 0f;

        protected static float Op(Faction from, Faction to) => WorldComponent_Dominion.Instance.GetRelation(from, to).opinion;

        protected static AIPersonality P(Faction f) => WorldComponent_Dominion.Instance.GetState(f).personality;

        protected static bool Ally(Faction a, Faction b) => a.RelationKindWith(b) == FactionRelationKind.Ally;

        protected static int Violations(Faction f) => WorldComponent_Dominion.Instance.Council.ViolationsBy(f);
    }

    public class CouncilMotion_Sanctions : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? (t[0].def.permanentEnemy ? (string)"Dominion_Council_NotForOutlaws".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction worst = CouncilUtility.KnownFactions()
                .Where(f => f != proposer && !f.def.permanentEnemy && (Op(proposer, f) < -40f || (Violations(f) > 0 && Op(proposer, f) < 0f)))
                .OrderBy(f => Op(proposer, f)).FirstOrDefault();
            targets = worst != null ? new List<Faction> { worst } : null;
            return worst != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -2f;
            return -Op(voter, x) / 100f + (voter.HostileTo(x) ? 0.4f : 0f) - (Ally(voter, x) ? 1f : 0f)
                   - P(voter).greed * 0.3f + Violations(x) * 0.2f - 0.1f;
        }

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            if (CouncilUtility.IsMember(Faction.OfPlayer) && !x.IsPlayer)
                DominionLetters.Send("Dominion_Decree_DutyLabel".Translate(), "Dominion_Decree_Sanctions_Duty".Translate(x.Name, d.def.graceDays),
                    LetterDefOf.NeutralEvent, null, x, important: true);
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || d.violators.Contains(m)) continue;
                RelationState r = comp.GetRelation(m, x);
                if (r.embargo || m.HostileTo(x))
                {
                    if (!d.complied.Contains(m)) d.complied.Add(m);
                    continue;
                }
                if (!m.IsPlayer && d.InGrace && Rand.Chance(0.1f + P(m).honour * 0.4f))
                {
                    r.embargo = true;
                    d.complied.Add(m);
                    d.imposed.Add(m);
                    EventBus.Raise(DominionEventDefOf.Dominion_EmbargoDeclared, m, x);
                }
                else if (!d.InGrace)
                    CouncilSystem.Violate(d, m);
            }
        }

        public override void OnExpired(Decree d)
        {
            // Embargoes AI members imposed for the decree end with it; embargoes they already had stay.
            Faction x = d.targets[0];
            foreach (Faction m in d.imposed)
                if (!m.IsPlayer && !x.IsPlayer && WorldComponent_Dominion.Instance.GetRelation(m, x).embargo)
                {
                    WorldComponent_Dominion.Instance.GetRelation(m, x).embargo = false;
                    EventBus.Raise(DominionEventDefOf.Dominion_EmbargoLifted, m, x);
                }
        }

        public override float EconomyBonus(Faction f, Decree d) => d.targets[0] == f ? -0.05f : 0f;
    }

    public class CouncilMotion_Censure : CouncilMotionWorker
    {
        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            Faction worst = CouncilUtility.KnownFactions()
                .Where(f => f != proposer && !f.def.permanentEnemy && (Violations(f) > 0 || Op(proposer, f) < -30f))
                .OrderBy(f => Op(proposer, f) - Violations(f) * 20f).FirstOrDefault();
            targets = worst != null ? new List<Faction> { worst } : null;
            return worst != null;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -2f;
            return -Op(voter, x) / 80f + Violations(x) * 0.25f - (Ally(voter, x) ? 1f : 0f) - 0.05f;
        }

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || m.IsPlayer) continue;
                comp.GetRelation(m, x).favour -= 15f;
                if (x.IsPlayer) m.TryAffectGoodwillWith(x, -5, canSendMessage: false, canSendHostilityLetter: false);
            }
            if (!x.IsPlayer)
            {
                FactionState s = comp.GetState(x);
                s.legitimacy = Mathf.Max(0f, s.legitimacy - 10f);
                s.stability = Mathf.Max(0f, s.stability - 5f);
            }
        }
    }

    public class CouncilMotion_Ceasefire : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            if (t[0] == t[1] || !t[0].HostileTo(t[1])) return "Dominion_Council_NotAtWar".Translate();
            if (t[0].def.permanentEnemy || t[1].def.permanentEnemy) return "Dominion_Council_NotForOutlaws".Translate();
            return null;
        }

        public override IEnumerable<List<Faction>> PlayerTargetOptions()
        {
            List<Faction> known = CouncilUtility.KnownFactions().ToList();
            known.Add(Faction.OfPlayer);
            for (int i = 0; i < known.Count; i++)
                for (int j = i + 1; j < known.Count; j++)
                {
                    var pair = new List<Faction> { known[i], known[j] };
                    if (InvalidReason(pair) == null) yield return pair;
                }
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = null;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(proposer);
            foreach (Faction f in CouncilUtility.KnownFactions())
            {
                if (f == proposer || f.def.permanentEnemy || !proposer.HostileTo(f)) continue;
                if (comp.GetRelation(proposer, f).AtWar && s.warWeariness > 40f)
                {
                    targets = new List<Faction> { proposer, f };
                    return true;
                }
            }
            if (s.Bloc(Bloc.Doves) < 0.3f) return false;
            List<Faction> members = CouncilUtility.Members().ToList();
            foreach (Faction a in members)
                foreach (Faction b in members)
                    if (a.loadID < b.loadID && a != proposer && b != proposer && !a.def.permanentEnemy && !b.def.permanentEnemy
                        && comp.GetRelation(a, b).AtWar)
                    {
                        targets = new List<Faction> { a, b };
                        return true;
                    }
            return false;
        }

        public override float Utility(Faction voter, Motion m)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(voter);
            if (m.targets.Contains(voter)) return s.warWeariness / 100f - 0.25f;
            float allyBonus = Ally(voter, m.targets[0]) || Ally(voter, m.targets[1]) ? 0.2f : 0f;
            return s.Bloc(Bloc.Doves) * 0.8f - s.Bloc(Bloc.Hawks) * 0.5f + allyBonus;
        }

        public override void OnPassed(Decree d)
        {
            foreach (Faction party in d.targets)
                if (party.IsPlayer)
                    CouncilUtility.AskPlayer(d, "Dominion_Decree_Ceasefire_Ask".Translate(d.targets.First(t => !t.IsPlayer).Name),
                        "Dominion_Decree_Ceasefire_Accept".Translate(), () =>
                        {
                            Faction enemy = d.targets.First(t => !t.IsPlayer);
                            if (enemy.HostileTo(Faction.OfPlayer))
                                DiplomacyUtility.SetRelationKind(Faction.OfPlayer, enemy, FactionRelationKind.Neutral, Faction.OfPlayer, d.def.label);
                            d.complied.Add(Faction.OfPlayer);
                        });
        }

        public override void DailyCheck(Decree d)
        {
            Faction a = d.targets[0], b = d.targets[1];
            if (!a.HostileTo(b)) return;
            foreach (Faction party in d.targets)
            {
                if (party.IsPlayer || d.violators.Contains(party)) continue;
                FactionState s = WorldComponent_Dominion.Instance.GetState(party);
                if (d.InGrace && Rand.Chance(P(party).honour * 0.3f + s.warWeariness / 200f))
                {
                    d.complied.Add(party);
                    DiplomacyUtility.SetRelationKind(a, b, FactionRelationKind.Neutral, party, d.def.label);
                    return;
                }
            }
            if (!d.InGrace)
                foreach (Faction party in d.targets)
                    if (!d.complied.Contains(party)) CouncilSystem.Violate(d, party);
        }

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            // Restarting the war the council stopped is a violation by the aggressor.
            if (ev.def == DominionEventDefOf.Dominion_WarDeclared && d.Targets(ev.primary) && d.Targets(ev.secondary))
                CouncilSystem.Violate(d, ev.primary);
        }
    }

    public class CouncilMotion_CollectiveDefense : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? (t[0].def.permanentEnemy ? (string)"Dominion_Council_NotForOutlaws".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = null;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in CouncilUtility.KnownFactions())
            {
                if (f == proposer || f.def.permanentEnemy) continue;
                RelationState theirs = comp.GetRelation(f, proposer);
                if (theirs.AtWar && theirs.wasAggressor)
                {
                    targets = new List<Faction> { f };
                    return true;
                }
            }
            return false;
        }

        public override float Utility(Faction voter, Motion m)
        {
            Faction x = m.targets[0];
            if (voter == x) return -2f;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState theirs = comp.GetRelation(x, voter);
            float attacked = theirs.AtWar && theirs.wasAggressor ? 1f : 0f;
            return -Op(voter, x) / 100f + P(voter).honour * 0.3f - comp.GetState(voter).warWeariness / 100f
                   - (Ally(voter, x) ? 1f : 0f) + attacked - 0.15f;
        }

        public override void OnPassed(Decree d)
        {
            Faction x = d.targets[0];
            if (CouncilUtility.IsMember(Faction.OfPlayer) && !x.IsPlayer && !x.HostileTo(Faction.OfPlayer))
                CouncilUtility.AskPlayer(d, "Dominion_Decree_Defense_Ask".Translate(x.Name), "Dominion_Decree_Defense_Accept".Translate(x.Name), () =>
                {
                    DiplomacyUtility.SetRelationKind(Faction.OfPlayer, x, FactionRelationKind.Hostile, Faction.OfPlayer, d.def.label);
                    d.complied.Add(Faction.OfPlayer);
                });
        }

        public override void DailyCheck(Decree d)
        {
            Faction x = d.targets[0];
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == x || m.IsPlayer || d.violators.Contains(m) || d.complied.Contains(m)) continue;
                // An embargo counts as standing with the council; war is not the only way to comply.
                if (m.HostileTo(x) || comp.GetRelation(m, x).embargo)
                {
                    d.complied.Add(m);
                    continue;
                }
                // Excused: allies and pact partners of the aggressor, and members already fighting two wars.
                FactionState s = comp.GetState(m);
                if (Ally(m, x) || comp.Treaties.AnyNonAggression(m, x) || s.activeWars >= 2) continue;
                if (d.InGrace)
                {
                    float join = 0.04f + P(m).honour * 0.05f + s.Bloc(Bloc.Hawks) * 0.1f;
                    if (Rand.Chance(join))
                    {
                        d.complied.Add(m);
                        DiplomacyUtility.SetRelationKind(m, x, FactionRelationKind.Hostile, m, d.def.label);
                    }
                    else if (Rand.Chance(0.1f))
                    {
                        comp.GetRelation(m, x).embargo = true;
                        d.complied.Add(m);
                        d.imposed.Add(m);
                        EventBus.Raise(DominionEventDefOf.Dominion_EmbargoDeclared, m, x);
                    }
                }
                else
                    CouncilSystem.Violate(d, m);
            }
        }
    }

    public class CouncilMotion_TradeStandard : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? (CouncilUtility.ActiveDecree(def) != null ? (string)"Dominion_Council_AlreadyInForce".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            return P(proposer).greed > 0.6f && CouncilUtility.ActiveDecree(def) == null;
        }

        public override float Utility(Faction voter, Motion m) => P(voter).greed * 0.8f - 0.25f;

        public override float EconomyBonus(Faction f, Decree d) => CouncilUtility.IsMember(f) ? 0.02f : 0f;

        public override float TradeBonus(Faction a, Faction b, Decree d) =>
            CouncilUtility.IsMember(a) && CouncilUtility.IsMember(b) ? 0.05f : 0f;
    }

    public class CouncilMotion_DiplomaticImmunity : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? (CouncilUtility.ActiveDecree(def) != null ? (string)"Dominion_Council_AlreadyInForce".Translate() : null);

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            if (CouncilUtility.ActiveDecree(def) != null || P(proposer).honour < 0.4f) return false;
            int since = Find.TickManager.TicksGame - 60 * GenDate.TicksPerDay;
            foreach (DominionEvent ev in WorldComponent_Dominion.Instance.EventLog.Events)
                if (ev.tick > since && (ev.def == DominionEventDefOf.Dominion_DelegationIntercepted || ev.def == DominionEventDefOf.Dominion_EnvoyCaptured))
                    return true;
            return false;
        }

        public override float Utility(Faction voter, Motion m) => P(voter).honour * 0.6f + 0.1f - P(voter).aggression * 0.4f;

        public override void OnEvent(Decree d, DominionEvent ev)
        {
            if ((ev.def == DominionEventDefOf.Dominion_DelegationIntercepted || ev.def == DominionEventDefOf.Dominion_EnvoyCaptured) && ev.primary != null)
                CouncilSystem.Violate(d, ev.primary);
        }
    }

    public class CouncilMotion_BanSlavery : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t) =>
            base.InvalidReason(t) ?? (CouncilUtility.ActiveDecree(def) != null ? (string)"Dominion_Council_AlreadyInForce".Translate() : null);

        private static bool Supremacist(Faction f) => GovernmentUtility.HasMeme(f, "Supremacist") || GovernmentUtility.HasMeme(f, "Raider");

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = new List<Faction>();
            if (CouncilUtility.ActiveDecree(def) != null || Supremacist(proposer) || P(proposer).honour < 0.6f) return false;
            return CouncilUtility.PlayerSlaveCount() > 0 || Rand.Chance(0.05f);
        }

        public override float Utility(Faction voter, Motion m) =>
            Supremacist(voter) ? -0.6f : P(voter).honour * 0.4f + (1f - P(voter).greed) * 0.2f - 0.2f;

        public override void OnPassed(Decree d)
        {
            if (CouncilUtility.IsMember(Faction.OfPlayer) && CouncilUtility.PlayerSlaveCount() > 0)
                DominionLetters.Send("Dominion_Decree_DutyLabel".Translate(), "Dominion_Decree_Slavery_Duty".Translate(d.def.graceDays),
                    LetterDefOf.NeutralEvent, important: true);
        }

        public override void DailyCheck(Decree d)
        {
            if (!d.InGrace && CouncilUtility.IsMember(Faction.OfPlayer) && CouncilUtility.PlayerSlaveCount() > 0)
                CouncilSystem.Violate(d, Faction.OfPlayer);
        }
    }
}
