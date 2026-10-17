using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Vanilla Outposts Expanded. The player's own outposts add council weight and watch the factions near them
    /// (intel). AI factions at war build VOE's military outposts near the front:
    /// <list type="bullet">
    /// <item>An <b>encampment</b> is a forward base: warbands march from it, and set out a little stronger.</item>
    /// <item>A <b>defensive outpost</b> stiffens the garrisons of the faction's towns in its range.</item>
    /// <item>An <b>artillery outpost</b> softens up enemy towns in its range before a siege, and may shell the player's
    /// colony if it is in range and the two are at war.</item>
    /// </list>
    /// A player caravan can attack one (an act of war if the two are not already at war). Outposts are packed up when
    /// their faction's wars end. VOE's outposts are written for the player only, so for AI-owned ones Dominion hides the
    /// player's commands (pack, remove pawns, fire, give and take items) and skips VOE's own upkeep, production and
    /// "abandoned" letter.
    /// </summary>
    public class Integration_VanillaOutposts : DominionIntegration
    {
        public override string PackageId => "vanillaexpanded.outposts";
        public override string Label => "Vanilla Outposts Expanded";

        private static Integration_VanillaOutposts instance;

        private Type outpostType;
        private MethodInfo addPawn;
        private FieldInfo nameField;
        private PropertyInfo pawnCount;
        private PropertyInfo range;
        private Type artilleryType;
        private MethodInfo fire;
        private FieldInfo artilleryCooldown;
        private PropertyInfo capablePawns;

        private WorldObjectDef encampmentDef, defensiveDef, artilleryDef;

        public enum Kind { Encampment, Defensive, Artillery }

        /// <summary>The player's outposts watch factions with a town this close.</summary>
        private const float WatchTiles = 15f;
        private readonly List<PlanetTile> playerOutposts = new List<PlanetTile>();

        protected internal override void OnWorldLoaded() => playerOutposts.Clear();

        public override float VoteWeightModifier(Faction f) => f.IsPlayer ? Mathf.Min(3f, playerOutposts.Count * 0.3f) : 0f;

        public override float IntelModifier(Faction f)
        {
            List<Settlement> settlements = WorldComponent_Dominion.Instance.Cache.SettlementsOf(f);
            foreach (PlanetTile t in playerOutposts)
                foreach (Settlement s in settlements)
                    if (DominionGeo.Distance(t, s.Tile) is float d && d >= 0f && d < WatchTiles) return 8f;
            return 0f;
        }

        /// <summary>Outposts per faction at once: one, plus one per four towns, up to three.</summary>
        private static int MaxFor(Faction f) => Mathf.Clamp(1 + WorldComponent_Dominion.Instance.Cache.SettlementsOf(f).Count / 4, 1, 3);

        private const float EncampmentBonus = 1.1f;
        private const float DefensiveGarrison = 1.25f;
        private const float ArtilleryGarrison = 0.85f;
        private const int FallbackRange = 15;

        protected internal override void Bind(Binder b)
        {
            instance = this;
            outpostType = b.Type("Outposts.Outpost");
            addPawn = b.Method(outpostType, "AddPawn", new[] { typeof(Pawn) });
            nameField = b.Field(outpostType, "Name");
            pawnCount = b.Property(outpostType, "PawnCount");
            range = outpostType?.GetProperty("Range", BindingFlags.Public | BindingFlags.Instance);
            capablePawns = outpostType?.GetProperty("CapablePawns", BindingFlags.Public | BindingFlags.Instance);
            artilleryType = AccessTools.TypeByName("VOE.Outpost_Artillery");
            fire = artilleryType?.GetMethod("Fire", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(GlobalTargetInfo) }, null);
            artilleryCooldown = artilleryType?.GetField("cooldownTicksLeft", BindingFlags.NonPublic | BindingFlags.Instance);
            b.Note(fire != null && artilleryCooldown != null && capablePawns != null
                ? "AI artillery outposts can shell the player's colony" : "artillery fire not found: AI artillery only helps sieges");
        }

        protected internal override void OnActivated()
        {
            encampmentDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("Outpost_Encampment");
            defensiveDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("Outpost_Defensive");
            artilleryDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("Outpost_Artillery");
            try
            {
                Harmony h = DominionMod.Harmony;
                var skip = new HarmonyMethod(typeof(Integration_VanillaOutposts), nameof(SkipForAiPrefix));
                foreach (string name in new[] { "Tick", "TickInterval" })
                {
                    MethodInfo m = AccessTools.DeclaredMethod(outpostType, name);
                    if (m != null) h.Patch(m, prefix: skip);
                }

                // Every outpost class adds its own commands on top of the base ones; hide them all for AI outposts.
                var hide = new HarmonyMethod(typeof(Integration_VanillaOutposts), nameof(HideGizmosPostfix));
                foreach (Type t in GenTypes.AllTypes.Where(t => outpostType.IsAssignableFrom(t)))
                {
                    MethodInfo g = AccessTools.DeclaredMethod(t, "GetGizmos");
                    if (g != null) h.Patch(g, postfix: hide);
                    MethodInfo cg = AccessTools.DeclaredMethod(t, "GetCaravanGizmos");
                    if (cg != null) h.Patch(cg, postfix: hide);
                }
                MethodInfo menu = AccessTools.Method(outpostType, "GetFloatMenuOptions", new[] { typeof(Caravan) });
                if (menu != null) h.Patch(menu, postfix: new HarmonyMethod(typeof(Integration_VanillaOutposts), nameof(AttackOptionPostfix)));
            }
            catch (Exception e)
            {
                DLog.Warning("Could not adapt Vanilla Outposts Expanded for AI outposts: " + e.Message);
            }
        }

        public static bool IsAiOutpost(WorldObject o) =>
            o != null && instance?.outpostType != null && instance.outpostType.IsInstanceOfType(o) && o.Faction != null && !o.Faction.IsPlayer;

        /// <summary>Harmony prefix on Outpost.Tick / TickInterval: AI outposts need none of VOE's upkeep.</summary>
        public static bool SkipForAiPrefix(WorldObject __instance) => !IsAiOutpost(__instance);

        /// <summary>Harmony postfix on outpost gizmos: none of the player's commands on someone else's outpost.</summary>
        public static void HideGizmosPostfix(WorldObject __instance, ref IEnumerable<Gizmo> __result)
        {
            if (IsAiOutpost(__instance)) __result = Enumerable.Empty<Gizmo>();
        }

        /// <summary>Harmony postfix on Outpost.GetFloatMenuOptions: a caravan may attack an AI outpost.</summary>
        public static void AttackOptionPostfix(WorldObject __instance, Caravan caravan, ref IEnumerable<FloatMenuOption> __result)
        {
            if (!IsAiOutpost(__instance) || caravan == null || !caravan.IsPlayerControlled) return;
            bool actOfWar = !__instance.Faction.HostileTo(Faction.OfPlayer);
            string label = "Dominion_Outpost_Attack".Translate(__instance.Label).ToString()
                           + (actOfWar ? " " + "Dominion_Intercept_ActOfWarTag".Translate().ToString() : "");
            FloatMenuOption opt = InterceptUtility.Option(caravan, __instance, label, actOfWar);
            if (opt != null) __result = (__result ?? Enumerable.Empty<FloatMenuOption>()).Append(opt);
        }

        /// <summary>An AI outpost as something a caravan can attack (see <see cref="InterceptUtility"/>).</summary>
        public static IInterceptable AsTarget(WorldObject o) => IsAiOutpost(o) ? new OutpostTarget(o) : null;

        private class OutpostTarget : IInterceptable
        {
            private readonly WorldObject outpost;
            public OutpostTarget(WorldObject o) => outpost = o;
            public PlanetTile TileAhead(int steps) => outpost.Tile;
            public bool CanBeIntercepted => !outpost.Destroyed && outpost.Faction != null && !outpost.Faction.defeated;
            public void Engage(Caravan caravan) => instance?.Attack(outpost, caravan);
        }

        // ---------------------------------------------------------------- reading outposts

        public IEnumerable<WorldObject> OutpostsOf(Faction f)
        {
            if (!IsActive || outpostType == null) yield break;
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].Destroyed && all[i].Faction == f && outpostType.IsInstanceOfType(all[i])) yield return all[i];
        }

        private Kind? KindOf(WorldObject o) =>
            o.def == encampmentDef ? Kind.Encampment : o.def == defensiveDef ? Kind.Defensive : o.def == artilleryDef ? Kind.Artillery : (Kind?)null;

        private int RangeOf(WorldObject o)
        {
            try { return range != null && range.GetValue(o) is int r && r > 0 ? r : FallbackRange; }
            catch { return FallbackRange; }
        }

        private bool InRange(WorldObject o, PlanetTile tile) =>
            DominionGeo.Distance(o.Tile, tile) is float d && d >= 0f && d <= RangeOf(o);

        /// <summary>
        /// The garrison multiplier at a town under siege: its owner's defensive outposts in range stiffen it, the
        /// attacker's artillery in range softens it.
        /// </summary>
        public static float GarrisonFactor(Faction attacker, Faction defender, Settlement s)
        {
            Integration_VanillaOutposts v = instance;
            if (v == null || !v.IsActive || s == null) return 1f;
            float factor = 1f;
            if (v.OutpostsOf(defender).Any(o => v.KindOf(o) == Kind.Defensive && v.InRange(o, s.Tile))) factor *= DefensiveGarrison;
            if (v.OutpostsOf(attacker).Any(o => v.KindOf(o) == Kind.Artillery && v.InRange(o, s.Tile))) factor *= ArtilleryGarrison;
            return factor;
        }

        /// <summary>A forward encampment of <paramref name="f"/> nearer to <paramref name="to"/> than <paramref name="from"/>, or Invalid.</summary>
        public static PlanetTile ForwardBase(Faction f, PlanetTile from, PlanetTile to)
        {
            Integration_VanillaOutposts v = instance;
            if (v == null || !v.IsActive || !to.Valid) return PlanetTile.Invalid;
            float best = from.Valid ? DominionGeo.Distance(from, to) : float.MaxValue;
            if (best < 0f) best = float.MaxValue;
            PlanetTile result = PlanetTile.Invalid;
            foreach (WorldObject o in v.OutpostsOf(f))
            {
                if (v.KindOf(o) != Kind.Encampment || o.Tile == to) continue;
                float d = DominionGeo.Distance(o.Tile, to);
                if (d >= 0f && d < best)
                {
                    best = d;
                    result = o.Tile;
                }
            }
            return result;
        }

        /// <summary>Warbands that set out from a forward encampment are rested and supplied.</summary>
        public static float LaunchBonus(Faction f, PlanetTile start)
        {
            Integration_VanillaOutposts v = instance;
            if (v == null || !v.IsActive) return 1f;
            return v.OutpostsOf(f).Any(o => o.Tile == start && v.KindOf(o) == Kind.Encampment) ? EncampmentBonus : 1f;
        }

        // ---------------------------------------------------------------- the daily round

        public override void DailyGlobal()
        {
            try
            {
                if (!IsActive || outpostType == null) return;
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                DominionSettings set = DominionMod.Settings;

                playerOutposts.Clear();
                foreach (WorldObject o in Find.WorldObjects.AllWorldObjects)
                    if (o.Faction != null && o.Faction.IsPlayer && outpostType.IsInstanceOfType(o))
                        playerOutposts.Add(o.Tile);

                // Outposts are packed up when their faction has no war left (or the feature was switched off).
                foreach (WorldObject o in Find.WorldObjects.AllWorldObjects.Where(IsAiOutpost).ToList())
                {
                    Faction f = o.Faction;
                    if (!set.aiOutposts || f.defeated || !comp.Wars.AnyWar(f) || (pawnCount != null && (int)pawnCount.GetValue(o) == 0))
                        o.Destroy();
                }
                if (!set.aiOutposts || !set.warbands) return;

                foreach (War w in comp.Wars.wars.ToList())
                {
                    TryBuild(w.attacker, w.defender, comp);
                    TryBuild(w.defender, w.attacker, comp);
                }
                if (set.aiArtilleryOnPlayer) ShellPlayer();
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Vanilla Outposts Expanded daily round failed: " + e, 0x0E05);
            }
        }

        private void TryBuild(Faction f, Faction enemy, WorldComponent_Dominion comp)
        {
            if (f == null || enemy == null || f.IsPlayer || !DominionScheduler.IsSimulated(f) || !WarUtility.CanWage(f)) return;
            if (OutpostsOf(f).Count() >= MaxFor(f)) return;
            FactionState s = comp.GetState(f);
            float chance = 0.05f * (0.5f + s.effectiveAggression) * DominionMod.Settings.warbandFrequency;
            if (!Rand.Chance(chance)) return;

            // The front: the enemy town (or the player's colony) nearest to us, and our town nearest to it.
            PlanetTile home = DelegationUtility.HomeTile(f);
            PlanetTile target;
            if (enemy.IsPlayer)
            {
                Map map = VisitorUtility.PlayerCapitalMap();
                target = map?.Tile ?? PlanetTile.Invalid;
            }
            else target = WarUtility.NearestSettlement(enemy, home)?.Tile ?? PlanetTile.Invalid;
            if (!target.Valid || !DominionGeo.OnSurface(target)) return;
            PlanetTile front = WarbandUtility.LaunchTile(f, target);
            if (!front.Valid || !DominionGeo.OnSurface(front)) return;
            float frontDist = DominionGeo.Distance(front, target);
            if (frontDist < 0f) return;

            var kinds = new List<(Kind kind, float weight)>();
            if (encampmentDef != null) kinds.Add((Kind.Encampment, 0.45f));
            if (defensiveDef != null) kinds.Add((Kind.Defensive, 0.25f));
            // Artillery only pays when its guns can reach the enemy from somewhere near home.
            if (artilleryDef != null && frontDist <= FallbackRange + 6) kinds.Add((Kind.Artillery, enemy.IsPlayer ? 0.2f : 0.35f));
            if (kinds.Count == 0) return;
            Kind kind = kinds.RandomElementByWeight(k => k.weight).kind;
            WorldObjectDef def = kind == Kind.Encampment ? encampmentDef : kind == Kind.Defensive ? defensiveDef : artilleryDef;

            // A tile a few days out from our front town, towards the enemy (artillery: within range of it).
            bool Valid(PlanetTile t)
            {
                if (Find.WorldObjects.AnyWorldObjectAt(t) || Find.WorldObjects.AnySettlementBaseAtOrAdjacent(t)) return false;
                float toTarget = DominionGeo.Distance(t, target);
                if (toTarget < 0f || toTarget >= frontDist) return false;
                if (kind == Kind.Artillery) return toTarget <= FallbackRange - 1;
                if (kind == Kind.Defensive) return DominionGeo.Distance(t, front) <= 6f;
                return toTarget >= 3f;
            }
            if (!TileFinder.TryFindPassableTileWithTraversalDistance(front, 2, 10, out PlanetTile tile, Valid)) return;

            WorldObject o = WorldObjectMaker.MakeWorldObject(def);
            o.SetFaction(f);
            o.Tile = tile;
            nameField?.SetValue(o, "Dominion_Outpost_Name".Translate(f.Name, def.label).ToString());
            Find.WorldObjects.Add(o);

            // Garrisoned by the faction's own fighters.
            float points = Mathf.Clamp(WarbandUtility.FieldPoints(f) * 0.3f, 300f, 1500f);
            List<Pawn> pawns = InterceptUtility.GenerateFighters(f, points, tile);
            int added = 0;
            foreach (Pawn p in pawns)
            {
                bool ok = false;
                try { ok = (bool)addPawn.Invoke(o, new object[] { p }); }
                catch (Exception e) { DLog.WarningOnce("Adding a pawn to an AI outpost failed: " + e.Message, 0x0E06); }
                if (ok) added++;
                else if (!p.Destroyed) p.Destroy();
            }
            if (added == 0)
            {
                o.Destroy();
                return;
            }
            EventBus.Raise(IntegrationEventDefOf.Dominion_OutpostBuilt, f, enemy, tile.tileId, detail: def.label);
        }

        /// <summary>Artillery outposts at war with the player, with the colony in range, sometimes fire a barrage at it.</summary>
        private void ShellPlayer()
        {
            if (fire == null || artilleryCooldown == null || capablePawns == null || artilleryDef == null) return;
            if (!Find.Storyteller.difficulty.allowBigThreats) return;
            foreach (WorldObject o in Find.WorldObjects.AllWorldObjects.Where(IsAiOutpost).ToList())
            {
                if (o.def != artilleryDef || !o.Faction.HostileTo(Faction.OfPlayer) || !Rand.Chance(0.2f)) continue;
                if ((int)artilleryCooldown.GetValue(o) > 0) continue;
                if (!(capablePawns.GetValue(o) is IEnumerable crew) || !crew.Cast<object>().Any()) continue;
                Map map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && InRange(o, m.Tile));
                if (map == null) continue;
                // Aim at the colony's buildings, or anywhere in the home area.
                Thing aim = map.listerBuildings.allBuildingsColonist.Where(b => b.def.building != null && !b.def.building.isNaturalRock).RandomElementWithFallback();
                IntVec3 cell = aim?.Position ?? map.Center;
                try
                {
                    fire.Invoke(o, new object[] { new GlobalTargetInfo(cell, map) });
                }
                catch (Exception e)
                {
                    DLog.WarningOnce("AI artillery barrage failed: " + e.Message, 0x0E07);
                    continue;
                }
                DominionLetters.Send("Dominion_Outpost_ShellingLabel".Translate(), "Dominion_Outpost_ShellingText".Translate(o.Faction.Name, o.Label),
                    LetterDefOf.ThreatBig, new LookTargets(o), o.Faction, important: true);
                return; // one barrage a day is plenty
            }
        }

        // ---------------------------------------------------------------- a caravan attacks

        private void Attack(WorldObject outpost, Caravan caravan)
        {
            if (outpost == null || outpost.Destroyed || caravan == null || caravan.Destroyed || !InterceptUtility.InReach(caravan, outpost)) return;
            Faction f = outpost.Faction;
            Faction player = Faction.OfPlayer;
            if (!f.HostileTo(player))
            {
                WarUtility.Registry.pendingPlayerGoal = WarGoal.Punitive;
                DiplomacyUtility.SetRelationKind(player, f, FactionRelationKind.Hostile, player, "Dominion_Reason_OutpostAttacked".Translate());
            }
            int garrison = pawnCount != null ? (int)pawnCount.GetValue(outpost) : 4;
            float points = WarbandUtility.ForPlayerBattle(f, garrison * 150f, 0.6f);
            List<Pawn> enemies = InterceptUtility.GenerateFighters(f, points, caravan.Tile);
            string label = outpost.Label;
            // The garrison sallies out to meet the attack; the camp itself does not survive the day either way.
            outpost.Destroy();
            WarUtility.AddScore(player, f, 3f);
            if (enemies.Count == 0) return;
            Map map = CaravanIncidentUtility.SetupCaravanAttackMap(caravan, enemies, true);
            WarUtility.Registry.fights.Add(new WarbandFight { faction = f, enemies = new List<Pawn>(enemies), tile = map.Tile.tileId, points = points });
            Verse.AI.Group.LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, true, true, false, false, true, false, true), map, enemies);
            DominionLetters.Send("Dominion_Outpost_AttackedLabel".Translate(), "Dominion_Outpost_AttackedText".Translate(label, f.Name),
                LetterDefOf.ThreatBig, new LookTargets(enemies), f, important: true);
        }
    }
}
