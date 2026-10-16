using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// AI factions conduct diplomacy among themselves: they drift goodwill toward their opinions, and
    /// now and then declare war, make peace, ally or break alliances. Player relations are not touched
    /// here; the player's diplomacy arrives with the Dominion tab in M3.
    /// </summary>
    public class AIDiplomacySystem : SimSystem
    {
        public override int Order => 30;

        private const int MaxConcurrentWars = 2;

        private readonly List<Faction> candidates = new List<Faction>();

        public override void DailyFaction(Faction faction)
        {
            DominionSettings settings = DominionMod.Settings;
            if (!settings.aiDiplomacyEnabled || faction.def.permanentEnemy) return;

            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState fs = comp.GetState(faction);
            List<Faction> simulated = comp.Cache.Simulated;

            candidates.Clear();
            for (int i = 0; i < simulated.Count; i++)
                if (DiplomacyUtility.AiMayChange(faction, simulated[i]))
                    candidates.Add(simulated[i]);

            // Goodwill follows opinion. Each pair is nudged once a day, by its lower-id member.
            for (int i = 0; i < candidates.Count; i++)
            {
                Faction other = candidates[i];
                if (faction.loadID > other.loadID) continue;
                float avg = (comp.GetRelation(faction, other).opinion + comp.GetRelation(other, faction).opinion) * 0.5f;
                DiplomacyUtility.NudgeGoodwill(faction, other, avg);
            }

            int now = Find.TickManager.TicksGame;
            if (now < fs.nextAiActionTick || candidates.Count == 0) return;

            // Consider partners in a random order; the first decision that passes its roll is taken.
            int start = Rand.Range(0, candidates.Count);
            for (int k = 0; k < candidates.Count; k++)
            {
                Faction other = candidates[(start + k) % candidates.Count];
                if (TryAct(faction, fs, other, comp, now, settings))
                {
                    fs.nextAiActionTick = now + Mathf.RoundToInt(Rand.Range(8f, 15f) * settings.AiCooldownFactor * GenDate.TicksPerDay);
                    return;
                }
            }
        }

        private static bool TryAct(Faction f, FactionState fs, Faction other, WorldComponent_Dominion comp, int now, DominionSettings settings)
        {
            RelationState r = comp.GetRelation(f, other);
            RelationState back = comp.GetRelation(other, f);
            FactionState os = comp.GetState(other);
            AIPersonality p = fs.personality;

            switch (f.RelationKindWith(other))
            {
                case FactionRelationKind.Hostile:
                {
                    bool active = r.AtWar;
                    if (active && now - r.warSince < 10 * GenDate.TicksPerDay) return false;
                    float mine = fs.warWeariness / 100f * 0.6f + fs.Bloc(Bloc.Doves) * 0.5f + (r.opinion + 100f) / 400f + p.honour * 0.1f;
                    float theirs = os.warWeariness / 100f * 0.6f + os.Bloc(Bloc.Doves) * 0.5f + (back.opinion + 100f) / 400f;
                    float chance = Mathf.Min(mine, theirs) * (active ? 0.04f : 0.01f);
                    if (!Rand.Chance(chance)) return false;
                    DiplomacyUtility.SetRelationKind(f, other, FactionRelationKind.Neutral, f, "Dominion_Reason_Peace".Translate());
                    return true;
                }

                case FactionRelationKind.Neutral:
                {
                    if (r.opinion > 55f && back.opinion > 50f && (r.sharedEnemies > 0 || p.honour > 0.6f) && Rand.Chance(0.03f))
                    {
                        DiplomacyUtility.SetRelationKind(f, other, FactionRelationKind.Ally, f, "Dominion_Reason_Alliance".Translate());
                        return true;
                    }

                    TreatyRegistry treaties = comp.Treaties;
                    if (r.opinion > 30f && back.opinion > 25f && !treaties.Has(f, other, DominionTreatyDefOf.Dominion_TradeAgreement)
                        && Rand.Chance(0.03f * (p.greed + os.personality.greed)))
                    {
                        treaties.Sign(DominionTreatyDefOf.Dominion_TradeAgreement, f, other, f);
                        return true;
                    }
                    if (r.opinion > 5f && back.opinion > 0f && r.distance >= 0f && r.distance < RelationsSystem.NeighbourDistance
                        && p.paranoia > 0.5f && !treaties.Has(f, other, DominionTreatyDefOf.Dominion_NonAggressionPact) && Rand.Chance(0.02f))
                    {
                        treaties.Sign(DominionTreatyDefOf.Dominion_NonAggressionPact, f, other, f);
                        return true;
                    }

                    if (now < r.truceUntil || fs.activeWars >= MaxConcurrentWars) return false;
                    float hatred = Mathf.Max(0f, (-r.opinion - 35f) / 65f);
                    if (hatred <= 0f) return false;
                    float strength = Mathf.Clamp(fs.militaryStrength / Mathf.Max(1f, os.militaryStrength), 0.5f, 2f);
                    float proximity = r.distance < 0f ? 0.3f : Mathf.Clamp(1f - (r.distance - 5f) / 40f, 0.1f, 1f);
                    float stable = fs.stability > 35f ? 1f : 0.4f;
                    float utility = hatred * fs.effectiveAggression * strength * proximity * stable * (1f - fs.warWeariness / 100f);
                    // Breaking a pact is possible, but only for the dishonourable and the desperate.
                    if (treaties.AnyNonAggression(f, other)) utility *= (1f - p.honour) * 0.3f;
                    if (!Rand.Chance(Mathf.Min(0.1f, utility * 0.03f * settings.aiAggression))) return false;
                    DiplomacyUtility.SetRelationKind(f, other, FactionRelationKind.Hostile, f, "Dominion_Reason_War".Translate());
                    return true;
                }

                case FactionRelationKind.Ally:
                {
                    if (r.opinion > 60f && back.opinion > 50f && !comp.Treaties.Has(f, other, DominionTreatyDefOf.Dominion_DefensivePact)
                        && Rand.Chance(0.02f))
                    {
                        comp.Treaties.Sign(DominionTreatyDefOf.Dominion_DefensivePact, f, other, f);
                        return true;
                    }
                    if (r.opinion >= 5f) return false;
                    float chance = (5f - r.opinion) / 100f * (1f - p.honour) * 0.5f;
                    if (!Rand.Chance(chance)) return false;
                    DiplomacyUtility.SetRelationKind(f, other, FactionRelationKind.Neutral, f, "Dominion_Reason_BreakAlliance".Translate());
                    return true;
                }
            }
            return false;
        }
    }
}
