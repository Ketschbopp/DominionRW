using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Basic actions need a comms console or the desk; advanced ones need the desk and a head of state.</summary>
    public enum ActionTier { Basic, Advanced }

    /// <summary>Something the player can do toward a faction from the Diplomacy tab.</summary>
    public class DiplomacyActionDef : Def
    {
        public Type workerClass;
        public ActionTier tier = ActionTier.Basic;
        public float influenceCost;
        public int cooldownDays;
        public ResearchProjectDef requiredResearch;
        public int order;

        [Unsaved] private DiplomacyActionWorker workerInt;

        public DiplomacyActionWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (DiplomacyActionWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }
    }

    public abstract class DiplomacyActionWorker
    {
        public DiplomacyActionDef def;

        /// <summary>Whether the button appears at all for this faction.</summary>
        public virtual bool Visible(Faction f) => true;

        /// <summary>A reason specific to this action that it cannot be used now, or null.</summary>
        public virtual string DisabledReason(Faction f) => null;

        /// <summary>Chance the other side agrees, if the action needs their agreement.</summary>
        public virtual float? AcceptanceChance(Faction f) => null;

        /// <summary>Carry out the action. Workers with a choice (amount, treaty...) open a menu and commit later.</summary>
        public abstract void Execute(Faction f);

        /// <summary>Spends influence and starts the cooldown. Call when the action actually happens.</summary>
        protected void Commit(Faction f, float extraInfluence = 0f)
        {
            PlayerState player = WorldComponent_Dominion.Instance.Player;
            player.AddInfluence(-(def.influenceCost + extraInfluence));
            if (def.cooldownDays > 0)
                player.SetCooldown(def.defName, f.loadID, Find.TickManager.TicksGame + def.cooldownDays * GenDate.TicksPerDay);
        }

        protected static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        protected static RelationState TheirView(Faction f) => Comp.GetRelation(f, Faction.OfPlayer);

        protected static RelationState OurPolicy(Faction f) => Comp.GetRelation(Faction.OfPlayer, f);

        protected static void Report(string text, bool good) =>
            Messages.Message(text, good ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, historical: true);

        protected static float Clamp(float chance) => Mathf.Clamp(chance, 0.02f, 0.95f);
    }

    public static class DiplomacyActions
    {
        /// <summary>Why the player cannot use this action on this faction now, or null if they can.</summary>
        public static string CannotUseReason(DiplomacyActionDef def, Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || f == null || f.IsPlayer || f.defeated) return "Dominion_Action_Invalid".Translate();

            if (def.tier == ActionTier.Advanced)
            {
                if (LeadershipUtility.BestDesk() == null) return "Dominion_Action_NeedsDesk".Translate();
                if (LeadershipUtility.HeadOfState() == null) return LeadershipUtility.NoHeadOfStateReason();
            }
            else if (LeadershipUtility.BestDesk() == null && !LeadershipUtility.HasComms())
                return "Dominion_Action_NeedsComms".Translate();

            if (def.requiredResearch != null && !def.requiredResearch.IsFinished)
                return "Dominion_Action_NeedsResearch".Translate(def.requiredResearch.label);

            int until = comp.Player.CooldownUntil(def.defName, f.loadID);
            int now = Find.TickManager.TicksGame;
            if (until > now)
                return "Dominion_Action_Cooldown".Translate((until - now).ToStringTicksToPeriod(allowSeconds: false));

            if (comp.Player.influence < def.influenceCost)
                return "Dominion_Action_NeedsInfluence".Translate(def.influenceCost.ToString("F0"));

            return def.Worker.DisabledReason(f);
        }
    }
}
