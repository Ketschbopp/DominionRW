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
    /// <summary>
    /// Simple Leadership. Its power events and Dominion's politics are the same story told once (design §4.4):
    /// <list type="bullet">
    /// <item>Dominion coups and revolutions start a power struggle at the capital, riots start rioting there, famines a
    /// famine, sanctions decrees and embargoes by most of the world sanction the faction, and settlements a warband is
    /// marching on fortify.</item>
    /// <item>Its own events (at settlements as well as faction-wide) shake Dominion's politics and economy, unless Dominion
    /// started them.</item>
    /// </list>
    /// </summary>
    public class Integration_SimpleLeadership : DominionIntegration
    {
        public override string PackageId => "pb3n.SimpleLeadership";
        public override string Label => "Simple Leadership";

        private FieldInfo instance;
        private MethodInfo getBaseLeader;
        private FieldInfo activeEvents;
        private MethodInfo startPowerEvent;
        private MethodInfo getTarget;
        private FieldInfo eventDef;
        private Type powerEventDefType;

        // Events already seen (by reference) and those Dominion started itself. Not saved: the first day after loading
        // only records what is going on.
        private readonly HashSet<object> known = new HashSet<object>();
        private readonly HashSet<object> ours = new HashSet<object>();
        private bool primed;

        protected internal override void OnWorldLoaded()
        {
            known.Clear();
            ours.Clear();
            primed = false;
        }

        protected internal override void Bind(Binder b)
        {
            Type tracker = b.Type("SimpleLeadership.WorldComponent_LeaderTracker");
            instance = b.Field(tracker, "Instance");
            getBaseLeader = b.Method(tracker, "GetBaseLeader", new[] { typeof(Settlement) });
            activeEvents = b.Field(tracker, "activeEvents");
            startPowerEvent = b.Method(tracker, "StartPowerEvent");
            Type ev = b.Type("SimpleLeadership.PowerEventBase");
            getTarget = b.Method(ev, "GetTarget", Type.EmptyTypes);
            eventDef = b.Field(ev, "def");
            powerEventDefType = b.Type("SimpleLeadership.PowerEventDef");
        }

        private object Tracker => instance?.GetValue(null);

        /// <summary>The leader Simple Leadership assigned to a settlement, if any.</summary>
        public Pawn GovernorOf(Settlement s)
        {
            object t = Tracker;
            return t == null || s == null ? null : getBaseLeader.Invoke(t, new object[] { s }) as Pawn;
        }

        private IList Events => Tracker != null ? activeEvents.GetValue(Tracker) as IList : null;

        /// <summary>Starts one of its power events (SL_PowerStruggle, SL_Rioting...) on a settlement or a faction.</summary>
        public void Start(string defName, object target)
        {
            object t = Tracker;
            if (t == null || target == null || powerEventDefType == null) return;
            Def def = GenDefDatabase.GetDefSilentFail(powerEventDefType, defName, false);
            if (def == null) return;
            try
            {
                IList list = Events;
                // SL may end the target's running events before adding the new one, so compare contents, not counts.
                var snapshot = new HashSet<object>();
                if (list != null)
                    foreach (object e in list)
                        snapshot.Add(e);
                startPowerEvent.Invoke(t, new object[] { def, new object[] { target } });
                // Whatever it added is ours: Dominion already applied its own effects.
                list = Events;
                if (list != null)
                    foreach (object e in list)
                    {
                        if (e == null || snapshot.Contains(e)) continue;
                        ours.Add(e);
                        known.Add(e);
                    }
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Starting a Simple Leadership power event failed: " + e.Message, 0x51A1);
            }
        }

        private bool AlreadyRunning(string defName, object target)
        {
            IList list = Events;
            if (list == null) return false;
            foreach (object e in list)
                if (e != null && (eventDef.GetValue(e) as Def)?.defName == defName && getTarget.Invoke(e, null) == target) return true;
            return false;
        }

        // ------------------------------------------------------------------ Dominion -> Simple Leadership

        public override void OnEvent(DominionEvent ev)
        {
            Faction f = ev.primary;
            if (f == null || f.IsPlayer || ev.source == PackageId) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Settlement capital = comp.TryGetState(f, out FactionState s) ? s.capital : null;
            DominionEventDef def = ev.def;
            if (def == DominionEventDefOf.Dominion_Coup || def == DominionEventDefOf.Dominion_Revolution)
            {
                if (capital != null) Start("SL_PowerStruggle", capital);
            }
            else if (def == DominionEventDefOf.Dominion_Riots)
            {
                if (capital != null && !AlreadyRunning("SL_Rioting", capital)) Start("SL_Rioting", capital);
            }
            else if (def.defName == "Dominion_Famine")
            {
                Settlement at = ev.tile >= 0 ? Find.WorldObjects.SettlementAt(new PlanetTile(ev.tile)) : capital;
                if (at != null && at.Faction == f) Start("SL_Famine", at);
            }
            else if (def == DominionEventDefOf.Dominion_WarbandMarching && ev.secondary != null && !ev.secondary.IsPlayer)
            {
                // The town in the warband's path digs in.
                Settlement target = !ev.detail.NullOrEmpty() ? comp.Cache.SettlementsOf(ev.secondary).Find(x => x.Label == ev.detail) : null;
                if (target != null && !AlreadyRunning("SL_Fortifying", target)) Start("SL_Fortifying", target);
            }
            else if (def == DominionEventDefOf.Dominion_EmbargoDeclared && ev.secondary != null && !ev.secondary.IsPlayer)
            {
                // Under embargo from most of the world, a faction cannot trade at all.
                Faction target = ev.secondary;
                int embargoes = EconomyEffects.EmbargoesAgainst(target, comp);
                if (embargoes * 2 > comp.Cache.Simulated.Count && !AlreadyRunning("SL_Sanctioned", target)) Start("SL_Sanctioned", target);
            }
        }

        // ------------------------------------------------------------------ Simple Leadership -> Dominion

        public override void DailyGlobal()
        {
            IList list = Events;
            if (list == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var alive = new HashSet<object>();
            var fresh = new List<(object ev, string def, Faction f, Settlement s)>();
            foreach (object e in list)
            {
                if (e == null) continue;
                alive.Add(e);
                if (!known.Add(e) || !primed || ours.Contains(e)) continue;
                object target = getTarget.Invoke(e, null);
                Settlement s = target as Settlement;
                Faction f = s?.Faction ?? target as Faction;
                if (f == null || !DominionScheduler.IsSimulated(f)) continue;
                fresh.Add((e, (eventDef.GetValue(e) as Def)?.defName, f, s));
            }
            known.IntersectWith(alive);
            ours.IntersectWith(alive);
            primed = true;

            // SL starts a power struggle in every town of a faction when its leader dies: count that once per faction.
            var struggles = new HashSet<Faction>();
            foreach ((object _, string defName, Faction f, Settlement s) in fresh)
            {
                FactionState st = comp.GetState(f);
                switch (defName)
                {
                    case "SL_PowerStruggle":
                    case "SL_PowerVoid":
                        if (!struggles.Add(f)) break;
                        st.stability = Mathf.Max(0f, st.stability - 4f);
                        st.legitimacy = Mathf.Max(0f, st.legitimacy - 5f);
                        EventBus.Raise(DominionEventDefOf.Dominion_PowerStruggle, f, tile: s?.Tile.tileId ?? -1, detail: s?.Label, source: PackageId);
                        break;
                    case "SL_Rioting":
                        st.stability = Mathf.Max(0f, st.stability - 3f);
                        EventBus.Raise(DominionEventDefOf.Dominion_Riots, f, magnitude: 0.5f, source: PackageId);
                        break;
                    case "SL_Famine":
                        EconomyEffects.Shock(f, -0.03f, days: 6);
                        st.stability = Mathf.Max(0f, st.stability - 2f);
                        break;
                    case "SL_Sanctioned":
                        EconomyEffects.Shock(f, -0.05f, destroyGoods: false);
                        break;
                    case "SL_Fortifying":
                    case "SL_Support":
                    case "SL_Reinforcements":
                        st.militaryStrength *= 1.05f;
                        break;
                }
            }
        }
    }
}
