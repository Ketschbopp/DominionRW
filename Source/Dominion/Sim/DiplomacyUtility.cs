using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Changes relations between factions through vanilla's own state (base goodwill and relation kind on
    /// both sides), so vanilla, other mods and Dominion all agree on who is at war with whom.
    /// </summary>
    public static class DiplomacyUtility
    {
        /// <summary>The faction driving the relation change in progress, so events can name the aggressor.</summary>
        public static Faction Actor { get; private set; }

        /// <summary>True if the AI may change this pair's relations on its own.</summary>
        public static bool AiMayChange(Faction a, Faction b) =>
            a != null && b != null && a != b && !a.IsPlayer && !b.IsPlayer
            && !a.def.permanentEnemy && !b.def.permanentEnemy && !a.Hidden && !b.Hidden;

        /// <summary>Puts the pair into <paramref name="kind"/>, with goodwill to match, on both sides.</summary>
        public static void SetRelationKind(Faction a, Faction b, FactionRelationKind kind, Faction actor, string reason)
        {
            if (a == null || b == null || a == b) return;
            FactionRelationKind before = a.RelationKindWith(b);
            int target;
            switch (kind)
            {
                case FactionRelationKind.Hostile: target = -80; break;
                case FactionRelationKind.Ally: target = 80; break;
                default: target = before == FactionRelationKind.Hostile ? 10 : before == FactionRelationKind.Ally ? -10 : 0; break;
            }
            // GoodwillWith adds situational modifiers (ideology etc.) on top of base goodwill.
            int situational = a.GoodwillWith(b) - a.BaseGoodwillWith(b);
            SetBaseGoodwill(a, b, target - situational);

            Faction previousActor = Actor;
            Actor = actor;
            try
            {
                a.SetRelationDirect(b, kind, canSendHostilityLetter: false, reason: reason, lookTarget: null);
                if (b.RelationKindWith(a) != kind)
                    b.SetRelationDirect(a, kind, canSendHostilityLetter: false, reason: reason, lookTarget: null);
            }
            finally
            {
                Actor = previousActor;
            }
        }

        public static void SetBaseGoodwill(Faction a, Faction b, int value)
        {
            value = Mathf.Clamp(value, -100, 100);
            FactionRelation ab = a.RelationWith(b, allowNull: true);
            FactionRelation ba = b.RelationWith(a, allowNull: true);
            if (ab != null) ab.baseGoodwill = value;
            if (ba != null) ba.baseGoodwill = value;
        }

        /// <summary>
        /// Moves base goodwill a few points toward <paramref name="towards"/> without crossing the thresholds
        /// that would flip the relation kind. Changing kind is always a deliberate decision.
        /// </summary>
        public static void NudgeGoodwill(Faction a, Faction b, float towards)
        {
            int current = a.BaseGoodwillWith(b);
            float diff = towards - current;
            if (Mathf.Abs(diff) < 2f) return;
            int step = Mathf.Clamp(Mathf.RoundToInt(diff * 0.1f), -3, 3);
            if (step == 0) step = diff > 0 ? 1 : -1;

            int min, max;
            switch (a.RelationKindWith(b))
            {
                case FactionRelationKind.Hostile: min = -100; max = -10; break;
                case FactionRelationKind.Ally: min = 10; max = 100; break;
                default: min = -70; max = 70; break;
            }
            int next = Mathf.Clamp(current + step, min, max);
            if (next != current)
                SetBaseGoodwill(a, b, next);
        }
    }
}
