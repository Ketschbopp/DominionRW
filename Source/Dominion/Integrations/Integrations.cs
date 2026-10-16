using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    // Optional mods. Each binds what it needs at startup, and adds its effects through the modifier hooks on
    // DominionIntegration and its daily hook. Everything goes through reflection, so a missing or changed mod
    // only switches its own integration off. See docs/DESIGN.md §9h.

    /// <summary>Simple Leadership: its power struggles shake a faction's politics; capital governors show in dossiers.</summary>
    public class Integration_SimpleLeadership : DominionIntegration
    {
        public override string PackageId => "pb3n.SimpleLeadership";
        public override string Label => "Simple Leadership";

        private FieldInfo instance;
        private MethodInfo getBaseLeader;
        private MethodInfo getActiveEventsFor;
        private readonly Dictionary<int, int> knownEvents = new Dictionary<int, int>();
        private bool primed;

        /// <summary>The first day after loading only records what is already going on.</summary>
        protected internal override void OnWorldLoaded()
        {
            knownEvents.Clear();
            primed = false;
        }

        protected internal override void Bind(Binder b)
        {
            Type tracker = b.Type("SimpleLeadership.WorldComponent_LeaderTracker");
            instance = b.Field(tracker, "Instance");
            getBaseLeader = b.Method(tracker, "GetBaseLeader", new[] { typeof(Settlement) });
            getActiveEventsFor = b.Method(tracker, "GetActiveEventsFor", new[] { typeof(object) });
        }

        private object Tracker => instance?.GetValue(null);

        /// <summary>The leader Simple Leadership assigned to a settlement, if any.</summary>
        public Pawn GovernorOf(Settlement s)
        {
            object t = Tracker;
            return t == null || s == null ? null : getBaseLeader.Invoke(t, new object[] { s }) as Pawn;
        }

        public override void DailyGlobal()
        {
            object t = Tracker;
            if (t == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in comp.Cache.Simulated)
            {
                int count = (getActiveEventsFor.Invoke(t, new object[] { f }) as ICollection)?.Count ?? 0;
                knownEvents.TryGetValue(f.loadID, out int before);
                knownEvents[f.loadID] = count;
                if (!primed || count <= before) continue;
                FactionState s = comp.GetState(f);
                s.stability = Mathf.Max(0f, s.stability - 4f);
                s.legitimacy = Mathf.Max(0f, s.legitimacy - 5f);
                EventBus.Raise(DominionEventDefOf.Dominion_PowerStruggle, f, source: PackageId);
            }
            primed = true;
        }
    }

    /// <summary>Vanilla Outposts Expanded: your outposts add council weight and watch nearby factions.</summary>
    public class Integration_VanillaOutposts : DominionIntegration
    {
        public override string PackageId => "vanillaexpanded.outposts";
        public override string Label => "Vanilla Outposts Expanded";

        private const float WatchTiles = 15f;
        private Type outpostBase;
        private readonly List<PlanetTile> playerOutposts = new List<PlanetTile>();

        protected internal override void Bind(Binder b)
        {
            Type defensive = b.Type("VOE.Outpost_Defensive");
            outpostBase = defensive?.BaseType; // the shared Outpost base class from Vanilla Expanded Framework
            if (outpostBase == null) b.Fail("outpost base class");
        }

        public override void DailyGlobal()
        {
            playerOutposts.Clear();
            foreach (WorldObject o in Find.WorldObjects.AllWorldObjects)
                if (o.Faction != null && o.Faction.IsPlayer && outpostBase.IsInstanceOfType(o))
                    playerOutposts.Add(o.Tile);
        }

        public override float VoteWeightModifier(Faction f) => f.IsPlayer ? Mathf.Min(3f, playerOutposts.Count * 0.3f) : 0f;

        public override float IntelModifier(Faction f)
        {
            List<Settlement> settlements = WorldComponent_Dominion.Instance.Cache.SettlementsOf(f);
            foreach (PlanetTile t in playerOutposts)
                foreach (Settlement s in settlements)
                    if (Find.WorldGrid.ApproxDistanceInTiles(t, s.Tile) < WatchTiles) return 8f;
            return 0f;
        }
    }

    /// <summary>Visit Settlements: time your colonists spend inside a faction's settlement builds knowledge and ties.</summary>
    public class Integration_VisitSettlements : DominionIntegration
    {
        public override string PackageId => "alt4s.VisitSettlements";
        public override string Label => "Visit Settlements";

        protected internal override void Bind(Binder b)
        {
            Type data = b.Type("VisitSettlements.WorldComponent_SettlementData");
            b.Field(data, "settlementMaps");
        }

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Map map in Find.Maps)
            {
                if (!(map.Parent is Settlement s) || s.Faction == null || s.Faction.IsPlayer) continue;
                if (map.mapPawns.FreeColonistsSpawnedCount == 0) continue;
                FactionState fs = comp.GetState(s.Faction);
                fs.playerIntel = Mathf.Min(100f, fs.playerIntel + 2f);
                comp.GetRelation(s.Faction, Faction.OfPlayer).favour += 0.5f;
                BroadcastSystem.AddCulture(s.Faction, 0.5f);
            }
        }
    }

    /// <summary>RimToll: resentment over tolls sours a faction's view of you.</summary>
    public class Integration_RimToll : DominionIntegration
    {
        public override string PackageId => "forti.rimtoll";
        public override string Label => "RimToll";

        private PropertyInfo instance;
        private MethodInfo resentmentFor;

        protected internal override void Bind(Binder b)
        {
            Type state = b.Type("RimToll.GameComponent_TollState");
            instance = b.Property(state, "Instance");
            resentmentFor = b.Method(state, "ResentmentFor", new[] { typeof(Faction) });
            b.Method(state, "MostResentfulFaction");
        }

        public override float OpinionModifier(Faction f, Faction toward)
        {
            if (!toward.IsPlayer) return 0f;
            object s = instance?.GetValue(null);
            if (s == null) return 0f;
            float resentment = (float)resentmentFor.Invoke(s, new object[] { f });
            return -Mathf.Clamp(resentment * 0.2f, 0f, 20f);
        }
    }

    /// <summary>RimCourt: your standing as a judge is known abroad.</summary>
    public class Integration_RimCourt : DominionIntegration
    {
        public override string PackageId => "forti.rimcourt";
        public override string Label => "RimCourt";

        private PropertyInfo instance;
        private PropertyInfo reputation;
        private MethodInfo scoreOf;

        protected internal override void Bind(Binder b)
        {
            Type court = b.Type("RimCourt.Court.CourtManager");
            instance = b.Property(court, "Instance");
            reputation = b.Property(court, "Reputation");
            Type rep = b.Type("RimCourt.Court.JudgeReputation");
            scoreOf = b.Method(rep, "ScoreOf", new[] { typeof(int) });
            b.Type("RimCourt.Court.Betrothal");
        }

        private float cachedScore;
        private int cachedTick = -1;

        public override float OpinionModifier(Faction f, Faction toward)
        {
            if (!toward.IsPlayer) return 0f;
            int now = Find.TickManager.TicksGame;
            if (now != cachedTick)
            {
                cachedTick = now;
                object c = instance?.GetValue(null);
                object rep = c != null ? reputation.GetValue(c) : null;
                cachedScore = rep != null ? (float)scoreOf.Invoke(rep, new object[] { 0 }) : 0f;
            }
            return Mathf.Clamp(cachedScore * 0.1f, -8f, 8f);
        }
    }

    /// <summary>RimMarket: how a faction's traders rate your market carries over into its opinion of you.</summary>
    public class Integration_RimMarket : DominionIntegration
    {
        public override string PackageId => "forti.rimmarket";
        public override string Label => "RimMarket";

        private PropertyInfo instance;
        private MethodInfo opinionOf;
        private PropertyInfo total;

        protected internal override void Bind(Binder b)
        {
            Type manager = b.Type("RimMarket.Market.MarketManager");
            instance = b.Property(manager, "Instance");
            opinionOf = b.Method(manager, "OpinionOf", new[] { typeof(Faction) });
            Type rep = b.Type("RimMarket.Reputation.MarketReputation");
            total = b.Property(rep, "Total");
            b.Type("RimMarket.Routes.TradeRoute");
        }

        public override float OpinionModifier(Faction f, Faction toward)
        {
            if (!toward.IsPlayer) return 0f;
            object m = instance?.GetValue(null);
            object rep = m != null ? opinionOf.Invoke(m, new object[] { f }) : null;
            if (rep == null) return 0f;
            return Mathf.Clamp((float)total.GetValue(rep) * 0.1f, -10f, 10f);
        }
    }

    /// <summary>
    /// InterRim Ballistic Missile: satellite coverage over a faction's capital raises your intel on it; a world on
    /// the brink (doomsday clock triggered) unsettles every faction.
    /// </summary>
    public class Integration_IRBM : DominionIntegration
    {
        public override string PackageId => "kazepsi.irbm";
        public override string Label => "InterRim Ballistic Missile";

        private PropertyInfo registryInstance;
        private MethodInfo isTileObserved;
        private Type clockType;
        private FieldInfo isTriggered;

        protected internal override void Bind(Binder b)
        {
            Type registry = b.Type("IRBM.SatelliteRegistry");
            registryInstance = b.Property(registry, "Instance");
            isTileObserved = b.Method(registry, "IsTileObserved", new[] { typeof(int) });
            clockType = b.Type("IRBM.DoomsdayClockManager");
            isTriggered = b.Field(clockType, "isTriggered");
        }

        private bool worldOnEdge;

        public override void DailyGlobal()
        {
            object clock = Current.Game?.components?.Find(c => clockType.IsInstanceOfType(c));
            worldOnEdge = clock != null && (bool)isTriggered.GetValue(clock);
        }

        public override float IntelModifier(Faction f)
        {
            object reg = registryInstance?.GetValue(null);
            if (reg == null) return 0f;
            PlanetTile tile = DelegationUtility.HomeTile(f);
            return tile.Valid && (bool)isTileObserved.Invoke(reg, new object[] { tile.tileId }) ? 15f : 0f;
        }

        public override float StabilityModifier(Faction f) => worldOnEdge ? -10f : 0f;
    }

    /// <summary>Vanilla Books Expanded: newspapers print Dominion headlines (see Patch_Book_GenerateBook_Newspaper).</summary>
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

    /// <summary>Pillage &amp; Plunder: a looted settlement costs its faction economy and stability while it recovers.</summary>
    public class Integration_PillageAndPlunder : DominionIntegration
    {
        public override string PackageId => "lordfelix.pillageandplunder";
        public override string Label => "Pillage & Plunder";

        private PropertyInfo instance;
        private MethodInfo isPlundered;
        private readonly HashSet<int> known = new HashSet<int>();
        private bool primed;

        protected internal override void OnWorldLoaded()
        {
            known.Clear();
            recovering.Clear();
            primed = false;
        }
        private readonly Dictionary<int, int> recovering = new Dictionary<int, int>();

        protected internal override void Bind(Binder b)
        {
            Type manager = b.Type("PillageAndPlunder.PlunderedSettlementManager");
            instance = b.Property(manager, "Instance");
            isPlundered = b.Method(manager, "IsSettlementPlundered", new[] { typeof(Settlement) });
        }

        public override void DailyGlobal()
        {
            object m = instance?.GetValue(null);
            if (m == null) return;
            recovering.Clear();
            var stillPlundered = new HashSet<int>();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || s.Faction.IsPlayer || !(bool)isPlundered.Invoke(m, new object[] { s })) continue;
                stillPlundered.Add(s.ID);
                recovering[s.Faction.loadID] = (recovering.TryGetValue(s.Faction.loadID, out int n) ? n : 0) + 1;
                if (known.Add(s.ID) && primed)
                {
                    FactionState fs = WorldComponent_Dominion.Instance.GetState(s.Faction);
                    fs.stability = Mathf.Max(0f, fs.stability - 3f);
                    EventBus.Raise(DominionEventDefOf.Dominion_SettlementLooted, s.Faction, null, s.Tile.tileId, detail: s.Label, source: PackageId);
                }
            }
            known.IntersectWith(stillPlundered);
            primed = true;
        }

        public override float EconomyModifier(Faction f) => recovering.TryGetValue(f.loadID, out int n) ? -0.05f * n : 0f;
    }
}
