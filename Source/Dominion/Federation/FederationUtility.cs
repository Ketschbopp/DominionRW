using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The player's Federation and the unification ending. A major faction counts as unified when it is a
    /// Federation member, when the player holds as many of its former settlements as vassal outposts as it has
    /// left (subjugated), or when it fell while at war with the player (conquered).
    /// </summary>
    public static class FederationUtility
    {
        public static UnificationState State => WorldComponent_Dominion.Instance.Unification;

        public static bool ResearchDone => DominionFederationDefOf.Dominion_PlanetaryFederation.IsFinished;

        /// <summary>
        /// The charter passes at the Rim Council. With the council switched off, finishing the research is enough.
        /// </summary>
        public static bool CharterInForce => State.CharterInForce
            || (ResearchDone && (!DominionMod.Settings.councilEnabled || !CouncilCanMeet));

        /// <summary>The council needs two members to meet; in a mostly tribal world it never can.</summary>
        private static bool CouncilCanMeet
        {
            get
            {
                int n = 0;
                foreach (Faction f in CouncilUtility.Members())
                    if (++n >= 2) return true;
                return false;
            }
        }

        /// <summary>Major factions: visible, humanlike, with a settlement. Outlaws only if the setting says so.</summary>
        public static bool IsMajor(Faction f)
        {
            if (!DominionScheduler.IsSimulated(f)) return false;
            if (f.def.permanentEnemy && !DominionMod.Settings.endingIncludeOutlaws) return false;
            return WorldComponent_Dominion.Instance.Cache.SettlementsOf(f).Count > 0;
        }

        public static IEnumerable<Faction> Majors()
        {
            foreach (Faction f in WorldComponent_Dominion.Instance.Cache.Simulated)
                if (IsMajor(f)) yield return f;
        }

        public static bool IsMember(Faction f) =>
            f != null && WorldComponent_Dominion.Instance.Treaties.Has(Faction.OfPlayer, f, DominionTreatyDefOf.Dominion_FederationMembership);

        public static int MemberCount()
        {
            int n = 0;
            foreach (Treaty t in WorldComponent_Dominion.Instance.Treaties.All)
                if (t.def.federation) n++;
            return n;
        }

        /// <summary>How a living faction stands toward unification right now.</summary>
        public static UnifyPath StatusOf(Faction f)
        {
            if (IsMember(f)) return UnifyPath.Federation;
            // A puppet regime is as good as subjugated.
            if (WarSystem.IsPuppetOf(f, Faction.OfPlayer, WorldComponent_Dominion.Instance)) return UnifyPath.Subjugated;
            RelationState r = WorldComponent_Dominion.Instance.TryGetRelation(f, Faction.OfPlayer);
            int held = r?.outpostsHeldByOther ?? 0;
            if (held > 0 && held >= WorldComponent_Dominion.Instance.Cache.SettlementsOf(f).Count) return UnifyPath.Subjugated;
            return UnifyPath.None;
        }

        public static string CannotJoinReason(Faction f, bool withPlayer)
        {
            if (!withPlayer) return "Dominion_Federation_OnlyPlayer".Translate();
            if (!ResearchDone) return "Dominion_Action_NeedsResearch".Translate(DominionFederationDefOf.Dominion_PlanetaryFederation.label);
            if (!CharterInForce) return "Dominion_Federation_NoCharter".Translate();
            if (!IsMajor(f))
                return "Dominion_Federation_NotEligible".Translate();
            return null;
        }

        /// <summary>
        /// Extra acceptance for joining: factions follow the crowd, like your culture, and weigh how strong you are.
        /// </summary>
        public static float JoinBonus(Faction f)
        {
            int majors = 0, unified = 0;
            foreach (Faction m in Majors())
            {
                majors++;
                if (StatusOf(m) != UnifyPath.None) unified++;
            }
            float crowd = majors > 0 ? 0.3f * unified / majors : 0f;
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            float mine = DiplomacyHelpers.PlayerStrength();
            float power = Mathf.Clamp01(mine / (mine + Mathf.Max(1f, s.militaryStrength)));
            return crowd + s.playerCulture / 400f + (power - 0.5f) * 0.2f;
        }

        public struct Progress
        {
            public int total;
            public int unified;
            public int required;
            public int federation;
            public int subjugated;
            public int conquered;

            public bool Complete => total > 0 && unified >= required;
            public float Fraction => total > 0 ? (float)unified / total : 0f;
        }

        public static Progress Compute()
        {
            var p = new Progress();
            foreach (Faction f in Majors())
            {
                p.total++;
                Count(ref p, StatusOf(f));
            }
            foreach (KeyValuePair<int, UnifyPath> kv in State.fallen)
            {
                if (kv.Value == UnifyPath.None) continue;
                p.total++;
                Count(ref p, kv.Value);
            }
            p.required = Mathf.CeilToInt(p.total * DominionMod.Settings.endingShare - 0.001f);
            return p;
        }

        private static void Count(ref Progress p, UnifyPath path)
        {
            switch (path)
            {
                case UnifyPath.Federation: p.unified++; p.federation++; break;
                case UnifyPath.Subjugated: p.unified++; p.subjugated++; break;
                case UnifyPath.Conquered: p.unified++; p.conquered++; break;
            }
        }

        /// <summary>The path that brought in more than half the factions, or None for a mixed unification.</summary>
        public static UnifyPath DominantPath(Progress p)
        {
            if (p.unified == 0) return UnifyPath.None;
            if (p.federation * 2 > p.unified) return UnifyPath.Federation;
            if (p.subjugated * 2 > p.unified) return UnifyPath.Subjugated;
            if (p.conquered * 2 > p.unified) return UnifyPath.Conquered;
            return UnifyPath.None;
        }

        /// <summary>Proclaims the Federation: news, and the ending credits. The game goes on afterwards.</summary>
        public static void Proclaim()
        {
            UnificationState st = State;
            if (st.proclaimed) return;
            Progress p = Compute();
            st.proclaimed = true;
            st.proclaimedTick = Find.TickManager.TicksGame;
            EventBus.Raise(DominionEventDefOf.Dominion_FederationProclaimed, Faction.OfPlayer, detail: Faction.OfPlayer.Name);

            UnifyPath path = DominantPath(p);
            Pawn head = LeadershipUtility.HeadOfState();
            string leader = head != null ? head.Name.ToStringFull : Faction.OfPlayer.Name;
            string body = ("Dominion_Ending_" + (path == UnifyPath.None ? "Mixed" : path.ToString())).Translate(Faction.OfPlayer.Name, leader);
            string text = "Dominion_Ending_Intro".Translate(Faction.OfPlayer.Name, p.unified, p.federation, p.subjugated, p.conquered)
                          + "\n\n" + body + "\n\n" + "Dominion_Ending_Outro".Translate();
            GameVictoryUtility.ShowCredits(text, SongDefOf.EndCreditsSong, false, 5f);
        }
    }

    [DefOf]
    public static class DominionFederationDefOf
    {
        public static ResearchProjectDef Dominion_PlanetaryFederation;
        public static CouncilMotionDef Dominion_Motion_FederationCharter;

        static DominionFederationDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionFederationDefOf));
        }
    }

    /// <summary>
    /// The Federation Charter: once passed, any major faction may sign Federation membership with the player.
    /// Only the player can propose it, and only after researching Planetary Federation. It never expires.
    /// </summary>
    public class CouncilMotion_FederationCharter : CouncilMotionWorker
    {
        public override string InvalidReason(List<Faction> t)
        {
            string r = base.InvalidReason(t);
            if (r != null) return r;
            if (FederationUtility.State.CharterInForce) return "Dominion_Council_AlreadyInForce".Translate();
            if (!FederationUtility.ResearchDone)
                return "Dominion_Action_NeedsResearch".Translate(DominionFederationDefOf.Dominion_PlanetaryFederation.label);
            return null;
        }

        public override bool AIWants(Faction proposer, out List<Faction> targets)
        {
            targets = null;
            return false;
        }

        public override float Utility(Faction voter, Motion m)
        {
            if (voter.IsPlayer) return 2f;
            AIPersonality p = P(voter);
            return Op(voter, Faction.OfPlayer) / 100f + p.honour * 0.2f - p.paranoia * 0.3f - p.aggression * 0.2f
                   + (Ally(voter, Faction.OfPlayer) ? 0.3f : 0f) - 0.15f;
        }

        public override void OnPassed(Decree d)
        {
            FederationUtility.State.charterTick = Find.TickManager.TicksGame;
            EventBus.Raise(DominionEventDefOf.Dominion_FederationCharter, Faction.OfPlayer);
            Find.LetterStack.ReceiveLetter("Dominion_Federation_CharterLabel".Translate(), "Dominion_Federation_CharterText".Translate(),
                LetterDefOf.PositiveEvent);
        }
    }
}
