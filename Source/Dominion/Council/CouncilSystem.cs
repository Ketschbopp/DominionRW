using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Rim Council. Every interval a member hosts a session. It is announced in advance, members put motions on
    /// the agenda and send real delegations to the host's capital; only members present vote. Passed motions become
    /// decrees binding every member, and breaking one costs standing with all of them.
    /// </summary>
    public class CouncilSystem : SimSystem
    {
        public override int Order => 45;

        public const int AnnounceLeadDays = 15;
        public const int MaxMotions = 6;

        private static CouncilState State => WorldComponent_Dominion.Instance.Council;

        public override void DailyGlobal()
        {
            CouncilState c = State;
            int now = Find.TickManager.TicksGame;
            int day = GenDate.TicksPerDay;

            // Decrees: compliance and expiry. Decrees still run out when the council is switched off,
            // so their effects do not become permanent.
            for (int i = c.decrees.Count - 1; i >= 0; i--)
            {
                if (i >= c.decrees.Count) continue;
                Decree d = c.decrees[i];
                try
                {
                    if (d.expiresTick >= 0 && now >= d.expiresTick) Expire(d);
                    else if (DominionMod.Settings.councilEnabled) d.def.Worker.DailyCheck(d);
                }
                catch (System.Exception e)
                {
                    DLog.WarningOnce($"Decree {d.def?.defName} failed its daily check: {e}", d.id ^ 0x5D3);
                }
            }

            if (!DominionMod.Settings.councilEnabled) return;

            if (c.nextSessionTick < 0)
                c.nextSessionTick = now + Mathf.Max(AnnounceLeadDays + 5, DominionMod.Settings.councilIntervalDays / 2) * day;

            if (c.phase == CouncilPhase.Idle && now >= c.nextSessionTick - AnnounceLeadDays * day)
                Announce();
        }

        public override void Hourly()
        {
            CouncilState c = State;
            if (c.phase != CouncilPhase.Announced) return;
            if (!DominionMod.Settings.councilEnabled)
            {
                Close(null); // switched off while a session was announced
                return;
            }
            if (Find.TickManager.TicksGame >= c.nextSessionTick)
                OpenSession();
        }

        public override void OnEvent(DominionEvent ev)
        {
            // Runs for every event in the game: no allocation, and a decree may expire or be removed meanwhile.
            List<Decree> decrees = State.decrees;
            for (int i = decrees.Count - 1; i >= 0; i--)
            {
                if (i >= decrees.Count) continue;
                try { decrees[i].def.Worker.OnEvent(decrees[i], ev); }
                catch (System.Exception e) { DLog.WarningOnce($"Decree {decrees[i].def?.defName} failed on {ev.def.defName}: {e}", decrees[i].id ^ 0x5D4); }
            }
        }

        // ---------------------------------------------------------------- announcement

        public static void Announce(int sessionInTicks = -1)
        {
            CouncilState c = State;
            List<Faction> members = CouncilUtility.Members().ToList();
            if (members.Count < 2) return;
            if (sessionInTicks > 0) c.nextSessionTick = Find.TickManager.TicksGame + sessionInTicks;
            // If the announcement was delayed (too few members, no host), delegates still get the full lead time.
            else c.nextSessionTick = Mathf.Max(c.nextSessionTick, Find.TickManager.TicksGame + AnnounceLeadDays * GenDate.TicksPerDay);

            c.host = ChooseHost(members, c.sessionNumber);
            if (c.host == null) return;
            c.phase = CouncilPhase.Announced;
            c.agenda.Clear();
            c.attending.Clear();
            c.playerDelegate = null;
            c.attending.Add(c.host);

            // AI members put motions on the agenda.
            foreach (Faction m in members.InRandomOrder())
            {
                if (m.IsPlayer || c.agenda.Count >= MaxMotions) continue;
                foreach (CouncilMotionDef def in DefDatabase<CouncilMotionDef>.AllDefsListForReading.InRandomOrder())
                {
                    if (!Rand.Chance(def.aiProposeChance) || !def.Worker.AIWants(m, out List<Faction> targets)) continue;
                    if (def.Worker.InvalidReason(targets) != null || OnAgenda(def, targets)) continue;
                    c.agenda.Add(new Motion { def = def, sponsor = m, targets = targets });
                    break;
                }
            }

            // Delegates travel to the host.
            foreach (Faction m in members)
            {
                if (m == c.host || m.IsPlayer) continue;
                Delegation d = DelegationUtility.Dispatch(m, c.host, DelegationPurpose.CouncilDelegate);
                if (d == null) c.attending.Add(m); // no road needed (or none possible): counts as present
            }

            int days = Mathf.Max(1, (c.nextSessionTick - Find.TickManager.TicksGame) / GenDate.TicksPerDay);
            EventBus.Raise(DominionEventDefOf.Dominion_CouncilConvened, c.host, null, detail: days.ToString());
            string how = c.host.IsPlayer ? "Dominion_Council_Announce_PlayerHosts".Translate()
                : CouncilUtility.IsMember(Faction.OfPlayer) ? "Dominion_Council_Announce_Attend".Translate(c.host.Name)
                : "Dominion_Council_Announce_NotMember".Translate();
            Find.LetterStack.ReceiveLetter("Dominion_Council_AnnounceLabel".Translate(),
                "Dominion_Council_AnnounceText".Translate(c.host.Name, days, AgendaText(c)) + "\n\n" + how, LetterDefOf.NeutralEvent, null, c.host);
        }

        private static Faction ChooseHost(List<Faction> members, int session)
        {
            var eligible = members.Where(f => DelegationUtility.HomeTile(f).Valid && (!f.IsPlayer || LeadershipUtility.BestDesk() != null))
                .OrderBy(f => f.loadID).ToList();
            return eligible.Count == 0 ? null : eligible[session % eligible.Count];
        }

        public static bool OnAgenda(CouncilMotionDef def, List<Faction> targets) =>
            State.agenda.Any(m => m.def == def && m.targets.SequenceEqual(targets));

        public static string AgendaText(CouncilState c)
        {
            if (c.agenda.Count == 0) return "Dominion_Council_NoMotions".Translate();
            var sb = new StringBuilder();
            foreach (Motion m in c.agenda)
                sb.AppendLine("- " + m.Summary + " (" + "Dominion_Council_ProposedBy".Translate(m.sponsor?.Name ?? "?") + ")");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The player puts a motion on the agenda.</summary>
        public static string ProposeByPlayer(CouncilMotionDef def, List<Faction> targets)
        {
            CouncilState c = State;
            if (c.phase != CouncilPhase.Announced) return "Dominion_Council_NoSessionAnnounced".Translate();
            if (!CouncilUtility.IsMember(Faction.OfPlayer)) return "Dominion_Council_NotMember".Translate();
            if (c.agenda.Count >= MaxMotions) return "Dominion_Council_AgendaFull".Translate();
            if (OnAgenda(def, targets)) return "Dominion_Council_AlreadyOnAgenda".Translate();
            string invalid = def.Worker.InvalidReason(targets);
            if (invalid != null) return invalid;
            PlayerState p = WorldComponent_Dominion.Instance.Player;
            if (p.influence < def.proposeCost) return "Dominion_Action_NeedsInfluence".Translate(def.proposeCost.ToString("F0"));
            p.AddInfluence(-def.proposeCost);
            c.agenda.Add(new Motion { def = def, sponsor = Faction.OfPlayer, targets = targets });
            return null;
        }

        // ---------------------------------------------------------------- attendance

        public static void DelegateArrived(Faction member)
        {
            CouncilState c = State;
            if (c.phase != CouncilPhase.Announced || c.attending.Contains(member)) return;
            c.attending.Add(member);
            if (c.host != null && c.host.IsPlayer && !member.HostileTo(Faction.OfPlayer))
            {
                // Delegates to a council at your capital walk in and stay until the session.
                int stay = Mathf.Max(GenDate.TicksPerHour * 6, c.nextSessionTick - Find.TickManager.TicksGame + GenDate.TicksPerHour * 6);
                Map map = VisitorUtility.PlayerCapitalMap();
                if (VisitorUtility.CanHostVisitors(map))
                {
                    List<Pawn> party = VisitorUtility.MakeEnvoyParty(member, null, 1);
                    if (!VisitorUtility.SpawnVisitors(member, map, party, stay))
                        foreach (Pawn p in party) if (!p.Spawned && !p.Discarded) p.Discard();
                }
            }
        }

        /// <summary>Is a caravan at the host's capital able to register the player's attendance?</summary>
        public static bool CanRegisterAt(Settlement s) =>
            State.phase == CouncilPhase.Announced && State.host != null && !State.host.IsPlayer && s.Faction == State.host
            && DelegationUtility.HomeTile(State.host) == s.Tile && State.playerDelegate == null && CouncilUtility.IsMember(Faction.OfPlayer);

        public static void RegisterPlayer(Pawn envoy)
        {
            CouncilState c = State;
            c.playerDelegate = envoy;
            if (!c.attending.Contains(Faction.OfPlayer)) c.attending.Add(Faction.OfPlayer);
            Messages.Message("Dominion_Council_Registered".Translate(envoy.LabelShortCap), MessageTypeDefOf.PositiveEvent, historical: true);
        }

        /// <summary>The player's speaker at the session, or null if the player is not present.</summary>
        public static Pawn PlayerSpeaker()
        {
            CouncilState c = State;
            if (!CouncilUtility.IsMember(Faction.OfPlayer) || c.host == null) return null;
            if (c.host.IsPlayer)
            {
                Map map = VisitorUtility.PlayerCapitalMap();
                Pawn hos = LeadershipUtility.HeadOfState();
                if (hos != null && hos.Spawned && hos.Map == map && !hos.Downed) return hos;
                foreach (Pawn p in DiplomatUtility.AllDiplomats())
                    if (p.Spawned && p.Map == map && !p.Downed) return p;
                return null;
            }
            Pawn d = c.playerDelegate;
            return d != null && !d.Dead ? d : null;
        }

        // ---------------------------------------------------------------- session

        private static void OpenSession()
        {
            weightCache.Invalidate();
            CouncilState c = State;
            if (c.host == null || c.host.defeated)
            {
                Close(null);
                return;
            }
            Pawn speaker = PlayerSpeaker();
            if (speaker != null)
            {
                if (!c.attending.Contains(Faction.OfPlayer)) c.attending.Add(Faction.OfPlayer);
                Find.WindowStack.Add(new Dialog_CouncilSession(speaker));
            }
            else
            {
                c.attending.Remove(Faction.OfPlayer);
                Resolve(new Dictionary<Motion, int>());
            }
        }

        /// <summary>Counts the votes, passes decrees, reports, and closes the session.</summary>
        public static void Resolve(Dictionary<Motion, int> playerVotes)
        {
            CouncilState c = State;
            var report = new StringBuilder();
            weightCache.Invalidate();
            foreach (Motion m in c.agenda)
            {
                if (m.def.Worker.InvalidReason(m.targets) != null)
                {
                    report.AppendLine("Dominion_Council_Rejected".Translate().ToString() + " " + m.Summary + "  " + "Dominion_Council_MotionVoid".Translate().ToString());
                    continue;
                }
                Tally(m, playerVotes, out float yes, out float no);
                // A motion needs real support: more yes than no, and yes from at least a third of the weight in the room.
                bool passed = !m.vetoed && yes > no && yes >= 0.34f * TotalWeight() && m.def.Worker.InvalidReason(m.targets) == null;
                string vetoNote = m.vetoed ? " " + "Dominion_Council_Vetoed".Translate().ToString() : "";
                report.AppendLine((passed ? "Dominion_Council_Passed" : "Dominion_Council_Rejected").Translate().ToString() + " " + m.Summary + "  "
                                  + "Dominion_Council_VoteResult".Translate(yes.ToString("F1"), no.ToString("F1")).ToString() + vetoNote);
                if (passed) Pass(m);
                else EventBus.Raise(DominionEventDefOf.Dominion_MotionRejected, m.sponsor, m.targets.FirstOrDefault(), detail: m.Summary);
            }
            Close(report.ToString());
        }

        // The session window tallies every frame; weights barely move during a session.
        private static readonly UICache<Faction, float> weightCache = new UICache<Faction, float>(f => CouncilUtility.VoteWeight(f, State.attending), 2f);

        private static float TotalWeight()
        {
            float total = 0f;
            foreach (Faction f in State.attending) total += weightCache.Get(f);
            return Mathf.Max(0.01f, total);
        }

        public static void Tally(Motion m, Dictionary<Motion, int> playerVotes, out float yes, out float no)
        {
            CouncilState c = State;
            yes = no = 0f;
            foreach (Faction f in c.attending)
            {
                int vote = f.IsPlayer ? (playerVotes.TryGetValue(m, out int v) ? v : 0) : CouncilUtility.AIVote(f, m);
                float w = weightCache.Get(f);
                if (vote > 0) yes += w;
                else if (vote < 0) no += w;
            }
        }

        private static void Pass(Motion m)
        {
            CouncilState c = State;
            int now = Find.TickManager.TicksGame;
            var d = new Decree
            {
                id = c.NewDecreeId(),
                def = m.def,
                targets = new List<Faction>(m.targets),
                passedTick = now,
                expiresTick = m.def.permanent ? -1 : now + m.def.durationDays * GenDate.TicksPerDay,
            };
            // durationDays 0 means the motion acts once (e.g. censure) and leaves no decree behind.
            if (m.def.permanent || m.def.durationDays > 0) c.decrees.Add(d);
            EventBus.Raise(DominionEventDefOf.Dominion_DecreePassed, m.sponsor, m.targets.FirstOrDefault(), detail: m.Summary);
            m.def.Worker.OnPassed(d);
        }

        private static void Close(string report)
        {
            CouncilState c = State;
            int attended = c.attending.Count;
            if (report != null)
                Find.LetterStack.ReceiveLetter("Dominion_Council_ResultsLabel".Translate(),
                    "Dominion_Council_ResultsText".Translate(c.host?.Name ?? "?", attended, report.NullOrEmpty() ? (string)"Dominion_Council_NoMotions".Translate() : report),
                    LetterDefOf.NeutralEvent, null, c.host);
            c.phase = CouncilPhase.Idle;
            c.sessionNumber++;
            c.agenda.Clear();
            c.attending.Clear();
            c.playerDelegate = null;
            c.nextSessionTick = Find.TickManager.TicksGame + DominionMod.Settings.councilIntervalDays * GenDate.TicksPerDay;
        }

        // ---------------------------------------------------------------- decrees

        /// <summary>A member breaks a decree: every other member thinks less of them; the player also loses goodwill.</summary>
        public static void Violate(Decree d, Faction violator)
        {
            if (violator == null || d.violators.Contains(violator) || !CouncilUtility.IsMember(violator)) return;
            d.violators.Add(violator);
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction m in CouncilUtility.Members())
            {
                if (m == violator || m.IsPlayer) continue;
                comp.GetRelation(m, violator).favour -= 6f + 8f * comp.GetState(m).personality.honour;
                if (violator.IsPlayer)
                    m.TryAffectGoodwillWith(violator, -5, canSendMessage: false, canSendHostilityLetter: false);
            }
            if (!violator.IsPlayer)
                comp.GetState(violator).legitimacy = Mathf.Max(0f, comp.GetState(violator).legitimacy - 5f);
            EventBus.Raise(DominionEventDefOf.Dominion_DecreeViolated, violator, d.targets.FirstOrDefault(), detail: d.Summary);
        }

        private static void Expire(Decree d)
        {
            CouncilState c = State;
            c.decrees.Remove(d);
            // Old violations are forgiven over time: each expiry halves the record before adding the new ones.
            foreach (int id in c.pastViolations.Keys.ToList())
                c.pastViolations[id] /= 2;
            foreach (Faction v in d.violators)
                c.pastViolations[v.loadID] = (c.pastViolations.TryGetValue(v.loadID, out int n) ? n : 0) + 1;
            d.def.Worker.OnExpired(d);
            EventBus.Raise(DominionEventDefOf.Dominion_DecreeExpired, null, d.targets.FirstOrDefault(), detail: d.Summary);
        }

        /// <summary>Dev-mode veto, the only veto there is.</summary>
        public static void DevVeto(Motion m) => m.vetoed = true;
    }
}
