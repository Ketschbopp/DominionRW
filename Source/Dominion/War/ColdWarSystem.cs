using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Rivalry short of war. Two peers who loathe each other enter a cold war: an arms race, propaganda, border
    /// skirmishes between neighbours, and money for whoever is fighting the rival (proxy wars). A cold war ends when
    /// relations thaw, or when it turns hot.
    /// </summary>
    public class ColdWarSystem : SimSystem
    {
        public override int Order => 34;

        public const float StartOpinion = -45f;
        public const float ThawOpinion = -20f;

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        public static bool InColdWar(Faction a, Faction b) => Comp.TryGetRelation(a, b)?.ColdWar ?? false;

        public static void Begin(Faction a, Faction b)
        {
            if (a == null || b == null || a == b || a.HostileTo(b) || InColdWar(a, b)) return;
            int now = Find.TickManager.TicksGame;
            Comp.GetRelation(a, b).coldWarSince = now;
            Comp.GetRelation(b, a).coldWarSince = now;
            EventBus.Raise(DominionEventDefOf.Dominion_ColdWarBegan, a, b);
        }

        public static void End(Faction a, Faction b, bool escalated)
        {
            RelationState ab = Comp.TryGetRelation(a, b), ba = Comp.TryGetRelation(b, a);
            if (ab == null || !ab.ColdWar) return;
            ab.coldWarSince = -1;
            if (ba != null) ba.coldWarSince = -1;
            if (!escalated) EventBus.Raise(DominionEventDefOf.Dominion_ColdWarEnded, a, b);
        }

        public override void OnEvent(DominionEvent ev)
        {
            // A cold war that turns hot is simply a war.
            if (ev.def == DominionEventDefOf.Dominion_WarDeclared && ev.primary != null && ev.secondary != null)
                End(ev.primary, ev.secondary, escalated: true);
        }

        public override void DailyFaction(Faction f)
        {
            WorldComponent_Dominion comp = Comp;
            FactionState s = comp.GetState(f);
            List<Faction> sim = comp.Cache.Simulated;
            int coldWars = 0;
            bool enabled = DominionMod.Settings.coldWars;

            for (int i = 0; i < sim.Count; i++)
                coldWars += Pair(f, sim[i], s, comp, enabled) ? 1 : 0;
            Faction player = Faction.OfPlayerSilentFail;
            if (player != null) coldWars += Pair(f, player, s, comp, enabled) ? 1 : 0;
            s.coldWars = coldWars;
        }

        /// <summary>Daily for one pair, seen from <paramref name="f"/>. True if they are in a cold war afterwards.</summary>
        private static bool Pair(Faction f, Faction g, FactionState s, WorldComponent_Dominion comp, bool enabled)
        {
            if (g == f) return false;
            RelationState r = comp.TryGetRelation(f, g);
            if (r == null) return false;
            RelationState back = comp.TryGetRelation(g, f);

            if (r.ColdWar)
            {
                if (!enabled || f.HostileTo(g) || g.defeated)
                {
                    End(f, g, escalated: f.HostileTo(g));
                    return false;
                }
                // Thaw: both sides have warmed up.
                if (r.opinion > ThawOpinion && (back == null || back.opinion > ThawOpinion) && !g.IsPlayer && Rand.Chance(0.1f))
                {
                    End(f, g, escalated: false);
                    return false;
                }
                Rivalry(f, g, s, r, comp);
                return true;
            }

            // Only the lower id starts a cold war between two AI factions, so each pair rolls once a day.
            if (!enabled || g.IsPlayer || f.loadID > g.loadID || f.HostileTo(g) || !DiplomacyUtility.AiMayChange(f, g)) return false;
            if (r.opinion > StartOpinion || back == null || back.opinion > StartOpinion) return false;
            if (f.RelationKindWith(g) == FactionRelationKind.Ally) return false;
            float ratio = WarUtility.Strength(f) / WarUtility.Strength(g);
            if (ratio < 0.5f || ratio > 2f) return false; // a rivalry is between peers
            if (!Rand.Chance(0.02f)) return false;
            Begin(f, g);
            return true;
        }

        /// <summary>What a cold war does each day.</summary>
        private static void Rivalry(Faction f, Faction g, FactionState s, RelationState r, WorldComponent_Dominion comp)
        {
            s.blocs[(int)Bloc.Hawks] += 0.002f;
            s.NormalizeBlocs();

            // Arms race: both build up, and it is noticed.
            if (Rand.Chance(0.03f) && (g.IsPlayer || f.loadID < g.loadID))
                EventBus.Raise(DominionEventDefOf.Dominion_ArmsRace, f, g);

            // Propaganda battles.
            if (Rand.Chance(0.05f))
            {
                s.stability = Mathf.Max(0f, s.stability - 1f);
                r.grievance = Mathf.Min(100f, r.grievance + 2f);
            }

            // Border skirmishes between neighbours.
            if (r.distance >= 0f && r.distance < RelationsSystem.NeighbourDistance && Rand.Chance(0.02f) && (g.IsPlayer || f.loadID < g.loadID))
            {
                comp.GetRelation(g, f).grievance += 6f;
                r.grievance += 6f;
                EventBus.Raise(DominionEventDefOf.Dominion_BorderSkirmish, f, g, detail: null);
            }

            // Proxy war: fund whoever is fighting the rival.
            if (Rand.Chance(0.03f))
            {
                foreach (War w in comp.Wars.wars)
                {
                    if (!w.Involves(g)) continue;
                    Faction proxy = w.Other(g);
                    if (proxy == f || proxy.IsPlayer || proxy.HostileTo(f)) continue;
                    WarUtility.AddScore(proxy, g, 6f);
                    comp.GetRelation(proxy, f).favour += 5f;
                    // Real money: silver from the sponsor's stockpile to the proxy's.
                    EconomyEffects.Transfer(f, proxy, Mathf.RoundToInt(Mathf.Clamp(s.wealth * 0.01f, 200f, 1500f)));
                    EventBus.Raise(DominionEventDefOf.Dominion_ProxyWar, f, g, detail: proxy.Name);
                    break;
                }
            }
        }
    }
}
