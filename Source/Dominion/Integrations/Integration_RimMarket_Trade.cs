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
    /// <summary>
    /// RimMarket trade routes feed diplomacy. Once a day the route counters are compared with the day before:
    /// <list type="bullet">
    /// <item>good visits by a route's traders: favour with the player and a small lift to the faction's economy;</item>
    /// <item>convoys sent: a small lift to the faction's economy (trade income);</item>
    /// <item>ambushes: the route's faction holds a grievance against the ambusher, when RimMarket names one;</item>
    /// <item>convoys lost: news, a heavier grievance, and war score if the two are at war.</item>
    /// </list>
    /// Counters are not saved: the first read after a load only records them. All members are optional.
    /// </summary>
    public partial class Integration_RimMarket
    {
        private FieldInfo routeId;
        private FieldInfo routeGoodVisits;
        private FieldInfo routeConvoysSent;
        private FieldInfo routeConvoysLost;
        private FieldInfo routeAmbushes;
        private MethodInfo startAmbush;
        private static FieldInfo convoyRouteId;
        private bool ambushPatched;

        private struct Counters
        {
            public int goodVisits, convoysSent, convoysLost, ambushes;
        }

        private readonly Dictionary<int, Counters> lastCounters = new Dictionary<int, Counters>();

        /// <summary>The faction RimMarket last named as ambushing each route's convoy (route id -> load id).</summary>
        private static readonly Dictionary<int, int> lastAmbusher = new Dictionary<int, int>();

        private void BindTrade(Binder b, Type route)
        {
            const BindingFlags inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            routeId = IntField(route?.GetField("id", inst));
            routeGoodVisits = IntField(route?.GetField("goodVisits", inst));
            routeConvoysSent = IntField(route?.GetField("convoysSent", inst));
            routeConvoysLost = IntField(route?.GetField("convoysLost", inst));
            routeAmbushes = IntField(route?.GetField("ambushes", inst));
            Type convoy = AccessTools.TypeByName("RimMarket.Routes.TradeConvoy");
            convoyRouteId = IntField(convoy?.GetField("routeId", inst));
            Type util = AccessTools.TypeByName("RimMarket.Routes.TradeRouteUtility");
            startAmbush = util?.GetMethod("StartAmbush", BindingFlags.Public | BindingFlags.Static);
            if (startAmbush != null)
            {
                ParameterInfo[] ps = startAmbush.GetParameters();
                if (ps.Length != 2 || !typeof(WorldObject).IsAssignableFrom(ps[0].ParameterType) || ps[1].ParameterType != typeof(Faction))
                    startAmbush = null;
            }
            b.Note(routeId != null && routeGoodVisits != null ? "trade route counters feed favour and economy" : "trade route counters not found");
            b.Note(startAmbush != null && convoyRouteId != null ? "convoy ambushers are known (grievance)" : "ambush hook not found: ambushers stay unknown");
        }

        private static FieldInfo IntField(FieldInfo f) => f != null && f.FieldType == typeof(int) ? f : null;

        protected internal override void OnActivated()
        {
            if (ambushPatched || startAmbush == null || convoyRouteId == null) return;
            try
            {
                DominionMod.Harmony.Patch(startAmbush, postfix: new HarmonyMethod(typeof(Integration_RimMarket), nameof(StartAmbushPostfix)));
                ambushPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook RimMarket convoy ambushes: " + e.Message);
            }
        }

        protected internal override void OnWorldLoaded()
        {
            lastCounters.Clear();
            lastAmbusher.Clear();
        }

        /// <summary>Harmony postfix on TradeRouteUtility.StartAmbush: remembers who ambushed the route's convoy.</summary>
        public static void StartAmbushPostfix(WorldObject convoy, Faction attacker)
        {
            try
            {
                if (convoy == null || attacker == null || convoyRouteId == null) return;
                lastAmbusher[(int)convoyRouteId.GetValue(convoy)] = attacker.loadID;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("RimMarket ambush hook failed: " + e.Message, 0x7A41);
            }
        }

        private static int Read(FieldInfo f, object o) => f != null ? (int)f.GetValue(o) : 0;

        /// <summary>Compares each route's counters with yesterday's and turns the difference into diplomacy.</summary>
        private void TrackTrade(IEnumerable routeList)
        {
            if (routeId == null || routeFaction == null) return;
            try
            {
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                if (comp == null) return;
                var alive = new HashSet<int>();
                foreach (object r in routeList)
                {
                    if (r == null) continue;
                    int id = (int)routeId.GetValue(r);
                    alive.Add(id);
                    var now = new Counters
                    {
                        goodVisits = Read(routeGoodVisits, r),
                        convoysSent = Read(routeConvoysSent, r),
                        convoysLost = Read(routeConvoysLost, r),
                        ambushes = Read(routeAmbushes, r),
                    };
                    bool known = lastCounters.TryGetValue(id, out Counters before);
                    lastCounters[id] = now;
                    if (!known || !(routeFaction.GetValue(r) is Faction f) || !DominionScheduler.IsSimulated(f)) continue;
                    Apply(f, r, now, before, id, comp);
                }
                if (alive.Count != lastCounters.Count)
                {
                    var gone = new List<int>();
                    foreach (int id in lastCounters.Keys)
                        if (!alive.Contains(id)) gone.Add(id);
                    foreach (int id in gone)
                    {
                        lastCounters.Remove(id);
                        lastAmbusher.Remove(id);
                    }
                }
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Reading RimMarket trade routes failed: " + e.Message, 0x7A42);
            }
        }

        private void Apply(Faction f, object route, Counters now, Counters before, int id, WorldComponent_Dominion comp)
        {
            int visits = Mathf.Max(0, now.goodVisits - before.goodVisits);
            int sent = Mathf.Max(0, now.convoysSent - before.convoysSent);
            int ambushed = Mathf.Max(0, now.ambushes - before.ambushes);
            int lost = Mathf.Max(0, now.convoysLost - before.convoysLost);

            if (visits > 0)
            {
                comp.GetRelation(f, Faction.OfPlayer).favour += Mathf.Min(6f, 2f * visits);
                EconomyEffects.Shock(f, Mathf.Min(0.03f, 0.01f * visits), destroyGoods: false, days: 10);
            }
            if (sent > 0)
                EconomyEffects.Shock(f, Mathf.Min(0.02f, 0.005f * sent), destroyGoods: false, days: 7);
            if (ambushed == 0 && lost == 0) return;

            Faction ambusher = lastAmbusher.TryGetValue(id, out int aid) ? comp.Cache.FactionById(aid) : null;
            if (ambusher == f) ambusher = null;
            if (ambusher != null)
            {
                RelationState view = comp.GetRelation(f, ambusher);
                view.grievance = Mathf.Min(100f, view.grievance + 4f * ambushed + 8f * lost);
                view.opinion = Mathf.Max(-100f, view.opinion - 2f * (ambushed + lost));
                if (lost > 0) WarUtility.AddScore(ambusher, f, 3f * lost);
            }
            if (lost > 0)
            {
                EconomyEffects.Shock(f, -0.01f * Mathf.Min(3, lost), destroyGoods: false, days: 10);
                Settlement s = routeSettlement?.GetValue(route) as Settlement;
                EventBus.Raise(IntegrationEventDefOf.Dominion_ConvoyLost, f, ambusher, s?.Tile.tileId ?? -1,
                    magnitude: lost, detail: s?.Label ?? f.Name, source: PackageId);
            }
        }
    }
}
