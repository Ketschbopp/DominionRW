using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// What a peace settles. The side ahead by <see cref="DecisiveScore"/> or more dictates terms: the attacker gets its
    /// war goal, a winning defender gets reparations. Below that it is a white peace and occupied settlements go back.
    /// A capitulation always gives the winner its terms, and the occupier keeps what it holds.
    /// </summary>
    public static class PeaceTerms
    {
        public const float DecisiveScore = 25f;

        /// <summary>What peace would bring now, without doing anything. <paramref name="loser"/> forces a capitulation by that side.</summary>
        public static string Describe(War w, Faction loser = null)
        {
            var lines = new List<string>();
            Resolve(w, loser, false, lines, out _);
            return string.Join("\n", lines);
        }

        /// <summary>Carries out the terms. Returns the full text; <paramref name="outcome"/> is a short phrase for headlines.</summary>
        public static string Apply(War w, Faction loser, out string outcome)
        {
            var lines = new List<string>();
            Resolve(w, loser, true, lines, out outcome);
            return string.Join("\n", lines);
        }

        private static void Resolve(War w, Faction forcedLoser, bool apply, List<string> lines, out string outcome)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            bool capitulation = forcedLoser != null;
            Faction winner = capitulation ? w.Other(forcedLoser) : w.Leader();
            Faction loser = winner != null ? w.Other(winner) : null;

            // Occupied settlements between the two.
            WarRegistry reg = comp.Wars;
            foreach (Settlement s in OccupiedBetween(w))
            {
                Faction holder = s.Faction;
                Faction orig = comp.Cache.FactionById(reg.OriginalOwnerOf(s));
                bool keep = winner != null && holder == winner
                            && (capitulation || (w.goal == WarGoal.Conquest && winner == w.attacker) || w.ScoreFor(winner) >= 50f);
                if (keep)
                {
                    lines.Add("Dominion_Terms_KeepsSettlement".Translate(holder.Name, s.Label));
                    if (apply) WarUtility.Cede(s, holder);
                }
                else if (orig != null && !orig.defeated)
                {
                    lines.Add("Dominion_Terms_ReturnsSettlement".Translate(holder.Name, s.Label, orig.Name));
                    if (apply) WarUtility.ReturnSettlement(s, orig);
                }
            }

            if (winner == null)
            {
                outcome = "Dominion_Terms_Outcome_White".Translate();
                lines.Insert(0, "Dominion_Terms_WhitePeace".Translate());
                return;
            }

            lines.Insert(0, (capitulation ? "Dominion_Terms_Capitulation" : "Dominion_Terms_Victory").Translate(winner.Name, loser.Name));
            if (winner == w.attacker && w.alliedTo == null)
                outcome = GoalTerms(w, winner, loser, apply, lines);
            else
            {
                // A defender who wins, or a faction that fought for an ally, takes reparations.
                outcome = "Dominion_Terms_Outcome_Reparations".Translate();
                lines.Add("Dominion_Terms_Reparations".Translate(loser.Name, winner.Name));
                if (apply)
                {
                    string paid = WarUtility.Pay(loser, winner);
                    if (paid != null) lines.Add(paid);
                    if (!loser.IsPlayer)
                    {
                        FactionState ls = comp.GetState(loser);
                        ls.legitimacy = Mathf.Max(0f, ls.legitimacy - 10f);
                    }
                    else comp.Player.AddInfluence(-15f);
                }
            }

            if (apply)
            {
                if (!winner.IsPlayer)
                {
                    FactionState ws = comp.GetState(winner);
                    ws.legitimacy = Mathf.Min(100f, ws.legitimacy + 6f);
                    ws.stability = Mathf.Min(100f, ws.stability + 4f);
                }
                if (!loser.IsPlayer)
                {
                    FactionState ls = comp.GetState(loser);
                    ls.stability = Mathf.Max(0f, ls.stability - 5f);
                }
                if (capitulation)
                {
                    // A capitulation buys a longer peace than a negotiated one.
                    int until = Find.TickManager.TicksGame + 60 * GenDate.TicksPerDay;
                    comp.GetRelation(winner, loser).truceUntil = until;
                    comp.GetRelation(loser, winner).truceUntil = until;
                }
            }
        }

        private static string GoalTerms(War w, Faction winner, Faction loser, bool apply, List<string> lines)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            switch (w.goal)
            {
                case WarGoal.Conquest:
                {
                    Settlement target = w.goalSettlement;
                    if (target == null || target.Destroyed || target.Faction != loser)
                        target = loser.IsPlayer ? null : WarUtility.NearestSettlement(loser, DelegationUtility.HomeTile(winner), avoidCapital: true);
                    if (target == null || loser.IsPlayer)
                    {
                        // Nothing to take: they pay instead.
                        lines.Add("Dominion_Terms_ConquestPaid".Translate(loser.Name));
                        if (apply) AddLine(lines, WarUtility.Pay(loser, winner, 1.5f));
                        return "Dominion_Terms_Outcome_Reparations".Translate();
                    }
                    if (winner.IsPlayer)
                    {
                        var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
                        lines.Add("Dominion_Terms_CededToPlayer".Translate(target.Label));
                        if (apply && (ftv == null || !ftv.TryCedeToPlayer(target)))
                        {
                            lines.Add("Dominion_Terms_CedeFailed".Translate(loser.Name));
                            AddLine(lines, WarUtility.Pay(loser, winner, 1.5f));
                        }
                        return "Dominion_Terms_Outcome_Ceded".Translate(target.Label);
                    }
                    if (comp.Cache.SettlementsOf(loser).Count < 2)
                    {
                        lines.Add("Dominion_Terms_ConquestPaid".Translate(loser.Name));
                        if (apply) AddLine(lines, WarUtility.Pay(loser, winner, 1.5f));
                        return "Dominion_Terms_Outcome_Reparations".Translate();
                    }
                    lines.Add("Dominion_Terms_Ceded".Translate(target.Label, winner.Name));
                    if (apply) WarUtility.Cede(target, winner);
                    return "Dominion_Terms_Outcome_Ceded".Translate(target.Label);
                }

                case WarGoal.Vassalisation:
                    if (loser.IsPlayer)
                    {
                        lines.Add("Dominion_Terms_PlayerTributary".Translate(winner.Name));
                        if (apply) comp.Treaties.Sign(DominionTreatyDefOf.Dominion_Tribute, loser, winner, winner);
                        return "Dominion_Terms_Outcome_Tribute".Translate();
                    }
                    lines.Add("Dominion_Terms_Puppet".Translate(loser.Name, winner.Name));
                    if (apply) comp.Treaties.Sign(DominionTreatyDefOf.Dominion_Puppet, loser, winner, winner);
                    return "Dominion_Terms_Outcome_Puppet".Translate();

                case WarGoal.Tribute:
                    lines.Add("Dominion_Terms_Tribute".Translate(loser.Name, winner.Name));
                    if (apply)
                    {
                        AddLine(lines, WarUtility.Pay(loser, winner));
                        comp.Treaties.Sign(DominionTreatyDefOf.Dominion_Tribute, loser, winner, winner);
                    }
                    return "Dominion_Terms_Outcome_Tribute".Translate();

                case WarGoal.RegimeChange:
                    if (loser.IsPlayer)
                    {
                        lines.Add("Dominion_Terms_PlayerHumbled".Translate(winner.Name));
                        if (apply) comp.Player.AddInfluence(-30f);
                        return "Dominion_Terms_Outcome_Humbled".Translate();
                    }
                    lines.Add("Dominion_Terms_RegimeChange".Translate(loser.Name));
                    if (apply)
                    {
                        EventBus.Raise(DominionEventDefOf.Dominion_Coup, loser, winner, detail: winner.Name);
                        // The new regime owes its position to the winner.
                        RelationState view = comp.GetRelation(loser, winner);
                        view.grievance = 0f;
                        view.favour = Mathf.Max(view.favour, 20f);
                    }
                    return "Dominion_Terms_Outcome_RegimeChange".Translate();

                case WarGoal.Liberation:
                {
                    int freed = 0;
                    WarRegistry reg = comp.Wars;
                    foreach (Settlement s in new List<Settlement>(comp.Cache.SettlementsOf(loser)))
                    {
                        Faction orig = comp.Cache.FactionById(reg.OriginalOwnerOf(s));
                        if (orig == null || orig == loser || orig.defeated) continue;
                        freed++;
                        lines.Add("Dominion_Terms_ReturnsSettlement".Translate(loser.Name, s.Label, orig.Name));
                        if (apply) WarUtility.ReturnSettlement(s, orig);
                    }
                    if (freed == 0)
                    {
                        lines.Add("Dominion_Terms_Punitive".Translate(loser.Name));
                        if (apply) Punish(loser);
                    }
                    return "Dominion_Terms_Outcome_Liberation".Translate();
                }

                default:
                    lines.Add("Dominion_Terms_Punitive".Translate(loser.Name));
                    if (apply) Punish(loser);
                    return "Dominion_Terms_Outcome_Punitive".Translate();
            }
        }

        private static void Punish(Faction loser)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (loser.IsPlayer)
            {
                comp.Player.AddInfluence(-20f);
                return;
            }
            FactionState s = comp.GetState(loser);
            s.stability = Mathf.Max(0f, s.stability - 12f);
            s.legitimacy = Mathf.Max(0f, s.legitimacy - 10f);
            s.economyIndex = Mathf.Max(10f, s.economyIndex * 0.92f);
        }

        private static void AddLine(List<string> lines, string line)
        {
            if (!line.NullOrEmpty()) lines.Add(line);
        }

        /// <summary>A war that ended outside Dominion (a quest, another mod): occupied settlements simply go back.</summary>
        public static void ReturnOccupied(War w)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Settlement s in OccupiedBetween(w))
            {
                Faction orig = comp.Cache.FactionById(comp.Wars.OriginalOwnerOf(s));
                if (orig != null && !orig.defeated) WarUtility.ReturnSettlement(s, orig);
            }
        }

        /// <summary>Settlements one side of the war took from the other and still holds.</summary>
        public static List<Settlement> OccupiedBetween(War w)
        {
            var list = new List<Settlement>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            WarRegistry reg = comp.Wars;
            if (reg.originalOwner.Count == 0) return list;
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || !reg.originalOwner.TryGetValue(s.ID, out int origId)) continue;
                Faction orig = comp.Cache.FactionById(origId);
                if ((s.Faction == w.attacker && orig == w.defender) || (s.Faction == w.defender && orig == w.attacker))
                    list.Add(s);
            }
            return list;
        }
    }
}
