using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    public static class DiplomacyHelpers
    {
        /// <summary>Silver in the player's home maps that colonists could hand over.</summary>
        // Read by action buttons every frame; counting walks every silver stack on every home map.
        private static readonly UICache<int> silverCache = new UICache<int>(CountSilver);

        /// <summary>Silver on home maps, cached for UI (up to half a second old).</summary>
        public static int AvailableSilver() => silverCache.Value;

        /// <summary>Silver on home maps, counted now. Use before actually spending.</summary>
        public static int AvailableSilverFresh()
        {
            silverCache.Invalidate();
            return silverCache.Value;
        }

        private static int CountSilver()
        {
            int total = 0;
            foreach (Thing t in SilverStacks())
                total += t.stackCount;
            return total;
        }

        /// <summary>Removes silver from the player's home maps. Returns how much was taken.</summary>
        public static int TakeSilver(int amount)
        {
            silverCache.Invalidate();
            int taken = 0;
            foreach (Thing t in SilverStacks())
            {
                if (taken >= amount) break;
                int n = Mathf.Min(amount - taken, t.stackCount);
                t.SplitOff(n).Destroy();
                taken += n;
            }
            return taken;
        }

        private static List<Thing> SilverStacks()
        {
            var list = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
                    if (!t.IsForbidden(Faction.OfPlayer) && t.Spawned)
                        list.Add(t);
            }
            return list;
        }

        /// <summary>Drops silver from a faction near the player's capital (or any home).</summary>
        /// <summary>Drops silver at the capital by pod. False if there is no home map to deliver to.</summary>
        public static bool DropSilver(int amount)
        {
            Map map = WorldComponent_Dominion.Instance?.Player.capital?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null || amount <= 0) return false;
            var things = new List<Thing>();
            while (amount > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.Min(amount, ThingDefOf.Silver.stackLimit);
                amount -= silver.stackCount;
                things.Add(silver);
            }
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, things, 110, false, false, true, false, true, null);
            return true;
        }

        /// <summary>The player's strength on the same scale as factions' military strength.</summary>
        public static float PlayerStrength()
        {
            int colonists = 0;
            float wealth = 0f;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                colonists += map.mapPawns.FreeColonistsCount;
                wealth += map.wealthWatcher.WealthTotal;
            }
            return (colonists * 15f + wealth / 2000f) * MilitarySystem.TechFactor(Faction.OfPlayer.def.techLevel);
        }

        /// <summary>Chance faction <paramref name="f"/> accepts treaty <paramref name="def"/> proposed by the player.</summary>
        public static float TreatyAcceptance(TreatyDef def, Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState view = comp.GetRelation(f, Faction.OfPlayer);
            if (view.opinion < def.minOpinion) return 0f;
            if (def.maxDistance > 0f && (view.distance < 0f || view.distance > def.maxDistance)) return 0f;
            AIPersonality p = comp.GetState(f).personality;
            float chance = def.aiAcceptBase + view.opinion / 200f
                           + p.greed * def.greedWeight + p.honour * def.honourWeight + p.paranoia * def.paranoiaWeight
                           + LeadershipUtility.AcceptanceBonus()
                           + (def.federation ? FederationUtility.JoinBonus(f) : 0f);
            return Mathf.Clamp(chance, 0.02f, 0.95f);
        }
    }
}
