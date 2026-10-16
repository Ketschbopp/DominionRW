using System;

namespace Dominion
{
    // Optional mods. Each one only binds the members later milestones will call, so the self-test
    // shows straight away which mods are wired correctly. Behaviour is added per milestone
    // (see docs/DESIGN.md §4.3 and §9). Integrations with behaviour live in their own files.

    public class Integration_SimpleLeadership : DominionIntegration
    {
        public override string PackageId => "pb3n.SimpleLeadership";
        public override string Label => "Simple Leadership";

        protected internal override void Bind(Binder b)
        {
            Type tracker = b.Type("SimpleLeadership.WorldComponent_LeaderTracker");
            b.Field(tracker, "Instance");
            b.Method(tracker, "GetBaseLeader");
            b.Method(tracker, "GetLeadershipDataFor");
            b.Method(tracker, "StartPowerEvent");
            b.Method(tracker, "GetActiveEventsFor");
        }
    }

    public class Integration_VanillaOutposts : DominionIntegration
    {
        public override string PackageId => "vanillaexpanded.outposts";
        public override string Label => "Vanilla Outposts Expanded";

        protected internal override void Bind(Binder b)
        {
            b.Type("VOE.Outpost_Defensive");
            b.Type("VOE.Outpost_Artillery");
            b.Type("VOE.TravellingArtilleryStrike");
        }
    }

    public class Integration_VisitSettlements : DominionIntegration
    {
        public override string PackageId => "alt4s.VisitSettlements";
        public override string Label => "Visit Settlements";

        protected internal override void Bind(Binder b)
        {
            Type data = b.Type("VisitSettlements.WorldComponent_SettlementData");
            b.Field(data, "settlementMaps");
            b.Field(data, "settlementMapParents");
        }
    }

    public class Integration_RimToll : DominionIntegration
    {
        public override string PackageId => "forti.rimtoll";
        public override string Label => "RimToll";

        protected internal override void Bind(Binder b)
        {
            Type state = b.Type("RimToll.GameComponent_TollState");
            b.Method(state, "ResentmentFor");
            b.Method(state, "MemoryFor");
            b.Method(state, "MostResentfulFaction");
        }
    }

    public class Integration_RimCourt : DominionIntegration
    {
        public override string PackageId => "forti.rimcourt";
        public override string Label => "RimCourt";

        protected internal override void Bind(Binder b)
        {
            Type court = b.Type("RimCourt.Court.CourtManager");
            b.Method(court, "TryConvene");
            b.Type("RimCourt.Court.Betrothal");
            b.Type("RimCourt.Court.PetitionWorker_Envoy");
        }
    }

    public class Integration_RimMarket : DominionIntegration
    {
        public override string PackageId => "forti.rimmarket";
        public override string Label => "RimMarket";

        protected internal override void Bind(Binder b)
        {
            b.Type("RimMarket.Routes.TradeRoute");
            b.Type("RimMarket.Routes.TradeConvoy");
            b.Type("RimMarket.Market.MarketManager");
            b.Type("RimMarket.Reputation.MarketReputation");
        }
    }

    public class Integration_IRBM : DominionIntegration
    {
        public override string PackageId => "kazepsi.irbm";
        public override string Label => "InterRim Ballistic Missile";

        protected internal override void Bind(Binder b)
        {
            b.Type("IRBM.SatelliteRegistry");
            b.Type("IRBM.DoomsdayClockManager");
            b.Type("IRBM.MissileQueueManager");
            b.Type("IRBM.AIFactionMissileUtility");
        }
    }

    public class Integration_VanillaBooks : DominionIntegration
    {
        public override string PackageId => "VanillaExpanded.VBooksE";
        public override string Label => "Vanilla Books Expanded";

        protected internal override void Bind(Binder b)
        {
            b.Type("VanillaBooksExpanded.Newspaper");
            Type doer = b.Type("VanillaBooksExpanded.BookOutcomeDoer_Newspaper");
            b.Method(doer, "OnBookGenerated");
        }
    }

    public class Integration_PillageAndPlunder : DominionIntegration
    {
        public override string PackageId => "lordfelix.pillageandplunder";
        public override string Label => "Pillage & Plunder";

        protected internal override void Bind(Binder b)
        {
            b.Type("PillageAndPlunder.PlunderedSettlementManager");
            b.Type("PillageAndPlunder.IncidentWorker_NpcSettlementPlunder");
        }
    }
}
