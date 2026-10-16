using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Required. Faction Territories and Vassalage owns territory and vassal outposts. Tile ownership is
    /// read through a compiled delegate, because the territory scan calls it for every world tile once a day.
    /// </summary>
    public partial class Integration_FactionTerritories : DominionIntegration
    {
        public override string PackageId => "jaeger972.factionterritories";
        public override string Label => "Faction Territories and Vassalage";
        public override bool Required => true;

        private delegate bool OwnerByInt(int tile, out int factionId);
        private delegate bool OwnerByPlanetTile(PlanetTile tile, out int factionId);

        // FTV for 1.6 takes an int tile id; a 1.7 build may take PlanetTile. Either one is bound.
        private OwnerByInt ownerByInt;
        private OwnerByPlanetTile ownerByPlanetTile;

        private FieldInfo vassalComponentInstance;
        private MethodInfo getAllOutposts;
        private PropertyInfo outpostMasterId;
        private FieldInfo outpostOriginalId;
        private MethodInfo canPurchase;
        private MethodInfo tryPurchase;
        private MethodInfo goodwillCost;
        private MethodInfo executeVassalisation;

        // Optional: territory refresh and FTV invasions (its own base sieges).
        private MethodInfo requestRegenerate;
        private Type invasionType;
        private MethodInfo tryCreateInvasion;
        private FieldInfo invasionAttacker;
        private FieldInfo invasionDefender;
        private MethodInfo rollWinner;
        private bool rollPatched;

        protected internal override void Bind(Binder b)
        {
            Type cache = b.Type("FactionTerritories.TerritoryOwnershipCache");
            MethodInfo single = b.Method(cache, "TryGetSingleFactionIfUncontested");
            if (single != null)
            {
                Type tileType = single.GetParameters()[0].ParameterType;
                if (tileType == typeof(int))
                    ownerByInt = b.Delegate<OwnerByInt>(single);
                else if (tileType == typeof(PlanetTile))
                    ownerByPlanetTile = b.Delegate<OwnerByPlanetTile>(single);
                else
                    b.Fail("territory lookup takes unexpected tile type " + tileType.Name);
            }

            Type component = b.Type("FactionTerritories.Vassalise.VassaliseComponent");
            vassalComponentInstance = b.Field(component, "Instance");
            getAllOutposts = b.Method(component, "GetAllOutposts", Type.EmptyTypes);
            Type outpost = b.Type("FactionTerritories.Vassalise.FactionTerritories_VassalOutpost");
            outpostMasterId = b.Property(outpost, "MasterFactionLoadIDString");
            outpostOriginalId = b.Field(outpost, "originalFactionId");

            // Used by later milestones (wars ceding settlements, vassalisation).
            Type vassalise = b.Type("FactionTerritories.Vassalise.VassaliseUtility");
            canPurchase = b.Method(vassalise, "CanPurchaseVassaliseSettlement");
            tryPurchase = b.Method(vassalise, "TryPurchaseVassalisation");
            goodwillCost = b.Method(vassalise, "GetSettlementVassaliseGoodwillCost");
            b.Method(vassalise, "CanVassaliseSettlement");
            executeVassalisation = b.Method(vassalise, "ExecuteVassalisation");
            b.Method(vassalise, "ExecuteCedeToFactionAtTile");

            // Optional members: missing ones only switch off the feature that needs them.
            Type util = AccessTools.TypeByName("FactionTerritories.FactionTerritoriesUtility");
            requestRegenerate = util?.GetMethod("RequestRegenerate", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            Type inv = AccessTools.TypeByName("FactionTerritories.Invasions.Utility");
            invasionType = AccessTools.TypeByName("FactionTerritories.Invasions.Invasion");
            tryCreateInvasion = inv?.GetMethod("TryCreateForSettlement", BindingFlags.Public | BindingFlags.Static);
            rollWinner = inv?.GetMethod("RollWinner", BindingFlags.NonPublic | BindingFlags.Static);
            invasionAttacker = invasionType?.GetField("attackerFactionLoadId");
            invasionDefender = invasionType?.GetField("defenderFactionLoadId");
            b.Note(requestRegenerate != null ? "territory refresh: RequestRegenerate" : "territory refresh not found");
            b.Note(tryCreateInvasion != null && invasionAttacker != null ? "invasions: warbands besiege through FTV" : "invasions not found: warband sieges stay abstract");
            b.Note(rollWinner != null ? "invasion outcomes use Dominion's military strength" : "invasion outcome hook not found");
            BindExpansion(b);
        }

        protected internal override void OnActivated()
        {
            PatchExpansion();
            if (rollWinner == null || rollPatched) return;
            try
            {
                DominionMod.Harmony.Patch(rollWinner, prefix: new HarmonyMethod(typeof(Integration_FactionTerritories), nameof(RollWinnerPrefix)));
                rollPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook FTV invasion outcomes: " + e.Message);
            }
        }

        /// <summary>
        /// FTV decides an invasion it resolves off-map from tech level alone; between factions Dominion simulates, the
        /// odds come from the attacking force (a Dominion warband's, if one started it) against the garrison.
        /// </summary>
        public static bool RollWinnerPrefix(RimWorld.Faction attacker, RimWorld.Faction defender, float attackerWeight, float defenderWeight, int tile, ref RimWorld.Faction __result)
        {
            try
            {
                if (WorldComponent_Dominion.Instance == null || attacker == null || defender == null) return true;
                if (!DominionScheduler.IsSimulated(attacker) || !DominionScheduler.IsSimulated(defender)) return true;
                Settlement s = Find.WorldObjects.SettlementAt(new PlanetTile(tile));
                // A battle fought out on a real map is decided by who is left standing: leave that to FTV.
                if (s != null && s.HasMap) return true;
                float points = WarbandUtility.SiegePointsAt(tile, attacker);
                float chance = WarbandUtility.SiegeChance(points, attacker, defender, s);
                // Blend with FTV's own weighting so its numbers still matter.
                if (attackerWeight + defenderWeight > 0f)
                    chance = 0.6f * chance + 0.4f * attackerWeight / (attackerWeight + defenderWeight);
                __result = Rand.Chance(chance) ? attacker : defender;
                return false;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("FTV invasion outcome hook failed: " + e.Message, 0x0F75);
                return true;
            }
        }

        /// <summary>Asks FTV to redraw territory after Dominion moved a settlement to another owner.</summary>
        public void RefreshTerritory()
        {
            if (!IsActive || requestRegenerate == null) return;
            try { requestRegenerate.Invoke(null, new object[] { true }); }
            catch (Exception e) { DLog.WarningOnce("FTV territory refresh failed: " + e.Message, 0x0F76); }
        }

        public bool CanInvade => IsActive && tryCreateInvasion != null;

        /// <summary>Starts an FTV base invasion: the siege shows on the map, the player can join it, and FTV resolves it.</summary>
        public bool TryStartInvasion(Settlement s, RimWorld.Faction attacker)
        {
            if (!CanInvade || s == null || attacker == null) return false;
            try
            {
                return (bool)tryCreateInvasion.Invoke(null, new object[] { s, attacker, Find.TickManager.TicksGame });
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Starting an FTV invasion failed: " + e.Message, 0x0F77);
                return false;
            }
        }

        /// <summary>FTV invasions on the map right now: (tile, attacker load id, defender load id).</summary>
        public IEnumerable<(int tile, int attacker, int defender, int id)> Invasions()
        {
            if (!IsActive || invasionType == null || invasionAttacker == null) yield break;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (!invasionType.IsInstanceOfType(o) || o.Destroyed) continue;
                yield return (o.Tile.tileId, (int)invasionAttacker.GetValue(o), (int)invasionDefender.GetValue(o), o.ID);
            }
        }

        public bool CanReadTerritory => ownerByInt != null || ownerByPlanetTile != null;

        /// <summary>The faction load id that solely claims a surface tile, if any.</summary>
        public bool TryGetOwner(int tileId, out int factionId)
        {
            factionId = -1;
            if (ownerByInt != null) return ownerByInt(tileId, out factionId);
            if (ownerByPlanetTile != null) return ownerByPlanetTile(new PlanetTile(tileId), out factionId);
            return false;
        }

        /// <summary>
        /// Calls <paramref name="visit"/> with (master faction load id, original faction load id) for every
        /// vassal outpost. Load ids are -1 when unknown.
        /// </summary>
        public void ForEachVassalOutpost(Action<int, int> visit)
        {
            if (!IsActive) return;
            object component = vassalComponentInstance?.GetValue(null);
            if (component == null) return;
            try
            {
                if (!(getAllOutposts.Invoke(component, null) is IEnumerable outposts)) return;
                foreach (object o in outposts)
                {
                    if (o == null) continue;
                    // Outposts belong to their master (the player, in FTV); MasterFactionLoadIDString is the original owner.
                    int master = (o as WorldObject)?.Faction?.loadID ?? ParseLoadId(outpostMasterId.GetValue(o) as string);
                    int original = (int)outpostOriginalId.GetValue(o);
                    visit(master, original);
                }
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Reading FTV vassal outposts failed: " + e, 0x0F71);
            }
        }

        /// <summary>Whether FTV lets the player negotiate this settlement into a vassal outpost.</summary>
        public bool CanNegotiateVassalage(Settlement s, out string reason)
        {
            reason = null;
            if (!IsActive || s == null) return false;
            try
            {
                object[] args = { s, null };
                bool ok = (bool)canPurchase.Invoke(null, args);
                reason = args[1] as string;
                return ok;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Checking FTV vassalisation failed: " + e, 0x0F72);
                return false;
            }
        }

        /// <summary>Goodwill FTV charges for a negotiated vassalisation, or -1 if unknown.</summary>
        public int VassalageGoodwillCost
        {
            get
            {
                try { return IsActive ? (int)goodwillCost.Invoke(null, null) : -1; }
                catch { return -1; }
            }
        }

        /// <summary>Has FTV turn the settlement into a player vassal outpost, paying FTV's own costs.</summary>
        public bool TryNegotiateVassalage(Settlement s)
        {
            if (!IsActive || s == null) return false;
            try
            {
                RimWorld.Faction before = s.Faction;
                bool ok = (bool)tryPurchase.Invoke(null, new object[] { s });
                if (ok) WarUtility.SettlementChangedHands(before, RimWorld.Faction.OfPlayer);
                return ok;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("FTV vassalisation failed: " + e, 0x0F73);
                return false;
            }
        }

        /// <summary>
        /// Peace terms: the settlement becomes the player's vassal outpost without FTV's purchase cost. Arguments are
        /// matched by type, so small signature changes in FTV still work. True if the settlement changed hands.
        /// </summary>
        public bool TryCedeToPlayer(Settlement s)
        {
            if (!IsActive || s == null || s.Destroyed || executeVassalisation == null) return false;
            RimWorld.Faction before = s.Faction;
            try
            {
                ParameterInfo[] ps = executeVassalisation.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    Type t = ps[i].ParameterType;
                    if (t.IsAssignableFrom(typeof(Settlement))) args[i] = s;
                    else if (t == typeof(RimWorld.Faction)) args[i] = RimWorld.Faction.OfPlayer;
                    else if (t == typeof(PlanetTile)) args[i] = s.Tile;
                    else if (t == typeof(int)) args[i] = s.Tile.tileId;
                    else if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else if (t == typeof(bool)) args[i] = false;
                    else if (t.IsValueType) args[i] = Activator.CreateInstance(t);
                }
                object result = executeVassalisation.Invoke(null, args);
                // FTV removes the settlement from the world (not Destroy) and puts an outpost in its place.
                bool changed = result is bool ok ? ok : s.Destroyed || !Find.WorldObjects.Contains(s) || s.Faction != before;
                if (!changed) return false;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("FTV cession failed: " + e, 0x0F74);
                return false;
            }
            // The town's people and goods leave the old owner's books (E&D never sees FTV's Remove).
            WarUtility.SettlementChangedHands(before, RimWorld.Faction.OfPlayer);
            return true;
        }

        /// <summary>Land and vassals count in the Rim Council: up to +2 for territory, +0.2 per vassal outpost (max +2).</summary>
        public override float VoteWeightModifier(RimWorld.Faction f)
        {
            int tiles, vassals;
            if (f.IsPlayer)
            {
                tiles = TerritorySystem.PlayerTerritoryTiles;
                vassals = TerritorySystem.PlayerVassalOutposts;
            }
            else
            {
                FactionState s = WorldComponent_Dominion.Instance.GetState(f);
                tiles = s.territoryTiles;
                vassals = s.vassalOutposts;
            }
            return UnityEngine.Mathf.Min(2f, tiles / 250f) + UnityEngine.Mathf.Min(2f, vassals * 0.2f);
        }

        /// <summary>FTV stores factions as unique load ids like "Faction_12".</summary>
        private static int ParseLoadId(string s)
        {
            if (s.NullOrEmpty()) return -1;
            int i = s.LastIndexOf('_');
            return int.TryParse(i >= 0 ? s.Substring(i + 1) : s, out int id) ? id : -1;
        }
    }
}
