using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Works out the player's intel on each faction once a day: research, radio coverage, relations,
    /// proximity, and how secretive the faction is. Intel moves 10% of the way to its target per day.
    /// Espionage (M6) and satellites (IRBM) will add to the target.
    /// </summary>
    public class IntelSystem : SimSystem
    {
        public override int Order => 15;

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.radioCoverage *= 0.7f;
            if (comp.radioCoverage < 0.5f) comp.radioCoverage = 0f;
        }

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(faction);
            s.playerIntel = Mathf.Clamp(s.playerIntel + (TargetIntel(faction, s, comp) - s.playerIntel) * 0.1f, 0f, 100f);
        }

        public static float TargetIntel(Faction f, FactionState s, WorldComponent_Dominion comp)
        {
            float t = 5f; // word of mouth

            if (DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished) t += 8f;
            if (DominionResearchDefOf.Dominion_WorldCommsProtocols.IsFinished) t += 8f;
            if (DominionResearchDefOf.Dominion_SignalDecoding.IsFinished) t += 12f;
            if (DominionResearchDefOf.Dominion_IntelligenceAgency.IsFinished) t += 10f;

            t += Mathf.Min(25f, comp.radioCoverage * 0.25f);
            t += comp.Intel.Network(f) * 0.3f;

            Faction player = Faction.OfPlayerSilentFail;
            if (player != null)
            {
                switch (f.RelationKindWith(player))
                {
                    case FactionRelationKind.Ally: t += 25f; break;
                    case FactionRelationKind.Neutral: t += f.PlayerGoodwill > 25 ? 8f : 3f; break;
                }
                RelationState r = comp.TryGetRelation(f, player);
                if (r != null && r.distance >= 0f)
                    t += r.distance < 15f ? 15f : r.distance < 40f ? 8f : 0f;
            }

            // Intel-sharing partners tell you about themselves and, less fully, about their enemies.
            foreach (Treaty treaty in comp.Treaties.All)
            {
                if (treaty.def.intelBonus <= 0f || player == null || !treaty.Involves(player)) continue;
                Faction partner = treaty.Other(player);
                if (partner == f) t += treaty.def.intelBonus;
                else if (partner.HostileTo(f)) t += treaty.def.intelBonus * 0.4f;
            }

            t -= s.personality.paranoia * 10f;
            if (s.government == GovernmentType.Junta || s.government == GovernmentType.Autocracy) t -= 5f;

            return Mathf.Clamp(t, 0f, 100f);
        }
    }

    [DefOf]
    public static class DominionResearchDefOf
    {
        public static ResearchProjectDef Dominion_RadioMonitoring;
        public static ResearchProjectDef Dominion_WorldCommsProtocols;
        public static ResearchProjectDef Dominion_SignalDecoding;
        public static ResearchProjectDef Dominion_IntelligenceAgency;
        public static ResearchProjectDef Dominion_DiplomaticCorps;
        public static ResearchProjectDef Dominion_IntelligenceNetworks;
        public static ResearchProjectDef Dominion_Broadcasting;

        static DominionResearchDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionResearchDefOf));
        }
    }
}
