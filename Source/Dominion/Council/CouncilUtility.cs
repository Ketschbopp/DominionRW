using System;
using System.Collections.Generic;
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
            if (f == null || f.defeated) return false;
            if (f.IsPlayer)
                return f.def.techLevel >= DominionMod.Settings.councilMinTechLevel || DominionResearchDefOf.Dominion_DiplomaticCorps.IsFinished;
            return DominionScheduler.IsSimulated(f) && !f.def.permanentEnemy && f.def.techLevel >= DominionMod.Settings.councilMinTechLevel;
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
