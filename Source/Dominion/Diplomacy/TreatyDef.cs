using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>A kind of treaty. What it does is set by these flags and numbers, so new treaties are XML-only.</summary>
    public class TreatyDef : Def
    {
        /// <summary>Days until it expires; 0 or less means it lasts until cancelled or broken.</summary>
        public int durationDays;

        /// <summary>Influence the player spends to propose it.</summary>
        public float influenceCost = 20f;

        /// <summary>The player needs this research to propose it. Optional.</summary>
        public ResearchProjectDef requiredResearch;

        /// <summary>How secret the treaty is. Signing and breaking it are reported with this secrecy.</summary>
        public Secrecy secrecy = Secrecy.Public;

        // Effects
        /// <summary>Neither side may attack the other while it holds.</summary>
        public bool nonAggression;

        /// <summary>Makes the two factions vanilla allies (and ends when the alliance ends).</summary>
        public bool alliance;

        /// <summary>Each side is expected to join a war declared on the other.</summary>
        public bool defensive;

        /// <summary>Price change when trading with the partner: buy price × (1 − this), sell price × (1 + this).</summary>
        public float tradeBonus;

        /// <summary>Intel target bonus on the partner, for the player.</summary>
        public float intelBonus;

        /// <summary>Added to each side's opinion of the other while it holds.</summary>
        public float opinionBonus;

        /// <summary>Change to each side's economy index while it holds (0.03 = +3%).</summary>
        public float economyBonus;

        // AI acceptance
        /// <summary>Base chance an AI accepts, before opinion and personality.</summary>
        public float aiAcceptBase = 0.3f;

        /// <summary>The AI never accepts below this opinion of the proposer.</summary>
        public float minOpinion = -10f;

        public float greedWeight;
        public float honourWeight;
        public float paranoiaWeight;

        /// <summary>The AI only accepts if its settlements are within this many tiles of the proposer's; 0 = anywhere.</summary>
        public float maxDistance;

        /// <summary>Treaties that cannot exist alongside this one between the same pair.</summary>
        public List<TreatyDef> incompatibleWith;

        public bool Expires => durationDays > 0;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
                yield return e;
            if (aiAcceptBase < 0f || aiAcceptBase > 1f)
                yield return "aiAcceptBase must be between 0 and 1";
        }
    }

    [DefOf]
    public static class DominionTreatyDefOf
    {
        public static TreatyDef Dominion_Alliance;
        public static TreatyDef Dominion_NonAggressionPact;
        public static TreatyDef Dominion_TradeAgreement;
        public static TreatyDef Dominion_DefensivePact;
        public static TreatyDef Dominion_IntelSharing;

        static DominionTreatyDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionTreatyDefOf));
        }
    }
}
