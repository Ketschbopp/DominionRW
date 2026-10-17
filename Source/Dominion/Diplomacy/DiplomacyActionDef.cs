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

    /// <summary>How an envoy's letter is worded. Warm letters win friends, threatening ones win concessions from the weak.</summary>
    public enum LetterTone { Warm, Formal, Threatening }

    public abstract class DiplomacyActionWorker
    {
        public DiplomacyActionDef def;

        /// <summary>The tone of the letter carrying this action; set by the envoy letter while it executes.</summary>
        public LetterTone tone = LetterTone.Formal;

        /// <summary>Sent as an envoy's letter (with a tone) rather than straight from the button.</summary>
        public virtual bool ViaLetter => false;

        /// <summary>Asks for something rather than offering it: a threatening tone helps when you are the stronger.</summary>
        public virtual bool IsDemand => false;

        /// <summary>The reasons behind <see cref="AcceptanceChance"/>, one per line, or null.</summary>
        public virtual string ChanceBreakdown(Faction f) => null;

        /// <summary>The chance with a given tone, without changing the worker's tone.</summary>
        public float? ChanceWith(Faction f, LetterTone t)
        {
            LetterTone old = tone;
            tone = t;
            try { return AcceptanceChance(f); }
            finally { tone = old; }
        }

        /// <summary>What the tone adds to the chance they agree.</summary>
        protected float ToneChance(Faction f)
        {
            switch (tone)
            {
                case LetterTone.Warm:
                    return TheirView(f).opinion >= -20f ? 0.08f : -0.04f;
                case LetterTone.Threatening:
                    if (!IsDemand) return -0.12f;
                    float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, Comp.GetState(f).militaryStrength);
                    return Mathf.Clamp((ratio - 1f) * 0.15f, -0.1f, 0.15f);
                default:
                    return 0f;
            }
        }

        /// <summary>How much goodwill a friendly gesture earns in this tone.</summary>
        protected float ToneFavour => tone == LetterTone.Warm ? 1.25f : tone == LetterTone.Threatening ? 0.5f : 1f;

        /// <summary>How much a hostile gesture stings in this tone.</summary>
        protected float ToneSting => tone == LetterTone.Warm ? 0.6f : tone == LetterTone.Threatening ? 1.5f : 1f;

        protected void ToneTerm(System.Text.StringBuilder sb, Faction f) =>
            DominionPaper.Term(sb, ToneChance(f), "Dominion_Odds_Tone".Translate(("Dominion_Tone_" + tone).Translate()));

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
            reportFaction = f;
            PlayerState player = WorldComponent_Dominion.Instance.Player;
            player.AddInfluence(-(def.influenceCost + extraInfluence));
            if (def.cooldownDays > 0)
                player.SetCooldown(def.defName, f.loadID, Find.TickManager.TicksGame + def.cooldownDays * GenDate.TicksPerDay);
        }

        protected static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        protected static RelationState TheirView(Faction f) => Comp.GetRelation(f, Faction.OfPlayer);

        protected static RelationState OurPolicy(Faction f) => Comp.GetRelation(Faction.OfPlayer, f);

        /// <summary>The faction of the action being carried out, so its report goes into that faction's record.</summary>
        private static Faction reportFaction;

        protected static void Report(string text, bool good)
        {
            Messages.Message(text, good ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, historical: true);
            if (reportFaction != null) Comp?.Player.Log(reportFaction, text, good ? 1 : -1);
        }

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
