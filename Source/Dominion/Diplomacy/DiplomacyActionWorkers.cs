using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    public class DiplomacyAction_Gift : DiplomacyActionWorker
    {
        private static readonly int[] Amounts = { 100, 250, 500, 1000 };

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

        private void Give(Faction f, int amount)
        {
            int taken = DiplomacyHelpers.TakeSilver(amount);
            if (taken <= 0) return;
            Commit(f);
            int goodwill = Mathf.Min(15, Mathf.RoundToInt(taken / 40f));
            f.TryAffectGoodwillWith(Faction.OfPlayer, goodwill, canSendMessage: false, canSendHostilityLetter: false);
            TheirView(f).favour += Mathf.Min(20f, taken / 40f);
            Report("Dominion_Gift_Sent".Translate(taken, f.Name, goodwill), true);
        }
    }

    public class DiplomacyAction_Compliment : DiplomacyActionWorker
    {
        public override string DisabledReason(Faction f) =>
            f.HostileTo(Faction.OfPlayer) ? "Dominion_Action_NotWhileHostile".Translate() : null;

        public override float? AcceptanceChance(Faction f) => Clamp(0.6f + LeadershipUtility.AcceptanceBonus());

        public override void Execute(Faction f)
        {
            Commit(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                TheirView(f).favour += 6f;
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
        public override void Execute(Faction f)
        {
            Commit(f);
            RelationState view = TheirView(f);
            view.favour -= 15f;
            view.grievance += 5f;
            f.TryAffectGoodwillWith(Faction.OfPlayer, -10, canSendMessage: false, canSendHostilityLetter: true);
            Report("Dominion_Insult_Done".Translate(f.Name), false);
        }
    }

    public class DiplomacyAction_ProposePeace : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => f.HostileTo(Faction.OfPlayer);

        public override string DisabledReason(Faction f) =>
            f.def.permanentEnemy ? "Dominion_Action_PermanentEnemy".Translate() : null;

        public override float? AcceptanceChance(Faction f)
        {
            FactionState s = Comp.GetState(f);
            float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, s.militaryStrength);
            return Clamp(0.1f + s.warWeariness / 150f + s.Bloc(Bloc.Doves) * 0.4f + (TheirView(f).opinion + 100f) / 500f
                         + Mathf.Clamp(ratio - 1f, -0.3f, 0.3f) * 0.3f + LeadershipUtility.AcceptanceBonus());
        }

        public override void Execute(Faction f)
        {
            Commit(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
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
            List<Treaty> treaties = new List<Treaty>();
            Comp.Treaties.Between(Faction.OfPlayer, f, treaties);
            string text = "Dominion_War_Confirm".Translate(f.Name);
            if (treaties.Count > 0)
                text += "\n\n" + "Dominion_War_BreaksTreaties".Translate(string.Join(", ", treaties.ConvertAll(t => t.Label)));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () =>
            {
                Commit(f);
                DiplomacyUtility.SetRelationKind(Faction.OfPlayer, f, FactionRelationKind.Hostile, Faction.OfPlayer, def.label);
            }, destructive: true));
        }
    }

    public class DiplomacyAction_ProposeTreaty : DiplomacyActionWorker
    {
        public override string DisabledReason(Faction f) =>
            f.HostileTo(Faction.OfPlayer) ? "Dominion_Action_NotWhileHostile".Translate() : null;

        public override void Execute(Faction f)
        {
            var options = new List<FloatMenuOption>();
            float influence = Comp.Player.influence;
            foreach (TreatyDef t in DefDatabase<TreatyDef>.AllDefsListForReading)
            {
                TreatyDef treaty = t;
                string reason = Comp.Treaties.CannotSignReason(t, Faction.OfPlayer, f);
                if (reason == null && t.requiredResearch != null && !t.requiredResearch.IsFinished)
                    reason = "Dominion_Action_NeedsResearch".Translate(t.requiredResearch.label);
                float cost = def.influenceCost + t.influenceCost;
                if (reason == null && influence < cost)
                    reason = "Dominion_Action_NeedsInfluence".Translate(cost.ToString("F0"));
                float chance = DiplomacyHelpers.TreatyAcceptance(t, f);
                string label = "Dominion_Treaty_Option".Translate(t.LabelCap, cost.ToString("F0"), chance.ToStringPercent());
                options.Add(reason != null
                    ? new FloatMenuOption(label + " (" + reason + ")", null)
                    : new FloatMenuOption(label, () => Propose(f, treaty, chance)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Propose(Faction f, TreatyDef treaty, float chance)
        {
            Commit(f, treaty.influenceCost);
            if (Rand.Chance(chance))
            {
                Comp.Treaties.Sign(treaty, Faction.OfPlayer, f, Faction.OfPlayer);
                Report("Dominion_Treaty_Accepted".Translate(f.Name, treaty.label), true);
            }
            else
            {
                TheirView(f).favour -= 2f;
                Report("Dominion_Treaty_Refused".Translate(f.Name, treaty.label), false);
            }
        }
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
        public override void Execute(Faction f)
        {
            Commit(f);
            EventBus.Raise(DominionEventDefOf.Dominion_Denounced, Faction.OfPlayer, f);
        }
    }

    public class DiplomacyAction_DemandTribute : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer);

        public override float? AcceptanceChance(Faction f)
        {
            FactionState s = Comp.GetState(f);
            float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, s.militaryStrength);
            return Mathf.Clamp((ratio - 1f) * 0.4f * (1f - s.personality.aggression * 0.5f), 0f, 0.85f);
        }

        public override void Execute(Faction f)
        {
            Commit(f);
            RelationState view = TheirView(f);
            if (Rand.Chance(AcceptanceChance(f).Value))
            {
                FactionState s = Comp.GetState(f);
                int amount = Mathf.Clamp(Mathf.RoundToInt(s.wealth * 0.01f), 100, 1000);
                DiplomacyHelpers.DropSilver(amount);
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
