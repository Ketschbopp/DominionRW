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

        static IntegrationEventDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IntegrationEventDefOf));
        }
    }
}
