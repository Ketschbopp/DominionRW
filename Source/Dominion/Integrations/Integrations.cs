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
                    if (Find.WorldGrid.ApproxDistanceInTiles(t, s.Tile) < WatchTiles) return 8f;
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
            Type route = b.Type("RimMarket.Routes.TradeRoute");
            routes = b.Property(manager, "Routes");
            routeFaction = b.Field(route, "faction");
            routeSettlement = b.Field(route, "settlement");
            routeNextDeparture = b.Field(route, "nextDepartureTick");
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

            // Optional: missile strikes as acts of war.
            missileType = AccessTools.TypeByName("IRBM.WorldObject_Missile");
            onArrived = missileType?.GetMethod("OnArrived", BindingFlags.Public | BindingFlags.Instance);
            b.Note(onArrived != null ? "missile strikes: WorldObject_Missile.OnArrived" : "missile strike hook not found");
        }

        private static Type missileType;
        private MethodInfo onArrived;

        protected internal override void OnActivated()
        {
            if (onArrived == null) return;
            try
            {
                DominionMod.Harmony.Patch(onArrived, prefix: new HarmonyMethod(typeof(Integration_IRBM), nameof(MissileArrivedPrefix)));
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook IRBM missile strikes: " + e.Message);
            }
        }

        /// <summary>
        /// A missile is about to hit a settlement or site: an act of war against its owner (and war if they were at peace),
        /// war score, losses in people and wealth, and a world that thinks less of whoever uses strategic weapons.
        /// </summary>
        public static void MissileArrivedPrefix(WorldObject __instance)
        {
            if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
            try
            {
                Traverse t = Traverse.Create(__instance);
                if (t.Field("isAntiAir").GetValue<bool>() || t.Field("targetSatellite").GetValue() != null || t.Field("targetMissile").GetValue() != null) return;
                Faction attacker = __instance.Faction;
                PlanetTile tile = t.Field("destinationTile").GetValue<PlanetTile>();
                MapParent target = t.Field("targetMapParent").GetValue<MapParent>() ?? (tile.Valid ? Find.WorldObjects.MapParentAt(tile) : null);
                Faction victim = target?.Faction;
                if (attacker == null || victim == null || attacker == victim || victim.Hidden) return;
                object warhead = Traverse.Create(t.Property("MissileComp").GetValue()).Field("warheadDef").GetValue();
                bool strategic = warhead != null && (Traverse.Create(warhead).Field("doomsdayAdvanceMinutes").GetValue<float>() > 0f
                    || (Traverse.Create(warhead).Field("payloadComps").GetValue() is System.Collections.IEnumerable comps
                        && comps.Cast<object>().Any(c => c != null && c.GetType().Name.Contains("Strategic"))));
                MissileStrike(attacker, victim, target, strategic);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("IRBM missile hook failed: " + e.Message, 0x1B31);
            }
        }

        private static void MissileStrike(Faction attacker, Faction victim, MapParent target, bool strategic)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (!attacker.HostileTo(victim) && WarUtility.CanWage(attacker) && WarUtility.CanWage(victim))
                DiplomacyUtility.SetRelationKind(attacker, victim, FactionRelationKind.Hostile, attacker, "Dominion_Reason_MissileStrike".Translate());
            WarUtility.AddScore(attacker, victim, strategic ? 25f : 8f);
            if (!victim.IsPlayer && DominionScheduler.IsSimulated(victim))
            {
                FactionState s = comp.GetState(victim);
                s.stability = Mathf.Max(0f, s.stability - (strategic ? 12f : 4f));
                EconomyEffects.Shock(victim, strategic ? -0.15f : -0.04f);
                IntegrationRegistry.Get<Integration_EconomicsDemography>()?.ModifyPopulation(victim,
                    -Mathf.Clamp(Mathf.RoundToInt(s.population * (strategic ? 0.08f : 0.01f)), 1, strategic ? 120 : 15));
            }
            comp.GetRelation(victim, attacker).grievance += strategic ? 60f : 20f;
            if (strategic)
                // Nobody likes the faction that used a weapon of mass destruction, whoever it was aimed at.
                foreach (Faction other in comp.Cache.Simulated)
                    if (other != attacker && other != victim) comp.GetRelation(other, attacker).favour -= 10f + 10f * comp.GetState(other).personality.honour;
            EventBus.Raise(DominionEventDefOf.Dominion_MissileStrike, attacker, victim, target.Tile.tileId, detail: target.Label,
                magnitude: strategic ? 2f : 1f, source: "kazepsi.irbm");
        }

        private bool worldOnEdge;

        protected internal override void OnWorldLoaded() => worldOnEdge = false;

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
}
