using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    public static class CouncilUtility
    {
        /// <summary>
        /// Members: humanlike factions at or above the tech threshold (tribals out by default), excluding outlaws.
        /// The player joins when above the threshold or after researching Diplomatic Corps. No one has a veto.
        /// </summary>
        public static bool IsMember(Faction f)
        {
            if (f == null || f.defeated || WorldComponent_Dominion.Instance.Council.disbanded) return false;
            if (Expelled(f)) return false;
            if (f.IsPlayer)
                return f.def.techLevel >= DominionMod.Settings.councilMinTechLevel || DominionResearchDefOf.Dominion_DiplomaticCorps.IsFinished;
            return DominionScheduler.IsSimulated(f) && !f.def.permanentEnemy && (f.def.techLevel >= DominionMod.Settings.councilMinTechLevel || Admitted(f));
        }

        /// <summary>A faction below the tech threshold that the council voted in.</summary>
        public static bool Admitted(Faction f) => Named(CouncilMotionDefOf.Dominion_Motion_AdmitMember, f);

        /// <summary>A member suspended by an expulsion decree: no seat, no vote, no protection while it holds.</summary>
        public static bool Expelled(Faction f) => Named(CouncilMotionDefOf.Dominion_Motion_ExpelMember, f);

        /// <summary>Whether a decree of this kind in force names the faction as its first target.</summary>
        public static bool Named(CouncilMotionDef def, Faction f)
        {
            if (def == null || f == null) return false;
            List<Decree> decrees = WorldComponent_Dominion.Instance.Council.decrees;
            for (int i = 0; i < decrees.Count; i++)
                if (decrees[i].def == def && decrees[i].targets.Count > 0 && decrees[i].targets[0] == f) return true;
            return false;
        }

        // ------------------------------------------------------------------ decrees others ask about

        /// <summary>A non-proliferation decree is in force.</summary>
        public static bool NonProliferationInForce => ActiveDecree(CouncilMotionDefOf.Dominion_Motion_NonProliferation) != null;

        /// <summary>The faction is bound not to use missiles: a member while non-proliferation is in force.</summary>
        public static bool BindsNonProliferation(Faction f) => f != null && NonProliferationInForce && IsMember(f);

        /// <summary>A quarantine decree is in force against the faction: plagues do not spread from it to members.</summary>
        public static bool Quarantined(Faction f) => Named(CouncilMotionDefOf.Dominion_Motion_Quarantine, f);

        // ------------------------------------------------------------------ blocs

        /// <summary>A voting bloc: members close in ideology and alliances, who tend to vote together.</summary>
        public class VotingBloc
        {
            public List<Faction> members = new List<Faction>();
            public string label;
            public Color color;
        }

        private static readonly Color[] BlocColors =
        {
            new Color(0.85f, 0.55f, 0.25f), new Color(0.35f, 0.6f, 0.9f), new Color(0.65f, 0.4f, 0.85f),
            new Color(0.3f, 0.75f, 0.6f), new Color(0.85f, 0.35f, 0.5f), new Color(0.75f, 0.75f, 0.35f),
        };

        private static readonly UICache<List<VotingBloc>> blocCache = new UICache<List<VotingBloc>>(ComputeBlocs, 5f);

        /// <summary>The blocs of the current members (two or more members each), recomputed every few seconds.</summary>
        public static List<VotingBloc> Blocs => blocCache.Value;

        public static VotingBloc BlocOf(Faction f)
        {
            foreach (VotingBloc b in Blocs)
                if (b.members.Contains(f)) return b;
            return null;
        }

        /// <summary>How close two members are: shared ideology, alliance and goodwill.</summary>
        public static float Affinity(Faction a, Faction b)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState ab = comp.TryGetRelation(a, b), ba = comp.TryGetRelation(b, a);
            float ideo = ((ab?.ideoCompatibility ?? 0f) + (ba?.ideoCompatibility ?? 0f)) / 2f;
            float opinion = ((ab?.opinion ?? 0f) + (ba?.opinion ?? 0f)) / 2f;
            return ideo + opinion / 150f + (a.RelationKindWith(b) == FactionRelationKind.Ally ? 0.5f : 0f) - (a.HostileTo(b) ? 1f : 0f);
        }

        private static List<VotingBloc> ComputeBlocs()
        {
            var blocs = new List<VotingBloc>();
            if (WorldComponent_Dominion.Instance == null) return blocs;
            List<Faction> members = Members().Where(f => !f.IsPlayer).ToList();
            // Join members pairwise when they are close, then take the connected groups.
            var group = new Dictionary<Faction, int>();
            for (int i = 0; i < members.Count; i++) group[members[i]] = i;
            int Root(Faction f)
            {
                int g = group[f];
                while (group[members[g]] != g) g = group[members[g]];
                return g;
            }
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    if (Affinity(members[i], members[j]) > 0.45f)
                    {
                        int a = Root(members[i]), b = Root(members[j]);
                        if (a != b) group[members[b]] = a;
                    }
            foreach (IGrouping<int, Faction> g in members.GroupBy(Root))
            {
                if (g.Count() < 2) continue;
                var bloc = new VotingBloc { members = g.OrderByDescending(f => WorldComponent_Dominion.Instance.GetState(f).militaryStrength).ToList() };
                bloc.color = BlocColors[blocs.Count % BlocColors.Length];
                bloc.label = BlocLabel(bloc.members);
                blocs.Add(bloc);
            }
            return blocs;
        }

        /// <summary>A bloc is named for the government most of its members share, or for its strongest member.</summary>
        private static string BlocLabel(List<Faction> members)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var govs = members.Select(f => comp.GetState(f)).Where(st => st.governmentInitialized).GroupBy(st => st.government).OrderByDescending(x => x.Count()).ToList();
            if (govs.Count > 0 && govs[0].Count() * 2 > members.Count)
                return "Dominion_Bloc_ByGovernment".Translate(DominionUI.GovernmentLabel(govs[0].Key));
            return "Dominion_Bloc_ByLeader".Translate(members[0].Name);
        }

        // ------------------------------------------------------------------ favours

        public static int FavoursOwedToPlayer(Faction f) => WorldComponent_Dominion.Instance.Council.favours.Count(x => x.faction == f && x.owedToPlayer);

        public static int FavoursOwedByPlayer(Faction f) => WorldComponent_Dominion.Instance.Council.favours.Count(x => x.faction == f && !x.owedToPlayer);

        public static void AddFavour(Faction f, bool owedToPlayer, string reason)
        {
            if (f == null || f.IsPlayer) return;
            WorldComponent_Dominion.Instance.Council.favours.Add(new Favour { faction = f, owedToPlayer = owedToPlayer, tick = Find.TickManager.TicksGame, reason = reason });
        }

        /// <summary>Uses up the oldest favour of that kind with the faction. False if there was none.</summary>
        public static bool SpendFavour(Faction f, bool owedToPlayer)
        {
            List<Favour> favours = WorldComponent_Dominion.Instance.Council.favours;
            int i = favours.FindIndex(x => x.faction == f && x.owedToPlayer == owedToPlayer);
            if (i < 0) return false;
            favours.RemoveAt(i);
            return true;
        }

        public static IEnumerable<Faction> Members()
        {
            foreach (Faction f in WorldComponent_Dominion.Instance.Cache.Simulated)
                if (IsMember(f)) yield return f;
            if (IsMember(Faction.OfPlayerSilentFail)) yield return Faction.OfPlayer;
        }

        /// <summary>AI factions that can be named in motions.</summary>
        public static IEnumerable<Faction> KnownFactions() => WorldComponent_Dominion.Instance.Cache.Simulated;

        public static Decree ActiveDecree(CouncilMotionDef def)
        {
            if (def == null) return null;
            foreach (Decree d in WorldComponent_Dominion.Instance.Council.decrees)
                if (d.def == def) return d;
            return null;
        }

        public static int PlayerSlaveCount()
        {
            int n = 0;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome) n += map.mapPawns.SlavesOfColonySpawned.Count;
            return n;
        }

        /// <summary>
        /// Voting weight. Bigger, richer, stronger members weigh more, and allies present at the session add a little.
        /// </summary>
        public static float VoteWeight(Faction f, List<Faction> attending)
        {
            if (f.IsPlayer)
            {
                int colonists = 0;
                foreach (Map map in Find.Maps)
                    if (map.IsPlayerHome) colonists += map.mapPawns.FreeColonistsCount;
                float pw = 1f + colonists / 10f + DiplomacyHelpers.PlayerStrength() / 300f + IntegrationRegistry.Sum(i => i.VoteWeightModifier(f));
                return Mathf.Round(pw * 10f) / 10f;
            }
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            int allies = 0;
            foreach (Faction a in attending)
                if (a != f && a.RelationKindWith(f) == FactionRelationKind.Ally) allies++;
            float w = 1f + s.population / 100f + s.economyIndex / 200f + s.militaryStrength / 300f + allies * 0.25f
                      + IntegrationRegistry.Sum(i => i.VoteWeightModifier(f));
            return Mathf.Round(w * 10f) / 10f;
        }

        /// <summary>An AI member's vote on a motion: +1 yes, −1 no, 0 abstain.</summary>
        public static int AIVote(Faction voter, Motion m)
        {
            float u = AILeaning(voter, m);
            return u > 0.1f ? 1 : u < -0.1f ? -1 : 0;
        }

        /// <summary>How strongly an AI member leans on a motion: above 0.1 yes, below −0.1 no, close to 0 wavering.</summary>
        public static float AILeaning(Faction voter, Motion m)
        {
            float u = m.def.Worker.Utility(voter, m) + m.LobbyFor(voter);
            if (m.sponsor != null && m.sponsor != voter)
            {
                RelationState r = WorldComponent_Dominion.Instance.GetRelation(voter, m.sponsor);
                u += r.opinion / 200f;
                if (voter.RelationKindWith(m.sponsor) == FactionRelationKind.Ally) u += 0.3f;
                if (voter.HostileTo(m.sponsor)) u -= 0.4f;
            }
            if (m.sponsor == voter) u += 1f;
            // Blocs vote together: a fellow member's motion gets the benefit of the doubt.
            if (m.sponsor != null && m.sponsor != voter && !m.sponsor.IsPlayer)
            {
                VotingBloc bloc = BlocOf(voter);
                if (bloc != null && bloc.members.Contains(m.sponsor)) u += 0.2f;
            }
            return u;
        }

        /// <summary>A member close enough to the line that lobbying could tip them.</summary>
        public static bool Wavering(Faction voter, Motion m) => Mathf.Abs(AILeaning(voter, m)) < 0.3f;

        /// <summary>A decree asks the player to act; refusing is a violation.</summary>
        public static void AskPlayer(Decree d, string text, string acceptLabel, Action onAccept)
        {
            var node = new DiaNode(text);
            node.options.Add(new DiaOption(acceptLabel) { action = onAccept, resolveTree = true });
            node.options.Add(new DiaOption("Dominion_Decree_Refuse".Translate())
            {
                action = () => CouncilSystem.Violate(d, Faction.OfPlayer),
                resolveTree = true,
            });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_Decree_DutyLabel".Translate()));
        }

        public static float EconomyBonus(Faction f)
        {
            float sum = 0f;
            foreach (Decree d in WorldComponent_Dominion.Instance.Council.decrees)
                sum += d.def.Worker.EconomyBonus(f, d);
            return sum;
        }

        public static float TradeBonus(Faction a, Faction b)
        {
            float sum = 0f;
            foreach (Decree d in WorldComponent_Dominion.Instance.Council.decrees)
                sum += d.def.Worker.TradeBonus(a, b, d);
            return sum;
        }
    }
}
