using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Who a world event involves besides the faction it happens to.</summary>
    public enum WorldEventPartner { None, Neighbour, Rival, TradePartner, MostStable }

    /// <summary>
    /// Conditions and effects of a world event, set in XML on a <see cref="DominionEventDef"/>. Effects apply whenever
    /// the event is raised (including from the dev menu), so a new world event needs no code.
    /// </summary>
    public class WorldEventProps
    {
        /// <summary>Mean days between occurrences for one faction that meets the conditions.</summary>
        public float mtbDays = 500f;

        // Conditions
        public FloatRange stability = new FloatRange(0f, 100f);
        public FloatRange legitimacy = new FloatRange(0f, 100f);
        public FloatRange economy = new FloatRange(0f, 1000f);
        public int minPopulation;
        public int minSettlements = 1;
        public TechLevel minTechLevel = TechLevel.Undefined;
        public bool needsWar;
        public bool needsPeace;
        public bool needsIdeology;

        /// <summary>The second faction involved, chosen when the event fires.</summary>
        public WorldEventPartner partner = WorldEventPartner.None;

        /// <summary>The event only happens if a partner can be found.</summary>
        public bool partnerRequired;

        /// <summary>Picks one of the faction's settlements as the place it happens.</summary>
        public bool atSettlement;

        // Effects on the faction
        public float stabilityOffset;
        public float legitimacyOffset;
        public float economyFactor = 1f;

        /// <summary>Share of the adult population lost (negative) or gained (positive), through Economics &amp; Demography.</summary>
        public float populationShare;

        /// <summary>Lost population moves to the partner instead of vanishing.</summary>
        public bool populationToPartner;

        public Bloc bloc;
        public float blocShift;

        // Effects involving the partner
        public float favourTowardPartner;
        public float partnerFavourTowardFaction;
        public float partnerStabilityOffset;

        /// <summary>Days the faction counts as in crisis: others (and you) can send aid.</summary>
        public int crisisDays;

        /// <summary>Chance to spread to each faction with a settlement within <see cref="spreadDistance"/> tiles.</summary>
        public float spreadChance;
        public float spreadDistance = 25f;
    }

    /// <summary>Rolls world events once a day per faction and applies their effects.</summary>
    public class WorldEventSystem : SimSystem
    {
        public override int Order => 40;

        /// <summary>No faction has two world events within this many days.</summary>
        private const int FactionCooldownDays = 8;

        /// <summary>The same event does not repeat for a faction within this many days.</summary>
        private const int RepeatCooldownDays = 60;

        /// <summary>Raised events carry this source when they spread, so they do not spread again.</summary>
        private const string SpreadSource = "spread";

        private static List<DominionEventDef> worldDefs;

        private static List<DominionEventDef> WorldDefs
        {
            get
            {
                if (worldDefs == null)
                {
                    worldDefs = new List<DominionEventDef>();
                    foreach (DominionEventDef d in DefDatabase<DominionEventDef>.AllDefsListForReading)
                        if (d.world != null) worldDefs.Add(d);
                }
                return worldDefs;
            }
        }

        public override void DailyFaction(Faction f)
        {
            DominionSettings settings = DominionMod.Settings;
            if (!settings.worldEvents) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            int now = Find.TickManager.TicksGame;
            if (now - s.lastWorldEventTick < FactionCooldownDays * GenDate.TicksPerDay) return;

            List<DominionEventDef> defs = WorldDefs;
            for (int i = 0; i < defs.Count; i++)
            {
                DominionEventDef def = defs[i];
                WorldEventProps w = def.world;
                if (def == s.lastWorldEvent && now - s.lastWorldEventTick < RepeatCooldownDays * GenDate.TicksPerDay) continue;
                if (!Rand.Chance(settings.worldEventFrequency / Mathf.Max(1f, w.mtbDays))) continue;
                if (!Conditions(f, s, w)) continue;
                Faction partner = PickPartner(f, w.partner);
                if (w.partnerRequired && partner == null) continue;
                Fire(def, f, partner, null);
                return; // one per faction per day
            }
        }

        public static bool Conditions(Faction f, FactionState s, WorldEventProps w)
        {
            if (!w.stability.Includes(s.stability) || !w.legitimacy.Includes(s.legitimacy) || !w.economy.Includes(s.economyIndex)) return false;
            if (s.population < w.minPopulation || s.settlementCount < w.minSettlements) return false;
            if (f.def.techLevel < w.minTechLevel) return false;
            if (w.needsWar && s.activeWars == 0) return false;
            if (w.needsPeace && s.activeWars > 0) return false;
            if (w.needsIdeology && (!ModsConfig.IdeologyActive || f.ideos?.PrimaryIdeo == null)) return false;
            return true;
        }

        public static void Fire(DominionEventDef def, Faction f, Faction partner, string source)
        {
            int tile = -1;
            string detail = null;
            if (def.world.atSettlement)
            {
                List<Settlement> list = WorldComponent_Dominion.Instance.Cache.SettlementsOf(f);
                if (list.Count > 0)
                {
                    Settlement at = list.RandomElement();
                    tile = at.Tile.tileId;
                    detail = at.Label;
                }
            }
            EventBus.Raise(def, f, partner, tile, source: source, detail: detail);
        }

        public static Faction PickPartner(Faction f, WorldEventPartner kind)
        {
            if (kind == WorldEventPartner.None) return null;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction best = null;
            float bestScore = float.MinValue;
            foreach (Faction g in comp.Cache.Simulated)
            {
                if (g == f) continue;
                RelationState r = comp.TryGetRelation(f, g);
                float score;
                switch (kind)
                {
                    case WorldEventPartner.Neighbour:
                        if (r == null || r.distance < 0f || g.HostileTo(f)) continue;
                        score = -r.distance;
                        break;
                    case WorldEventPartner.Rival:
                        if (r == null) continue;
                        score = -r.opinion;
                        break;
                    case WorldEventPartner.TradePartner:
                        if (!HasTrade(comp, f, g)) continue;
                        score = Rand.Value;
                        break;
                    case WorldEventPartner.MostStable:
                        if (g.HostileTo(f)) continue;
                        score = comp.GetState(g).stability - (r != null && r.distance >= 0f ? r.distance * 0.1f : 10f);
                        break;
                    default:
                        continue;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = g;
                }
            }
            return best;
        }

        private static bool HasTrade(WorldComponent_Dominion comp, Faction a, Faction b)
        {
            foreach (Treaty t in comp.Treaties.All)
                if (t.def.tradeBonus > 0f && t.Between(a, b)) return true;
            return false;
        }

        public override void OnEvent(DominionEvent ev)
        {
            WorldEventProps w = ev.def.world;
            Faction f = ev.primary;
            if (w == null || f == null || f.IsPlayer) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            int now = Find.TickManager.TicksGame;
            s.lastWorldEventTick = now;
            s.lastWorldEvent = ev.def;

            s.stability = Mathf.Clamp(s.stability + w.stabilityOffset, 0f, 100f);
            s.legitimacy = Mathf.Clamp(s.legitimacy + w.legitimacyOffset, 0f, 100f);
            s.economyIndex = Mathf.Clamp(s.economyIndex * w.economyFactor, 10f, 300f);
            if (w.blocShift != 0f)
            {
                s.blocs[(int)w.bloc] = Mathf.Max(0f, s.blocs[(int)w.bloc] + w.blocShift);
                s.NormalizeBlocs();
            }

            Faction partner = ev.secondary;
            if (w.populationShare != 0f)
            {
                int amount = Mathf.Clamp(Mathf.RoundToInt(s.population * Mathf.Abs(w.populationShare)), 1, 60);
                var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
                if (w.populationShare > 0f) ed?.ModifyPopulation(f, amount);
                else
                {
                    ed?.ModifyPopulation(f, -amount);
                    if (w.populationToPartner && partner != null && !partner.IsPlayer) ed?.ModifyPopulation(partner, amount);
                }
            }
            if (partner != null && !partner.IsPlayer)
            {
                if (w.favourTowardPartner != 0f) comp.GetRelation(f, partner).favour += w.favourTowardPartner;
                if (w.partnerFavourTowardFaction != 0f) comp.GetRelation(partner, f).favour += w.partnerFavourTowardFaction;
                if (w.partnerStabilityOffset != 0f)
                {
                    FactionState ps = comp.GetState(partner);
                    ps.stability = Mathf.Clamp(ps.stability + w.partnerStabilityOffset, 0f, 100f);
                }
            }

            if (w.crisisDays > 0)
            {
                s.crisis = ev.def;
                s.crisisUntilTick = Mathf.Max(s.crisisUntilTick, now + w.crisisDays * GenDate.TicksPerDay);
                AIAid(f, comp);
            }

            if (w.spreadChance > 0f && ev.source != SpreadSource && DominionMod.Settings.plagueSpread)
                Spread(ev.def, f, comp);
        }

        /// <summary>Friends with money to spare send help to a faction in crisis.</summary>
        private static void AIAid(Faction f, WorldComponent_Dominion comp)
        {
            foreach (Faction donor in comp.Cache.Simulated)
            {
                if (donor == f || donor.HostileTo(f)) continue;
                RelationState r = comp.TryGetRelation(donor, f);
                FactionState ds = comp.GetState(donor);
                if (r == null || r.opinion < 40f || ds.economyIndex < 90f || ds.InCrisis) continue;
                if (!Rand.Chance(0.15f + ds.personality.honour * 0.2f)) continue;
                comp.GetRelation(f, donor).favour += 8f;
                FactionState s = comp.GetState(f);
                s.stability = Mathf.Min(100f, s.stability + 2f);
                ds.economyIndex *= 0.99f;
                EventBus.Raise(DominionEventDefOf.Dominion_AidSent, donor, f);
                return; // one donor makes the news
            }
        }

        private static void Spread(DominionEventDef def, Faction from, WorldComponent_Dominion comp)
        {
            foreach (Faction g in comp.Cache.Simulated)
            {
                if (g == from) continue;
                RelationState r = comp.TryGetRelation(from, g);
                if (r == null || r.distance < 0f || r.distance > def.world.spreadDistance) continue;
                if (comp.GetState(g).InCrisis || !Rand.Chance(def.world.spreadChance)) continue;
                Fire(def, g, null, SpreadSource);
            }
        }
    }
}
