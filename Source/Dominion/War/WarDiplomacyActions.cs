using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Ask a friendly faction to join one of your wars. Refusing you is remembered by neither side; accepting is a war.</summary>
    public class DiplomacyAction_CallToArms : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer) && Comp.Wars.AnyWar(Faction.OfPlayer);

        public override string DisabledReason(Faction f) =>
            Enemies(f).Any() ? null : "Dominion_CallToArms_NoWar".Translate(f.Name);

        private static IEnumerable<Faction> Enemies(Faction f)
        {
            foreach (War w in Comp.Wars.wars)
            {
                if (!w.InvolvesPlayer) continue;
                Faction enemy = w.Other(Faction.OfPlayer);
                if (enemy != f && !f.HostileTo(enemy)) yield return enemy;
            }
        }

        public static float Acceptance(Faction f, Faction enemy)
        {
            WorldComponent_Dominion comp = Comp;
            FactionState s = comp.GetState(f);
            float chance = 0.05f + comp.GetRelation(f, Faction.OfPlayer).opinion / 150f + s.personality.honour * 0.2f
                           - comp.GetRelation(f, enemy).opinion / 200f - s.activeWars * 0.15f - s.warWeariness / 200f
                           + LeadershipUtility.AcceptanceBonus();
            if (f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally) chance += 0.3f;
            if (comp.Treaties.Has(f, Faction.OfPlayer, DominionTreatyDefOf.Dominion_DefensivePact)) chance += 0.15f;
            if (WarSystem.IsPuppetOf(f, Faction.OfPlayer, comp)) chance += 0.5f;
            if (comp.Treaties.AnyNonAggression(f, enemy)) chance *= 0.3f;
            // Nobody wants to join the losing side.
            War w = comp.Wars.Find(Faction.OfPlayer, enemy);
            if (w != null) chance += Mathf.Clamp(w.ScoreFor(Faction.OfPlayer) / 200f, -0.2f, 0.2f);
            return Mathf.Clamp(chance, 0.02f, 0.95f);
        }

        public override void Execute(Faction f)
        {
            var options = new List<FloatMenuOption>();
            foreach (Faction enemy in Enemies(f))
            {
                Faction e = enemy;
                options.Add(new FloatMenuOption("Dominion_CallToArms_Option".Translate(e.Name, Acceptance(f, e).ToStringPercent()), () => Ask(f, e)));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Asks them to join your war against <paramref name="enemy"/> (also used from audiences).</summary>
        public void Ask(Faction f, Faction enemy)
        {
            Commit(f);
            if (Rand.Chance(Acceptance(f, enemy)))
            {
                WarUtility.JoinWar(f, Faction.OfPlayer, enemy, "Dominion_Reason_CallToArms".Translate(Faction.OfPlayer.Name));
                Report("Dominion_CallToArms_Accepted".Translate(f.Name, enemy.Name), true);
            }
            else
            {
                EventBus.Raise(DominionEventDefOf.Dominion_CallToArmsRefused, f, Faction.OfPlayer, detail: enemy.Name);
                Report("Dominion_CallToArms_Refused".Translate(f.Name, enemy.Name), false);
            }
        }
    }

    /// <summary>Open rivalry without war, or an end to it.</summary>
    public class DiplomacyAction_ColdWar : DiplomacyActionWorker
    {
        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer) && !f.def.permanentEnemy;

        public override string DisabledReason(Faction f)
        {
            if (!DominionMod.Settings.coldWars) return "Dominion_ColdWar_Disabled".Translate();
            if (!ColdWarSystem.InColdWar(f, Faction.OfPlayer) && f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
                return "Dominion_ColdWar_Allied".Translate();
            return null;
        }

        public override float? AcceptanceChance(Faction f)
        {
            if (!ColdWarSystem.InColdWar(f, Faction.OfPlayer)) return null;
            float opinion = TheirView(f).opinion;
            return Clamp(0.2f + (opinion + 40f) / 100f + Comp.GetState(f).Bloc(Bloc.Doves) * 0.3f + LeadershipUtility.AcceptanceBonus());
        }

        public override void Execute(Faction f)
        {
            if (ColdWarSystem.InColdWar(f, Faction.OfPlayer))
            {
                Commit(f);
                if (Rand.Chance(AcceptanceChance(f).Value))
                {
                    ColdWarSystem.End(Faction.OfPlayer, f, escalated: false);
                    TheirView(f).favour += 5f;
                    Report("Dominion_ColdWar_Ended".Translate(f.Name), true);
                }
                else Report("Dominion_ColdWar_EndRefused".Translate(f.Name), false);
                return;
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_ColdWar_Confirm".Translate(f.Name), () =>
            {
                Commit(f);
                RelationState view = TheirView(f);
                view.favour -= 15f;
                view.grievance += 10f;
                ColdWarSystem.Begin(Faction.OfPlayer, f);
                // Their enemies approve; their friends do not.
                foreach (Faction other in Comp.Cache.Simulated)
                {
                    if (other == f) continue;
                    if (other.HostileTo(f) || ColdWarSystem.InColdWar(other, f)) Comp.GetRelation(other, Faction.OfPlayer).favour += 5f;
                    else if (other.RelationKindWith(f) == FactionRelationKind.Ally) Comp.GetRelation(other, Faction.OfPlayer).favour -= 5f;
                }
            }, destructive: true));
        }
    }

    /// <summary>Arm and pay a faction already at war with someone you oppose. Secret, unless it leaks.</summary>
    public class DiplomacyAction_ProxyWar : DiplomacyActionWorker
    {
        public const int Silver = 500;

        public override bool Visible(Faction f) => !f.HostileTo(Faction.OfPlayer) && Comp.Wars.AnyWar(f);

        public override string DisabledReason(Faction f)
        {
            if (!Targets(f).Any()) return "Dominion_Proxy_NoTarget".Translate(f.Name);
            return DiplomacyHelpers.AvailableSilver() < Silver ? "Dominion_Action_NeedsSilver".Translate(Silver) : null;
        }

        /// <summary>The enemies of <paramref name="f"/> you would like to see weakened: rivals, enemies and anyone who dislikes you.</summary>
        private static IEnumerable<Faction> Targets(Faction f)
        {
            foreach (War w in Comp.Wars.wars)
            {
                if (!w.Involves(f) || w.InvolvesPlayer) continue;
                Faction enemy = w.Other(f);
                if (enemy.HostileTo(Faction.OfPlayer) || ColdWarSystem.InColdWar(enemy, Faction.OfPlayer)
                    || Comp.GetRelation(enemy, Faction.OfPlayer).opinion < -20f)
                    yield return enemy;
            }
        }

        public override void Execute(Faction f)
        {
            var options = new List<FloatMenuOption>();
            foreach (Faction target in Targets(f))
            {
                Faction t = target;
                options.Add(new FloatMenuOption("Dominion_Proxy_Option".Translate(t.Name, Silver), () => Fund(f, t)));
            }
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Fund(Faction f, Faction enemy)
        {
            if (DiplomacyHelpers.AvailableSilverFresh() < Silver)
            {
                Messages.Message("Dominion_Action_NeedsSilver".Translate(Silver), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            EconomyEffects.Transfer(Faction.OfPlayer, f, Silver);
            Commit(f);
            WarUtility.AddScore(f, enemy, 10f);
            TheirView(f).favour += 8f;
            bool leaked = Rand.Chance(0.25f);
            if (leaked)
            {
                RelationState theirs = Comp.GetRelation(enemy, Faction.OfPlayer);
                theirs.grievance += 20f;
                theirs.favour -= 10f;
            }
            EventBus.Raise(DominionEventDefOf.Dominion_ProxyWar, Faction.OfPlayer, enemy, detail: f.Name, secrecy: leaked ? Secrecy.Public : Secrecy.Secret);
            Report((leaked ? "Dominion_Proxy_Leaked" : "Dominion_Proxy_Done").Translate(f.Name, enemy.Name), !leaked);
        }
    }
}
