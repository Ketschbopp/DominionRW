using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    public class DiplomacyAction_Gift : DiplomacyActionWorker
    {
        public static readonly int[] Amounts = { 100, 250, 500, 1000 };

        public override bool ViaLetter => true;

        public override string DisabledReason(Faction f)
        {
            if (f.HostileTo(Faction.OfPlayer)) return "Dominion_Action_NotWhileHostile".Translate();
            return DiplomacyHelpers.AvailableSilver() < Amounts[0] ? "Dominion_Action_NeedsSilver".Translate(Amounts[0]) : null;
        }

        public override void Execute(Faction f)
        {
            int silver = DiplomacyHelpers.AvailableSilver();
            var options = new List<FloatMenuOption>();
            foreach (int amount in Amounts)
            {
                int a = amount;
                options.Add(amount > silver
                    ? new FloatMenuOption("Dominion_Gift_Amount".Translate(a) + " (" + "Dominion_Action_NeedsSilver".Translate(a) + ")", null)
                    : new FloatMenuOption("Dominion_Gift_Amount".Translate(a), () => Give(f, a)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public void Give(Faction f, int amount)
        {
            int taken = DiplomacyHelpers.TakeSilver(amount);
            if (taken <= 0) return;
            EconomyEffects.Receive(f, taken);
            Commit(f);
            int goodwill = Mathf.Min(15, Mathf.RoundToInt(taken / 40f * ToneFavour));
            f.TryAffectGoodwillWith(Faction.OfPlayer, goodwill, canSendMessage: false, canSendHostilityLetter: false);
            TheirView(f).favour += Mathf.Min(20f, taken / 40f) * ToneFavour;
            Report("Dominion_Gift_Sent".Translate(taken, f.Name, goodwill), true);
        }
    }

    public class DiplomacyAction_Compliment : DiplomacyActionWorker
    {
        public override string DisabledReason(Faction f) =>
            f.HostileTo(Faction.OfPlayer) ? "Dominion_Action_NotWhileHostile".Translate() : null;

        public override bool ViaLetter => true;

        public override float? AcceptanceChance(Faction f) => Clamp(0.6f + LeadershipUtility.AcceptanceBonus() + ToneChance(f));

        public override string ChanceBreakdown(Faction f)
        {
            var sb = new System.Text.StringBuilder();
            DominionPaper.Term(sb, 0.6f, "Dominion_Odds_Base".Translate());
            DominionPaper.Term(sb, LeadershipUtility.AcceptanceBonus(), "Dominion_Odds_Leader".Translate());
            ToneTerm(sb, f);
            return sb.ToString();
        }

        public override void Execute(Faction f)
        {
            Commit(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                TheirView(f).favour += 6f * ToneFavour;
                f.TryAffectGoodwillWith(Faction.OfPlayer, 3, canSendMessage: false, canSendHostilityLetter: false);
                Report("Dominion_Compliment_Success".Translate(f.Name), true);
            }
            else
            {
                TheirView(f).favour -= 2f;
                Report("Dominion_Compliment_Fail".Translate(f.Name), false);
            }
        }
    }

    public class DiplomacyAction_Insult : DiplomacyActionWorker
    {
        public override bool ViaLetter => true;

        public override void Execute(Faction f)
        {
            Commit(f);
            RelationState view = TheirView(f);
            view.favour -= 15f * ToneSting;
            view.grievance += 5f * ToneSting;
            f.TryAffectGoodwillWith(Faction.OfPlayer, -Mathf.RoundToInt(10 * ToneSting), canSendMessage: false, canSendHostilityLetter: true);
            Report("Dominion_Insult_Done".Translate(f.Name), false);
        }
    }

    public class DiplomacyAction_ProposePeace : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => f.HostileTo(Faction.OfPlayer);

        public override string DisabledReason(Faction f) =>
            f.def.permanentEnemy ? "Dominion_Action_PermanentEnemy".Translate() : null;

        public override float? AcceptanceChance(Faction f) => Clamp(Terms(f, null));

        public override string ChanceBreakdown(Faction f)
        {
            var sb = new System.Text.StringBuilder();
            Terms(f, sb);
            return sb.ToString();
        }

        private float Terms(Faction f, System.Text.StringBuilder sb)
        {
            FactionState s = Comp.GetState(f);
            float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, s.militaryStrength);
            // How the war is going counts most: a beaten enemy wants out, a winning one wants more.
            War war = Comp.Wars.Find(f, Faction.OfPlayer);
            float score = war != null ? Mathf.Clamp(-war.ScoreFor(f) / 100f, -0.3f, 0.4f) : 0f;
            float weariness = s.warWeariness / 150f, doves = s.Bloc(Bloc.Doves) * 0.4f, opinion = (TheirView(f).opinion + 100f) / 500f;
            float strength = Mathf.Clamp(ratio - 1f, -0.3f, 0.3f) * 0.3f, leader = LeadershipUtility.AcceptanceBonus();
            if (sb != null)
            {
                DominionPaper.Term(sb, 0.1f, "Dominion_Odds_Base".Translate());
                DominionPaper.Term(sb, score, "Dominion_Odds_WarScore".Translate());
                DominionPaper.Term(sb, weariness, "Dominion_Odds_Weariness".Translate());
                DominionPaper.Term(sb, doves, "Dominion_Odds_Doves".Translate());
                DominionPaper.Term(sb, opinion, "Dominion_Odds_Opinion".Translate(TheirView(f).opinion.ToString("F0")));
                DominionPaper.Term(sb, strength, "Dominion_Odds_Strength".Translate());
                DominionPaper.Term(sb, leader, "Dominion_Odds_Leader".Translate());
            }
            return 0.1f + weariness + doves + opinion + strength + score + leader;
        }

        /// <summary>Opens the peace table, where the terms are drafted.</summary>
        public override void Execute(Faction f)
        {
            War w = Comp.Wars.Find(f, Faction.OfPlayer);
            if (w != null)
            {
                Find.WindowStack.Add(new Dialog_PeaceTable(w));
                return;
            }
            ProposeNow(f);
        }

        /// <summary>A peace offer with the default terms, decided now.</summary>
        public void ProposeNow(Faction f)
        {
            Commit(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                // Terms follow the war score (see PeaceTerms); the letter about the war's end lists them.
                DiplomacyUtility.SetRelationKind(Faction.OfPlayer, f, FactionRelationKind.Neutral, Faction.OfPlayer, def.label);
                Report("Dominion_Peace_Accepted".Translate(f.Name), true);
            }
            else
                Report("Dominion_Peace_Refused".Translate(f.Name), false);
        }
    }

    public class DiplomacyAction_DeclareWar : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer);

        public override void Execute(Faction f)
        {
            // First the war goal: it decides what you get if you win.
            var options = new List<FloatMenuOption>();
            foreach (WarGoal goal in WarUtility.PlayerGoals(f))
            {
                WarGoal g = goal;
                options.Add(new FloatMenuOption(WarUtility.GoalLabel(g), () => Confirm(f, g),
                    mouseoverGuiAction: r => TooltipHandler.TipRegion(r, WarUtility.GoalDescription(g))));
            }
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_War_ChooseGoal".Translate()));
        }

        private void Confirm(Faction f, WarGoal goal)
        {
            List<Treaty> treaties = new List<Treaty>();
            Comp.Treaties.Between(Faction.OfPlayer, f, treaties);
            Settlement target = goal == WarGoal.Conquest ? WarUtility.NearestSettlement(f, DelegationUtility.HomeTile(Faction.OfPlayer), avoidCapital: true) : null;
            string text = "Dominion_War_Confirm".Translate(f.Name) + "\n\n" + "Dominion_War_GoalChosen".Translate(WarUtility.GoalLabel(goal), WarUtility.GoalDescription(goal));
            if (target != null) text += "\n" + "Dominion_War_ConquestTarget".Translate(target.Label);
            if (treaties.Count > 0)
                text += "\n\n" + "Dominion_War_BreaksTreaties".Translate(string.Join(", ", treaties.ConvertAll(t => t.Label)));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () =>
            {
                if (f.HostileTo(Faction.OfPlayer)) return;
                Commit(f);
                Comp.Wars.pendingPlayerGoal = goal;
                Comp.Wars.pendingPlayerTarget = target;
                DiplomacyUtility.SetRelationKind(Faction.OfPlayer, f, FactionRelationKind.Hostile, Faction.OfPlayer, def.label);
            }, destructive: true));
        }
    }

    /// <summary>Opens the signing desk: pick a treaty, set its terms, sign, and see whether they seal it.</summary>
    public class DiplomacyAction_ProposeTreaty : DiplomacyActionWorker
    {
        public override string DisabledReason(Faction f) =>
            f.HostileTo(Faction.OfPlayer) ? "Dominion_Action_NotWhileHostile".Translate() : null;

        public override void Execute(Faction f) => Dialog_TreatyDraft.ProposeMenu(f);
    }

    public class DiplomacyAction_Embargo : DiplomacyActionWorker
    {
        public override void Execute(Faction f)
        {
            Commit(f);
            RelationState mine = OurPolicy(f);
            mine.embargo = !mine.embargo;
            EventBus.Raise(mine.embargo ? DominionEventDefOf.Dominion_EmbargoDeclared : DominionEventDefOf.Dominion_EmbargoLifted,
                Faction.OfPlayer, f);
        }
    }

    public class DiplomacyAction_Borders : DiplomacyActionWorker
    {
        public override void Execute(Faction f)
        {
            RelationState mine = OurPolicy(f);
            var options = new List<FloatMenuOption>();
            foreach (BorderStatus b in System.Enum.GetValues(typeof(BorderStatus)))
            {
                BorderStatus status = b;
                string label = BorderLabel(b) + ": " + ("Dominion_Border_" + b + "_Desc").Translate();
                options.Add(b == mine.border
                    ? new FloatMenuOption(label + " " + "Dominion_Border_Current".Translate(), null)
                    : new FloatMenuOption(label, () => Set(f, status)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Set(Faction f, BorderStatus status)
        {
            Commit(f);
            OurPolicy(f).border = status;
            EventBus.Raise(DominionEventDefOf.Dominion_BordersChanged, Faction.OfPlayer, f, detail: BorderLabel(status).ToLower());
        }

        public static string BorderLabel(BorderStatus b) => ("Dominion_Border_" + b).Translate();
    }

    public class DiplomacyAction_Denounce : DiplomacyActionWorker
    {
        public override bool ViaLetter => true;

        public override void Execute(Faction f)
        {
            Commit(f);
            TheirView(f).grievance += 3f * (ToneSting - 1f);
            EventBus.Raise(DominionEventDefOf.Dominion_Denounced, Faction.OfPlayer, f);
        }
    }

    public class DiplomacyAction_DemandTribute : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer);

        public override bool ViaLetter => true;

        public override bool IsDemand => true;

        public override float? AcceptanceChance(Faction f)
        {
            FactionState s = Comp.GetState(f);
            float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, s.militaryStrength);
            return Mathf.Clamp((ratio - 1f) * 0.4f * (1f - s.personality.aggression * 0.5f) + ToneChance(f), 0f, 0.85f);
        }

        public override string ChanceBreakdown(Faction f)
        {
            FactionState s = Comp.GetState(f);
            float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, s.militaryStrength);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Dominion_Odds_StrengthRatio".Translate(ratio.ToString("F2")));
            DominionPaper.Term(sb, (ratio - 1f) * 0.4f * (1f - s.personality.aggression * 0.5f), "Dominion_Odds_Strength".Translate());
            ToneTerm(sb, f);
            return sb.ToString();
        }

        public override void Execute(Faction f)
        {
            Commit(f);
            RelationState view = TheirView(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                FactionState s = Comp.GetState(f);
                int amount = EconomyEffects.Transfer(f, Faction.OfPlayer, Mathf.Clamp(Mathf.RoundToInt(s.wealth * 0.01f), 100, 1000));
                if (amount <= 0)
                {
                    Report("Dominion_Tribute_NoColony".Translate(f.Name), false);
                    return;
                }
                view.grievance += 25f;
                EventBus.Raise(DominionEventDefOf.Dominion_TributePaid, f, Faction.OfPlayer, detail: amount.ToString());
                Report("Dominion_Tribute_Paid".Translate(f.Name, amount), true);
            }
            else
            {
                view.favour -= 10f;
                view.grievance += 10f;
                Report("Dominion_Tribute_Refused".Translate(f.Name), false);
            }
        }
    }
}
