using System;
using System.Collections;
using System.Reflection;
using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Required. Faction Territories and Vassalage owns territory and vassal outposts. Tile ownership is
    /// read through a compiled delegate, because the territory scan calls it for every world tile once a day.
    /// </summary>
    public class Integration_FactionTerritories : DominionIntegration
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
            b.Method(vassalise, "ExecuteVassalisation");
            b.Method(vassalise, "ExecuteCedeToFactionAtTile");
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
                    int master = ParseLoadId(outpostMasterId.GetValue(o) as string);
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
                return (bool)tryPurchase.Invoke(null, new object[] { s });
            }
            catch (Exception e)
            {
                DLog.WarningOnce("FTV vassalisation failed: " + e, 0x0F73);
                return false;
            }
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
