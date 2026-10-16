using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// An abstract military strength per faction, used by the AI to judge wars. Real forces (warbands)
    /// come in M7 and will feed back into this.
    /// </summary>
    public class MilitarySystem : SimSystem
    {
        public override int Order => 3;

        public static float TechFactor(TechLevel t)
        {
            switch (t)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic: return 0.6f;
                case TechLevel.Medieval: return 0.8f;
                case TechLevel.Industrial: return 1f;
                case TechLevel.Spacer: return 1.4f;
                case TechLevel.Ultra:
                case TechLevel.Archotech: return 1.8f;
                default: return 1f;
            }
        }

        public override void DailyFaction(Faction faction)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(faction);
            float baseForce = s.settlementCount * 8f + s.population * 0.5f + s.vassalOutposts * 4f + s.territoryTiles * 0.01f;
            float economy = Mathf.Sqrt(Mathf.Max(10f, s.economyIndex) / 100f);
            float weariness = 1f - s.warWeariness / 200f;
            s.militaryStrength = baseForce * TechFactor(faction.def.techLevel) * economy * weariness;
        }
    }
}
