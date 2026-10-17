using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    // Optional mods. Each binds what it needs at startup, and adds its effects through the modifier hooks on
    // DominionIntegration and its daily hook. Everything goes through reflection, so a missing or changed mod
    // only switches its own integration off. See docs/DESIGN.md §9h.

    /// <summary>Vanilla Outposts Expanded: your outposts add council weight and watch nearby factions.</summary>
    public class Integration_VanillaOutposts : DominionIntegration
    {
        public override string PackageId => "vanillaexpanded.outposts";
        public override string Label => "Vanilla Outposts Expanded";

        private const float WatchTiles = 15f;
        private Type outpostBase;
        private readonly List<PlanetTile> playerOutposts = new List<PlanetTile>();

        protected internal override void OnWorldLoaded() => playerOutposts.Clear();

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
                    if (DominionGeo.Distance(t, s.Tile) is float d && d >= 0f && d < WatchTiles) return 8f;
            return 0f;
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
            // Resentment runs about 0..1 ("bitter" from 0.6) and fades 0.15 a day.
            return -Mathf.Clamp(resentment * 15f, 0f, 15f);
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
        private PropertyInfo stage;

        protected internal override void Bind(Binder b)
        {
            Type court = b.Type("RimCourt.Court.CourtManager");
            instance = b.Property(court, "Instance");
            reputation = b.Property(court, "Reputation");
            Type rep = b.Type("RimCourt.Court.JudgeReputation");
            scoreOf = b.Method(rep, "ScoreOf", new[] { typeof(int) });
            stage = b.Property(rep, "Stage");
            b.Type("RimCourt.Court.Betrothal");
        }

        private float cachedScore;
        private int cachedStage;
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
                cachedScore = 0f;
                cachedStage = 0;
                if (rep != null)
                {
                    cachedStage = (int)stage.GetValue(rep);
                    cachedScore = (float)scoreOf.Invoke(rep, new object[] { cachedStage });
                }
            }
            // Stages: 0 just, 1 harsh, 2 venal, 3 weak. A just judge is respected, a corrupt or weak one is not;
            // harshness impresses the proud and puts off the gentle.
            float sign;
            switch (cachedStage)
            {
                case 0: sign = 1f; break;
                case 1:
                    WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                    sign = comp != null && comp.TryGetState(f, out FactionState s) ? (s.personality.aggression - 0.5f) * 2f : 0f;
                    break;
                default: sign = -1f; break;
            }
            return Mathf.Clamp(sign * cachedScore * 0.1f, -8f, 8f);
        }
    }

    /// <summary>RimMarket: how a faction's traders rate your market carries over into its opinion of you.</summary>
    public partial class Integration_RimMarket : DominionIntegration
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
            Type route = b.Type("RimMarket.Routes.TradeRoute");
            routes = b.Property(manager, "Routes");
            routeFaction = b.Field(route, "faction");
            routeSettlement = b.Field(route, "settlement");
            routeNextDeparture = b.Field(route, "nextDepartureTick");
            BindTrade(b, route);
        }

        private PropertyInfo routes;
        private FieldInfo routeFaction;
        private FieldInfo routeSettlement;
        private FieldInfo routeNextDeparture;

        /// <summary>
        /// Trade routes stop while trade is impossible: war, an embargo either way, closed borders, or the route's town
        /// blockaded or occupied by someone else. Convoys wait a day at a time and resume on their own afterwards.
        /// </summary>
        public override void DailyGlobal()
        {
            object m = instance?.GetValue(null);
            if (m == null || !(routes.GetValue(m) is System.Collections.IEnumerable list)) return;
            TrackTrade(list);
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction player = Faction.OfPlayer;
            int next = Find.TickManager.TicksGame + GenDate.TicksPerDay;
            foreach (object r in list)
            {
                if (!(routeFaction.GetValue(r) is Faction f)) continue;
                Settlement s = routeSettlement.GetValue(r) as Settlement;
                bool blocked = f.HostileTo(player)
                               || (comp.TryGetRelation(player, f)?.embargo ?? false) || (comp.TryGetRelation(f, player)?.embargo ?? false)
                               || (comp.TryGetRelation(player, f)?.border == BorderStatus.Closed) || (comp.TryGetRelation(f, player)?.border == BorderStatus.Closed)
                               || (s != null && (s.Faction != f || comp.Wars.blockades.Any(b => b.settlement == s)
                                                 || WarbandUtility.All().Any(w => w.Blockading && w.Tile == s.Tile)));
                if (!blocked) continue;
                if ((int)routeNextDeparture.GetValue(r) < next) routeNextDeparture.SetValue(r, next);
            }
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
}
