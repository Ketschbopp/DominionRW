using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>War goals, scoring, occupation, joining wars and capitulation. State lives in <see cref="WarRegistry"/>.</summary>
    public static class WarUtility
    {
        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        public static WarRegistry Registry => WorldComponent_Dominion.Instance.Wars;

        /// <summary>Factions that can wage a Dominion war: the player and simulated factions, never permanent enemies or quest factions.</summary>
        public static bool CanWage(Faction f) =>
            f != null && !f.def.permanentEnemy && !f.temporary && !f.Hidden && !f.defeated && (f.IsPlayer || f.def.humanlikeFaction);

        public static string GoalLabel(WarGoal g) => ("Dominion_WarGoal_" + g).Translate();

        public static string GoalDescription(WarGoal g) => ("Dominion_WarGoal_" + g + "_Desc").Translate();

        // ---------------------------------------------------------------- strength

        /// <summary>Military strength on the shared scale, the player's included.</summary>
        public static float Strength(Faction f)
        {
            if (f == null) return 1f;
            if (f.IsPlayer) return Mathf.Max(1f, DiplomacyHelpers.PlayerStrength());
            return Mathf.Max(1f, Comp.GetState(f).militaryStrength);
        }

        // ---------------------------------------------------------------- goals

        /// <summary>The goal an AI attacker picks, from its personality and the situation.</summary>
        public static WarGoal ChooseGoal(Faction attacker, Faction defender)
        {
            WorldComponent_Dominion comp = Comp;
            FactionState s = comp.GetState(attacker);
            AIPersonality p = s.personality;
            RelationState r = comp.GetRelation(attacker, defender);
            float ratio = Strength(attacker) / Strength(defender);
            bool defenderIsPlayer = defender.IsPlayer;
            int defenderSettlements = defenderIsPlayer ? 0 : comp.Cache.SettlementsOf(defender).Count;

            var weights = new Dictionary<WarGoal, float>
            {
                [WarGoal.Punitive] = 0.4f + r.grievance / 100f,
                [WarGoal.Tribute] = 0.2f + p.greed,
            };
            if (!defenderIsPlayer && defenderSettlements >= 2)
                weights[WarGoal.Conquest] = 0.2f + p.aggression + (r.distance >= 0f && r.distance < 30f ? 0.4f : 0f) + p.greed * 0.3f;
            if (!defenderIsPlayer && ratio > 1.4f)
                weights[WarGoal.Vassalisation] = 0.3f + (ratio - 1.4f) * 0.5f;
            if (r.ideoCompatibility < -0.3f || (!defenderIsPlayer && comp.GetState(defender).stability < 30f))
                weights[WarGoal.RegimeChange] = 0.3f + s.Bloc(Bloc.Zealots) + Mathf.Max(0f, -r.ideoCompatibility) * 0.5f;
            if (HoldsTakenSettlements(defender, attacker))
                weights[WarGoal.Liberation] = 1.5f;

            float total = 0f;
            foreach (float w in weights.Values) total += w;
            float roll = Rand.Value * total;
            foreach (KeyValuePair<WarGoal, float> kv in weights)
            {
                roll -= kv.Value;
                if (roll <= 0f) return kv.Key;
            }
            return WarGoal.Punitive;
        }

        /// <summary>Goals the player may declare against <paramref name="f"/>.</summary>
        public static IEnumerable<WarGoal> PlayerGoals(Faction f)
        {
            yield return WarGoal.Punitive;
            yield return WarGoal.Tribute;
            yield return WarGoal.Conquest;
            yield return WarGoal.Vassalisation;
            yield return WarGoal.RegimeChange;
            if (HoldsTakenSettlements(f, null)) yield return WarGoal.Liberation;
        }

        /// <summary>True if <paramref name="holder"/> holds occupied settlements (taken from <paramref name="from"/>, or from anyone if null).</summary>
        public static bool HoldsTakenSettlements(Faction holder, Faction from)
        {
            WarRegistry reg = Registry;
            if (reg.originalOwner.Count == 0) return false;
            foreach (Settlement s in Comp.Cache.SettlementsOf(holder))
            {
                int orig = reg.OriginalOwnerOf(s);
                if (orig >= 0 && orig != holder.loadID && (from == null || orig == from.loadID)) return true;
            }
            return false;
        }

        /// <summary>The defender's settlement closest to the attacker's home: what a conquest goes for first.</summary>
        public static Settlement NearestSettlement(Faction of, PlanetTile from, bool avoidCapital = false)
        {
            List<Settlement> list = Comp.Cache.SettlementsOf(of);
            Settlement best = null;
            float bestDist = float.MaxValue;
            Settlement capital = of.IsPlayer ? null : Comp.GetState(of).capital;
            foreach (Settlement s in list)
            {
                if (s.Destroyed || s.Faction != of) continue;
                if (avoidCapital && s == capital && list.Count > 1) continue;
                if (s.Tile.Layer != Find.WorldGrid.Surface) continue;
                float d = from.Valid && from.Layer == s.Tile.Layer ? Find.WorldGrid.ApproxDistanceInTiles(from, s.Tile) : 0f;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- score

        /// <summary>
        /// Credits a success in the war between the two. A faction fighting for an ally also helps that ally's own war.
        /// </summary>
        public static void AddScore(Faction winner, Faction loser, float amount)
        {
            if (winner == null || loser == null || amount == 0f) return;
            War w = Registry.Find(winner, loser);
            if (w == null) return;
            amount *= WarOrders.StanceFactor(w, winner);
            w.AddScore(winner, amount);
            if (w.alliedTo != null && w.alliedTo != loser)
                Registry.Find(w.alliedTo, loser)?.AddScore(w.alliedTo, amount * 0.5f);
        }

        // ---------------------------------------------------------------- joining and ending wars

        /// <summary>Set while a faction joins someone else's war, so the new war record knows who it is fought for.</summary>
        public static Faction JoiningFor { get; private set; }

        /// <summary><paramref name="joiner"/> enters the war on <paramref name="ally"/>'s side against <paramref name="enemy"/>.</summary>
        public static void JoinWar(Faction joiner, Faction ally, Faction enemy, string reason)
        {
            if (joiner == null || enemy == null || joiner == enemy || joiner.HostileTo(enemy)) return;
            Faction previous = JoiningFor;
            JoiningFor = ally;
            try
            {
                DiplomacyUtility.SetRelationKind(joiner, enemy, FactionRelationKind.Hostile, joiner, reason);
            }
            finally
            {
                JoiningFor = previous;
            }
            if (joiner.HostileTo(enemy))
                EventBus.Raise(DominionEventDefOf.Dominion_JoinedWar, joiner, enemy, detail: ally?.Name);
        }

        /// <summary><paramref name="loser"/> gives up: peace on the winner's terms.</summary>
        public static void Capitulate(War w, Faction loser)
        {
            if (w == null || !w.Involves(loser)) return;
            Faction winner = w.Other(loser);
            if (!loser.HostileTo(winner)) return;
            w.capitulating = loser;
            DiplomacyUtility.SetRelationKind(loser, winner, FactionRelationKind.Neutral, loser, "Dominion_Reason_Capitulation".Translate());
            // If something stopped the peace, the war goes on as before.
            if (loser.HostileTo(winner)) w.capitulating = null;
        }

        // ---------------------------------------------------------------- occupation

        /// <summary>Whether an AI faction can take this settlement by force. Never the player's, never a faction's last one.</summary>
        public static bool CanOccupy(Settlement s, Faction occupier)
        {
            if (!DominionMod.Settings.warOccupation) return false;
            if (s == null || s.Destroyed || s.HasMap || s.Faction == null || s.Faction.IsPlayer || occupier == null || occupier.IsPlayer) return false;
            if (s.Faction == occupier) return false;
            return Comp.Cache.SettlementsOf(s.Faction).Count >= 2;
        }

        /// <summary>The occupier takes the settlement. Remembered so a peace can hand it back.</summary>
        public static void Occupy(Settlement s, Faction occupier)
        {
            Faction former = s.Faction;
            WarRegistry reg = Registry;
            if (!reg.originalOwner.ContainsKey(s.Tile.tileId)) reg.originalOwner[s.Tile.tileId] = former.loadID;
            bool liberated = reg.originalOwner[s.Tile.tileId] == occupier.loadID;
            ChangeOwner(s, occupier);
            if (liberated)
            {
                reg.originalOwner.Remove(s.Tile.tileId);
                EventBus.Raise(DominionEventDefOf.Dominion_SettlementReturned, occupier, former, s.Tile.tileId, detail: s.Label);
            }
            else
                EventBus.Raise(DominionEventDefOf.Dominion_SettlementOccupied, occupier, former, s.Tile.tileId, detail: s.Label);
            // Taking a capital is worth more: the capital-lost event adds its own score the next day.
            AddScore(occupier, former, 15f);
        }

        /// <summary>Hands an occupied settlement back to its original owner.</summary>
        public static void ReturnSettlement(Settlement s, Faction to)
        {
            if (s == null || s.Destroyed || to == null || to.defeated || s.Faction == to || s.HasMap) return;
            Faction from = s.Faction;
            ChangeOwner(s, to);
            Registry.originalOwner.Remove(s.Tile.tileId);
            EventBus.Raise(DominionEventDefOf.Dominion_SettlementReturned, to, from, s.Tile.tileId, detail: s.Label);
        }

        /// <summary>A settlement changes hands for good (peace terms). Not an occupation any more.</summary>
        public static void Cede(Settlement s, Faction to)
        {
            if (s == null || s.Destroyed || to == null || s.Faction == to || s.HasMap) return;
            ChangeOwner(s, to);
            Registry.originalOwner.Remove(s.Tile.tileId);
        }

        /// <summary>
        /// The economic side of a settlement changing hands (E&amp;D): most of the people living there stay and now belong to
        /// the new owner, and part of the town's share of the old owner's stores is captured.
        /// </summary>
        public static void SettlementChangedHands(Faction from, Faction to)
        {
            if (from == null || to == null || from.IsPlayer || !Comp.TryGetState(from, out FactionState fs)) return;
            int towns = Mathf.Max(1, fs.settlementCount);
            IntegrationRegistry.Get<Integration_EconomicsDemography>()?.MovePopulation(from, to, Mathf.RoundToInt(Mathf.Max(fs.totalLiving, fs.population) / (float)towns * 0.6f));
            int stores = Mathf.RoundToInt(Mathf.Clamp(fs.wealth / towns * 0.3f, 0f, 5000f));
            if (stores > 0 && !to.IsPlayer) EconomyEffects.Transfer(from, to, stores);
            EconomyEffects.Shock(from, -0.03f, destroyGoods: false);
        }

        private static AccessTools.FieldRef<Settlement, Material> cachedMat;
        private static bool cachedMatResolved;

        private static void ChangeOwner(Settlement s, Faction to)
        {
            Faction from = s.Faction;
            s.SetFaction(to);
            OwnershipWatch.Note(s);
            SettlementChangedHands(from, to);
            // The old owner's goods and people are not the new owner's.
            try { s.trader?.TryDestroyStock(); }
            catch (System.Exception e) { DLog.WarningOnce("Clearing a captured settlement's stock failed: " + e.Message, 0x7A01); }
            s.previouslyGeneratedInhabitants?.Clear();
            if (!cachedMatResolved)
            {
                cachedMatResolved = true;
                try { cachedMat = AccessTools.FieldRefAccess<Settlement, Material>("cachedMat"); }
                catch (System.Exception e) { DLog.WarningOnce("Settlement icon cache not found: " + e.Message, 0x7A02); }
            }
            if (cachedMat != null) cachedMat(s) = null; // redraw with the new owner's colours
            Comp.Cache.Rebuild();
            IntegrationRegistry.Get<Integration_FactionTerritories>()?.RefreshTerritory();
        }

        // ---------------------------------------------------------------- payments

        public static float PlayerHomeWealth()
        {
            float w = 0f;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome) w += map.wealthWatcher.WealthTotal;
            return w;
        }

        /// <summary>A one-time war payment, moved as real silver (E&amp;D stockpiles, drop pods, the colony). A line for the terms, or null.</summary>
        public static string Pay(Faction payer, Faction receiver, float scale = 1f)
        {
            int value = payer.IsPlayer
                ? Mathf.RoundToInt(Mathf.Clamp(PlayerHomeWealth() * 0.02f, 200f, 2000f) * scale)
                : Mathf.RoundToInt(Mathf.Clamp(Comp.GetState(payer).wealth * 0.02f, 300f, 3000f) * scale);
            int moved = EconomyEffects.Transfer(payer, receiver, value);
            if (!payer.IsPlayer) EconomyEffects.Shock(payer, -0.04f, destroyGoods: false);
            if (!receiver.IsPlayer) EconomyEffects.Shock(receiver, 0.03f);
            if (moved <= 0) return null;
            if (payer.IsPlayer) return "Dominion_Terms_PlayerPaid".Translate(moved, receiver.Name).ToString();
            if (receiver.IsPlayer) return "Dominion_Terms_PaidToPlayer".Translate(payer.Name, moved).ToString();
            return "Dominion_Terms_Paid".Translate(payer.Name, receiver.Name).ToString();
        }
    }
}
