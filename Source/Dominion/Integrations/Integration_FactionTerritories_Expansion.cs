using System;
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
    /// Faction Territories expansion and invasions as Dominion sees them:
    /// <list type="bullet">
    /// <item>FTV factions grow by building a <c>SettlementConstruction</c> site that becomes a settlement when it
    /// completes. Dominion reports the site once when it appears, and the new town once it stands.</item>
    /// <item>A player caravan attacking a construction site is a grievance for the builder and counts in a war.</item>
    /// <item>An FTV siege between two AI factions that are hostile but have no Dominion war opens one, so the siege,
    /// and any town it takes, counts towards a war that can be won, lost and ended on terms.</item>
    /// </list>
    /// All of it is optional: a missing member switches off only the feature that needs it.
    /// </summary>
    public partial class Integration_FactionTerritories
    {
        private Type constructionType;
        private FieldInfo constructionOwner;
        private FieldInfo constructionCompleteTick;
        private MethodInfo attackConstructionArrived;
        private FieldInfo attackConstructionTarget;
        private bool attackPatched;

        // Construction sites known to Dominion: world object id -> (tile, owner load id). Not saved: the first scan
        // after a load records what is already there without reporting it.
        private readonly Dictionary<int, (int tile, int owner)> sites = new Dictionary<int, (int, int)>();
        private readonly HashSet<int> raidedSites = new HashSet<int>();
        private bool sitesPrimed;

        private void BindExpansion(Binder b)
        {
            constructionType = AccessTools.TypeByName("FactionTerritories.Expansion.SettlementConstruction");
            constructionOwner = constructionType?.GetField("ownerFactionLoadId", BindingFlags.Public | BindingFlags.Instance);
            constructionCompleteTick = constructionType?.GetField("completeTick", BindingFlags.Public | BindingFlags.Instance);
            if (constructionOwner != null && constructionOwner.FieldType != typeof(int)) constructionOwner = null;
            if (constructionCompleteTick != null && constructionCompleteTick.FieldType != typeof(int)) constructionCompleteTick = null;

            Type attack = AccessTools.TypeByName("FactionTerritories.Expansion.ArrivalAction_AttackConstruction");
            attackConstructionArrived = attack?.GetMethod("Arrived", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Caravan) }, null);
            attackConstructionTarget = attack?.GetField("construction", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

            b.Note(constructionOwner != null ? "expansion: construction sites are reported as news" : "expansion sites not found: FTV expansion stays silent");
            b.Note(attackConstructionArrived != null && attackConstructionTarget != null
                ? "attacks on construction sites cause grievance and count in wars" : "construction attack hook not found");
        }

        private void PatchExpansion()
        {
            if (attackPatched || attackConstructionArrived == null || attackConstructionTarget == null) return;
            try
            {
                DominionMod.Harmony.Patch(attackConstructionArrived,
                    postfix: new HarmonyMethod(typeof(Integration_FactionTerritories), nameof(AttackConstructionPostfix)));
                attackPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook FTV construction attacks: " + e.Message);
            }
        }

        protected internal override void OnWorldLoaded()
        {
            sites.Clear();
            raidedSites.Clear();
            sitesPrimed = false;
            try { ScanConstructionSites(); }
            catch (Exception e) { DLog.WarningOnce("Reading FTV construction sites failed: " + e.Message, 0x0F78); }
        }

        public override void DailyGlobal()
        {
            try { ScanConstructionSites(); }
            catch (Exception e) { DLog.WarningOnce("Reading FTV construction sites failed: " + e.Message, 0x0F78); }
        }

        /// <summary>Construction sites on the map right now: (world object id, tile, owner load id).</summary>
        public IEnumerable<(int id, int tile, int owner)> ConstructionSites()
        {
            if (!IsActive || constructionType == null || constructionOwner == null || Find.WorldObjects == null) yield break;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject o = all[i];
                if (o == null || o.Destroyed || !constructionType.IsInstanceOfType(o)) continue;
                yield return (o.ID, o.Tile.tileId, (int)constructionOwner.GetValue(o));
            }
        }

        private void ScanConstructionSites()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || constructionType == null || constructionOwner == null) return;
            var now = new HashSet<int>();
            foreach ((int id, int tile, int owner) in ConstructionSites())
            {
                now.Add(id);
                if (sites.ContainsKey(id)) continue;
                sites[id] = (tile, owner);
                if (!sitesPrimed) continue;
                Faction f = FactionByLoadId(owner);
                if (!DominionScheduler.IsSimulated(f)) continue;
                Settlement near = WarUtility.NearestSettlement(f, new PlanetTile(tile));
                EventBus.Raise(IntegrationEventDefOf.Dominion_SettlementConstructionBegun, f, null, tile,
                    detail: near?.Label ?? "Dominion_Integration_TheFrontier".Translate().ToString(), source: PackageId);
            }
            sitesPrimed = true;
            if (sites.Count == now.Count) return;

            var gone = new List<int>();
            foreach (int id in sites.Keys)
                if (!now.Contains(id)) gone.Add(id);
            bool founded = false;
            foreach (int id in gone)
            {
                (int tile, int owner) = sites[id];
                sites.Remove(id);
                raidedSites.Remove(id);
                // FTV removes a finished site and puts its owner's new settlement on the same tile; anything else
                // (defeated, abandoned) leaves no town behind.
                Settlement s = Find.WorldObjects.SettlementAt(new PlanetTile(tile));
                if (s == null || s.Faction == null || s.Faction.loadID != owner) continue;
                Faction f = s.Faction;
                if (!DominionScheduler.IsSimulated(f)) continue;
                founded = true;
                FactionState fs = comp.GetState(f);
                fs.stability = Mathf.Min(100f, fs.stability + 2f);
                fs.legitimacy = Mathf.Min(100f, fs.legitimacy + 1f);
                EventBus.Raise(IntegrationEventDefOf.Dominion_SettlementFounded, f, null, tile, detail: s.Label, source: PackageId);
            }
            if (founded) comp.Cache.Rebuild();
        }

        /// <summary>Harmony postfix on FTV's ArrivalAction_AttackConstruction.Arrived: a caravan has attacked a construction site.</summary>
        public static void AttackConstructionPostfix(CaravanArrivalAction __instance, Caravan caravan)
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
                Integration_FactionTerritories ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
                if (ftv == null || !ftv.IsActive) return;
                ftv.Notify_ConstructionAttacked(__instance, caravan);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("FTV construction attack hook failed: " + e.Message, 0x0F79);
            }
        }

        private void Notify_ConstructionAttacked(CaravanArrivalAction action, Caravan caravan)
        {
            if (action == null || caravan == null || attackConstructionTarget == null || constructionOwner == null) return;
            if (!(attackConstructionTarget.GetValue(action) is WorldObject site) || site.Destroyed) return;
            // One report per site: re-entering the same site is the same raid.
            if (!raidedSites.Add(site.ID)) return;
            Faction victim = FactionByLoadId((int)constructionOwner.GetValue(site));
            Faction attacker = caravan.Faction;
            if (victim == null || attacker == null || victim == attacker || !DominionScheduler.IsSimulated(victim)) return;
            if (!attacker.IsPlayer && !DominionScheduler.IsSimulated(attacker)) return;

            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState view = comp.GetRelation(victim, attacker);
            view.grievance = Mathf.Min(100f, view.grievance + 20f);
            view.favour -= 10f;
            // Attacking makes them hostile (FTV applies the vanilla attack penalty), so the war exists by now.
            WarUtility.AddScore(attacker, victim, 6f);
            EconomyEffects.Shock(victim, -0.01f, destroyGoods: false, days: 10);
            Settlement near = WarUtility.NearestSettlement(victim, site.Tile);
            EventBus.Raise(IntegrationEventDefOf.Dominion_ConstructionRaided, attacker, victim, site.Tile.tileId,
                detail: near?.Label ?? "Dominion_Integration_TheFrontier".Translate().ToString(), source: PackageId);
        }

        /// <summary>
        /// An FTV siege between two AI factions that are hostile but have no Dominion war (hostile from the start, or
        /// set hostile while Dominion was not watching) makes the war official, so the siege counts towards it.
        /// Called by <see cref="OwnershipWatch"/> when it first sees a siege. True if a war was opened.
        /// </summary>
        public bool EnsureWarForSiege(Faction attacker, Faction defender)
        {
            try
            {
                if (!IsActive || attacker == null || defender == null || attacker == defender) return false;
                if (attacker.IsPlayer || defender.IsPlayer || !DominionMod.Settings.aiDiplomacyEnabled) return false;
                if (!DominionScheduler.IsSimulated(attacker) || !DominionScheduler.IsSimulated(defender)) return false;
                if (!WarUtility.CanWage(attacker) || !WarUtility.CanWage(defender)) return false;
                // FTV only invades enemies; if they are not hostile, something else is going on.
                if (!attacker.HostileTo(defender) || WarUtility.Registry.Find(attacker, defender) != null) return false;
                EventBus.Raise(DominionEventDefOf.Dominion_WarDeclared, attacker, defender, source: PackageId);
                return WarUtility.Registry.Find(attacker, defender) != null;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Opening a war for an FTV siege failed: " + e.Message, 0x0F7A);
                return false;
            }
        }

        internal static Faction FactionByLoadId(int loadId)
        {
            if (loadId < 0) return null;
            Faction f = WorldComponent_Dominion.Instance?.Cache.FactionById(loadId);
            if (f != null) return f;
            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i].loadID == loadId) return all[i];
            return null;
        }
    }
}
