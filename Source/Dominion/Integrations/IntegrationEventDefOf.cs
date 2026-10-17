using RimWorld;

namespace Dominion
{
    /// <summary>Events raised by integrations (defined in Dominion_Events_Integrations.xml).</summary>
    [DefOf]
    public static class IntegrationEventDefOf
    {
        // Faction Territories expansion
        public static DominionEventDef Dominion_SettlementConstructionBegun;
        public static DominionEventDef Dominion_SettlementFounded;
        public static DominionEventDef Dominion_ConstructionRaided;

        // RimMarket trade routes
        public static DominionEventDef Dominion_ConvoyLost;

        // Economics & Demography
        public static DominionEventDef Dominion_Starvation;
        public static DominionEventDef Dominion_PirateTheft;
        public static DominionEventDef Dominion_SettlementExpanded;
        public static DominionEventDef Dominion_FactionResettled;
        public static DominionEventDef Dominion_SettlementAbandoned;

        static IntegrationEventDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IntegrationEventDefOf));
        }
    }
}
