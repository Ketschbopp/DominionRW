using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A treaty being drafted at the signing desk: its term, what the player puts on the table, the odds the other side
    /// signs, and the counter-offer it may make instead. Also used to renew or amend a treaty already in force.
    /// </summary>
    public class TreatyDraft
    {
        public static readonly float[] TermMultipliers = { 0.5f, 1f, 2f };
        public static readonly int[] SweetenerOptions = { 0, 250, 500, 1000 };
        public static readonly int[] PressOptions = { 0, 10, 20 };

        public readonly TreatyDef def;
        public readonly Faction faction;

        /// <summary>The treaty being renewed or amended, or null for a new one.</summary>
        public readonly Treaty renewing;

        public int term = 1;
        public int sweetener;
        public int press;

        /// <summary>Extra silver the other side asked for in its counter-offer (0: none).</summary>
        public int counterSilver;

        public TreatyDraft(TreatyDef def, Faction faction, Treaty renewing = null)
        {
            this.def = def;
            this.faction = faction;
            this.renewing = renewing;
            if (renewing != null && def.Expires && renewing.termDays > 0)
            {
                // Start from the term they have now.
                float m = renewing.termDays / (float)def.durationDays;
                term = m < 0.75f ? 0 : m > 1.5f ? 2 : 1;
            }
        }

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        private static DiplomacyActionDef ProposeAction => DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposeTreaty");

        public bool Renewal => renewing != null;

        public int Days => def.Expires ? Mathf.Max(1, Mathf.RoundToInt(def.durationDays * TermMultipliers[term])) : 0;

        /// <summary>Influence spent when the draft is sent, whatever the answer.</summary>
        public float InfluenceCost
        {
            get
            {
                float treaty = Renewal ? def.influenceCost * 0.5f : def.influenceCost;
                return (ProposeAction?.influenceCost ?? 0f) + treaty + press;
            }
        }

        public int SilverOffered => sweetener + counterSilver;

        /// <summary>How much their greed makes silver count.</summary>
        private float GreedFactor => 1f + Comp.GetState(faction).personality.greed * 0.6f;

        private float SilverBonus(int silver) => Mathf.Min(0.35f, silver / 2000f * GreedFactor);

        /// <summary>Why the draft cannot be sent now, or null.</summary>
        public string CannotSendReason()
        {
            if (faction == null || faction.defeated) return "Dominion_Action_Invalid".Translate();
            DiplomacyActionDef action = ProposeAction;
            if (action != null && !Renewal)
            {
                string why = DiplomacyActions.CannotUseReason(action, faction);
                if (why != null) return why;
            }
            else if (LeadershipUtility.BestDesk() == null && !LeadershipUtility.HasComms())
                return "Dominion_Action_NeedsComms".Translate();
            if (faction.HostileTo(Faction.OfPlayer)) return "Dominion_Action_NotWhileHostile".Translate();
            if (!Renewal)
            {
                string why = Comp.Treaties.CannotSignReason(def, Faction.OfPlayer, faction);
                if (why != null) return why;
                if (def.requiredResearch != null && !def.requiredResearch.IsFinished)
                    return "Dominion_Action_NeedsResearch".Translate(def.requiredResearch.label);
            }
            else if (!Comp.Treaties.All.Contains(renewing)) return "Dominion_Treaty_NoLongerInForce".Translate();
            if (Comp.Player.influence < InfluenceCost) return "Dominion_Action_NeedsInfluence".Translate(InfluenceCost.ToString("F0"));
            if (SilverOffered > 0 && DiplomacyHelpers.AvailableSilver() < SilverOffered) return "Dominion_Action_NeedsSilver".Translate(SilverOffered);
            return null;
        }

        /// <summary>The chance they sign as drafted, with the reasons in <paramref name="explain"/>.</summary>
        public float Chance(StringBuilder explain = null)
        {
            float baseChance = DiplomacyHelpers.TreatyAcceptance(def, faction, explain);
            if (baseChance <= 0f) return 0f;
            float termTerm = !def.Expires ? 0f : term == 0 ? 0.06f : term == 2 ? -0.08f : 0f;
            float silver = SilverBonus(SilverOffered);
            float pressing = press * 0.004f;
            float renewal = Renewal ? 0.12f : 0f;
            if (explain != null)
            {
                DominionPaper.Term(explain, termTerm, term == 0 ? "Dominion_Odds_ShortTerm".Translate() : "Dominion_Odds_LongTerm".Translate());
                DominionPaper.Term(explain, silver, "Dominion_Odds_Silver".Translate(SilverOffered));
                DominionPaper.Term(explain, pressing, "Dominion_Odds_Press".Translate(press));
                DominionPaper.Term(explain, renewal, "Dominion_Odds_Renewal".Translate());
            }
            return Mathf.Clamp(baseChance + termTerm + silver + pressing + renewal, 0.02f, 0.95f);
        }

        public string Breakdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Dominion_Odds_Title".Translate(faction.Name));
            float c = Chance(sb);
            sb.AppendLine();
            sb.Append("Dominion_Odds_Total".Translate(c.ToStringPercent()));
            return sb.ToString();
        }

        /// <summary>
        /// Their answer to the draft: sign, refuse, or (when it was close) ask for more silver. Influence is spent and
        /// the cooldown starts either way.
        /// </summary>
        public SealCeremony.Stage Decide()
        {
            float chance = Chance();
            Spend();
            float roll = Rand.Value;
            if (roll < chance)
            {
                Conclude();
                return SealCeremony.Stage.Accepted;
            }
            // Close enough to haggle, and the colony could pay: they name a price.
            float gap = roll - chance;
            if (counterSilver == 0 && gap < 0.22f && chance > 0f)
            {
                float perSilver = GreedFactor / 2000f;
                int ask = Mathf.CeilToInt((gap + 0.03f) / perSilver / 50f) * 50;
                if (SilverBonus(SilverOffered + ask) - SilverBonus(SilverOffered) >= gap + 0.02f
                    && SilverOffered + ask <= DiplomacyHelpers.AvailableSilver() && SilverOffered + ask <= 3000)
                {
                    counterSilver = ask;
                    return SealCeremony.Stage.Countered;
                }
            }
            Refuse();
            return SealCeremony.Stage.Refused;
        }

        /// <summary>The player accepts their counter-offer: signed, for the silver they asked.</summary>
        public bool AcceptCounter()
        {
            if (DiplomacyHelpers.AvailableSilverFresh() < SilverOffered) return false;
            Conclude();
            return true;
        }

        public void DeclineCounter()
        {
            counterSilver = 0;
            Refuse();
        }

        private bool spent;

        private void Spend()
        {
            if (spent) return;
            spent = true;
            PlayerState player = Comp.Player;
            player.AddInfluence(-InfluenceCost);
            DiplomacyActionDef action = ProposeAction;
            if (action != null && action.cooldownDays > 0 && !Renewal)
                player.SetCooldown(action.defName, faction.loadID, Find.TickManager.TicksGame + action.cooldownDays * GenDate.TicksPerDay);
        }

        private void Conclude()
        {
            int silver = SilverOffered;
            if (silver > 0)
            {
                int taken = DiplomacyHelpers.TakeSilver(silver);
                EconomyEffects.Receive(faction, taken);
            }
            string text;
            if (Renewal)
            {
                Comp.Treaties.Renew(renewing, Days);
                text = "Dominion_Treaty_Renewed".Translate(faction.Name, def.label, Days);
            }
            else
            {
                Comp.Treaties.Sign(def, Faction.OfPlayer, faction, Faction.OfPlayer, Days);
                text = "Dominion_Treaty_Accepted".Translate(faction.Name, def.label);
            }
            if (silver > 0) text += " " + "Dominion_Treaty_SilverPaid".Translate(silver);
            Messages.Message(text, MessageTypeDefOf.PositiveEvent, historical: true);
            Comp.Player.Log(faction, text, 1);
        }

        private void Refuse()
        {
            Comp.GetRelation(faction, Faction.OfPlayer).favour -= 2f;
            string text = (Renewal ? "Dominion_Treaty_RenewRefused" : "Dominion_Treaty_Refused").Translate(faction.Name, def.label);
            Messages.Message(text, MessageTypeDefOf.NegativeEvent, historical: true);
            Comp.Player.Log(faction, text, -1);
        }

        // ------------------------------------------------------------------ wording

        /// <summary>The articles of a treaty, in plain words, from what the def does.</summary>
        public static List<string> Provisions(TreatyDef def, Faction a, Faction b)
        {
            var lines = new List<string>();
            if (def.alliance) lines.Add("Dominion_Provision_Alliance".Translate());
            if (def.nonAggression) lines.Add("Dominion_Provision_NonAggression".Translate());
            if (def.defensive) lines.Add("Dominion_Provision_Defensive".Translate());
            if (def.tradeBonus != 0f) lines.Add("Dominion_Provision_Trade".Translate(def.tradeBonus.ToStringPercent()));
            if (def.intelBonus != 0f) lines.Add("Dominion_Provision_Intel".Translate(def.intelBonus.ToString("F0")));
            if (def.opinionBonus != 0f) lines.Add("Dominion_Provision_Opinion".Translate(def.opinionBonus.ToStringWithSign("F0")));
            if (def.economyBonus != 0f) lines.Add("Dominion_Provision_Economy".Translate(def.economyBonus.ToStringPercent()));
            if (def.tribute != 0f) lines.Add("Dominion_Provision_Tribute".Translate(a?.Name ?? "?", b?.Name ?? "?"));
            if (def.puppet) lines.Add("Dominion_Provision_Puppet".Translate(a?.Name ?? "?", b?.Name ?? "?"));
            if (def.federation) lines.Add("Dominion_Provision_Federation".Translate());
            if (lines.Count == 0 && !def.description.NullOrEmpty()) lines.Add(def.description.CapitalizeFirst());
            return lines;
        }

        /// <summary>What ending a treaty early costs, for the "tear it up" confirmation.</summary>
        public static string TearUpCost(Treaty t, Faction by)
        {
            Faction other = t.Other(by);
            if (t.def.imposed && by == t.a) return "Dominion_TearUp_Violation".Translate(other.Name);
            string text = "Dominion_TearUp_Grievance".Translate(other.Name);
            if (t.def.alliance) text += "\n" + "Dominion_TearUp_Alliance".Translate(other.Name);
            if (t.def.federation) text += "\n" + "Dominion_TearUp_Federation".Translate(other.Name);
            if (t.def.tribute != 0f && by == t.b) text += "\n" + "Dominion_TearUp_ForgiveTribute".Translate(other.Name);
            return text;
        }

        public static string TermLabel(TreatyDef def, int term) =>
            ("Dominion_Term_" + term).Translate(Mathf.RoundToInt(def.durationDays * TermMultipliers[term]));
    }
}
