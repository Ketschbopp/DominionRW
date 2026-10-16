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

        /// <summary>FTV stores factions as unique load ids like "Faction_12".</summary>
        private static int ParseLoadId(string s)
        {
            if (s.NullOrEmpty()) return -1;
            int i = s.LastIndexOf('_');
            return int.TryParse(i >= 0 ? s.Substring(i + 1) : s, out int id) ? id : -1;
        }
    }
}
