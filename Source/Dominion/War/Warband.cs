using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Dominion
{
    public enum WarbandMission
    {
        /// <summary>Attacks an enemy settlement; a win can occupy it.</summary>
        Siege,
        /// <summary>Raids the player's colony on arrival.</summary>
        RaidPlayer,
        /// <summary>Sits outside an enemy settlement for a while, strangling its trade.</summary>
        Blockade,
        /// <summary>Marches to an ally at war and strengthens it.</summary>
        Reinforce,
    }

    /// <summary>
    /// An army on the march: a faction's fighters crossing the world map along a real path. It is drawn only if the
    /// player has intel on its faction or it comes near the player's settlements or caravans. A player caravan on the
    /// same tile can intercept it, which is a battle (and an act of war if the two are not already at war).
    /// </summary>
    public class Warband : WorldObject, IInterceptable
    {
        public const int TicksPerTile = 2500;
        public const float NearPlayerTiles = 8f;
        public const int BlockadeDays = 6;

        public WarbandMission mission;
        public Faction target;
        public Settlement targetSettlement;

        /// <summary>For reinforcements: the ally being helped.</summary>
        public Faction ally;

        /// <summary>Combat points: the size of the force.</summary>
        public float points;

        private List<int> path = new List<int>();
        private int index;
        private int ticksOnTile;
        private int blockadeUntil = -1;
        private bool warnedPlayer;
        private bool warnedCaravan;

        // Recomputed on each step, not saved.
        [Unsaved] private bool visibleCache;
        [Unsaved] private int visibleCheckedTick = -99999;

        public bool InvolvesPlayer => (Faction?.IsPlayer ?? false) || (target?.IsPlayer ?? false) || (ally?.IsPlayer ?? false);

        public bool Blockading => blockadeUntil >= 0;

        public bool VisibleToPlayer
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now - visibleCheckedTick > 250)
                {
                    visibleCheckedTick = now;
                    visibleCache = ComputeVisible();
                }
                return visibleCache;
            }
        }

        private bool ComputeVisible()
        {
            if (InvolvesPlayer || IntelUtility.IntelOn(Faction) >= IntelUtility.RestrictedIntel) return true;
            return WarbandUtility.NearPlayerAssets(Tile, NearPlayerTiles);
        }

        public bool SetRoute(PlanetTile from, PlanetTile to)
        {
            if (from.Layer != to.Layer) return false;
            WorldPath wp = from.Layer.Pather.FindPath(from, to, null, null);
            if (wp == null || !wp.Found)
            {
                wp?.ReleaseToPool();
                return false;
            }
            path.Clear();
            List<PlanetTile> nodes = wp.NodesReversed;
            for (int i = nodes.Count - 1; i >= 0; i--)
                path.Add(nodes[i].tileId);
            wp.ReleaseToPool();
            index = 0;
            Tile = from;
            return path.Count > 0;
        }

        public int TicksToArrival => Mathf.Max(0, (path.Count - 1 - index) * TicksPerTile - ticksOnTile);

        public PlanetTile DestinationTile => path.Count > 0 ? new PlanetTile(path[path.Count - 1]) : Tile;

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Blockading)
            {
                if (Find.TickManager.TicksGame >= blockadeUntil) Destroy();
                return;
            }
            ticksOnTile += delta;
            if (ticksOnTile < TicksPerTile) return;
            ticksOnTile = 0;

            if (index >= path.Count - 1)
            {
                Arrive();
                return;
            }
            index++;
            Tile = new PlanetTile(path[index]);
            visibleCheckedTick = -99999;

            if (!warnedPlayer && target != null && target.IsPlayer && TicksToArrival < GenDate.TicksPerDay && VisibleToPlayer)
            {
                warnedPlayer = true;
                Find.LetterStack.ReceiveLetter("Dominion_Warband_ApproachingLabel".Translate(),
                    "Dominion_Warband_ApproachingText".Translate(Faction.Name, TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)),
                    LetterDefOf.ThreatSmall, this, Faction);
            }
            if (!warnedCaravan && VisibleToPlayer)
            {
                Caravan caravan = Find.WorldObjects.PlayerControlledCaravanAt(Tile);
                if (caravan != null)
                {
                    warnedCaravan = true;
                    ticksOnTile = -GenDate.TicksPerHour * 2; // a short halt: time to act
                    Messages.Message("Dominion_Warband_PassingCaravan".Translate(Faction.Name, caravan.Label), this, MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
        }

        private void Arrive()
        {
            bool stays = WarbandUtility.Arrived(this);
            if (stays && mission == WarbandMission.Blockade)
            {
                blockadeUntil = Find.TickManager.TicksGame + BlockadeDays * GenDate.TicksPerDay;
                return;
            }
            if (!Destroyed) Destroy();
        }

        public override Vector3 DrawPos
        {
            get
            {
                if (path.Count == 0) return base.DrawPos;
                Vector3 a = Find.WorldGrid.GetTileCenter(new PlanetTile(path[index]));
                if (index >= path.Count - 1 || ticksOnTile <= 0) return a;
                Vector3 b = Find.WorldGrid.GetTileCenter(new PlanetTile(path[index + 1]));
                return Vector3.Lerp(a, b, Mathf.Clamp01(ticksOnTile / (float)TicksPerTile));
            }
        }

        public override void Draw()
        {
            if (VisibleToPlayer) base.Draw();
        }

        public override bool SelectableNow => VisibleToPlayer && base.SelectableNow;

        public override Color ExpandingIconColor => VisibleToPlayer ? base.ExpandingIconColor : Color.clear;

        public override string Label => "Dominion_Warband_Label".Translate(Faction?.Name ?? "?");

        /// <summary>What it is doing, in words: "Marching on X", "Reinforcing Y"...</summary>
        public string MissionText => ("Dominion_WarbandMission_" + mission).Translate(target?.Name ?? "?",
            mission == WarbandMission.Reinforce ? ally?.Name ?? "?" : targetSettlement?.Label ?? target?.Name ?? "?");

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(MissionText);
            sb.AppendLine();
            if (Blockading)
                sb.Append("Dominion_Warband_BlockadeLeft".Translate((blockadeUntil - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)));
            else
                sb.Append("Dominion_Warband_Arrives".Translate(TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)));
            sb.AppendLine();
            sb.Append("Dominion_Warband_Strength".Translate(points.ToString("F0")));
            return sb.ToString();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (FloatMenuOption o in base.GetFloatMenuOptions(caravan))
                yield return o;
            if (!caravan.IsPlayerControlled || Faction == null || Faction.IsPlayer || !VisibleToPlayer) yield break;
            bool actOfWar = !Faction.HostileTo(Faction.OfPlayer);
            string label = (InterceptUtility.IsPursuing(caravan, this) ? "Dominion_Intercept_Pursuing" : "Dominion_Warband_Intercept").Translate(Faction.Name).ToString()
                           + (actOfWar ? " " + "Dominion_Intercept_ActOfWarTag".Translate().ToString() : "");
            FloatMenuOption opt = InterceptUtility.Option(caravan, this, label, actOfWar);
            if (opt != null) yield return opt;
        }

        public PlanetTile TileAhead(int steps) => path.Count == 0 || Blockading ? Tile : new PlanetTile(path[Mathf.Min(path.Count - 1, index + steps)]);

        public bool CanBeIntercepted => !Destroyed && Faction != null && !Faction.IsPlayer && VisibleToPlayer;

        public void Engage(Caravan caravan) => WarbandUtility.Intercept(this, caravan);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mission, "mission");
            Scribe_References.Look(ref target, "target");
            if (Scribe.mode == LoadSaveMode.Saving && targetSettlement != null && targetSettlement.Destroyed) targetSettlement = null;
            Scribe_References.Look(ref targetSettlement, "targetSettlement");
            Scribe_References.Look(ref ally, "ally");
            Scribe_Values.Look(ref points, "points");
            Scribe_Collections.Look(ref path, "path", LookMode.Value);
            Scribe_Values.Look(ref index, "index");
            Scribe_Values.Look(ref ticksOnTile, "ticksOnTile");
            Scribe_Values.Look(ref blockadeUntil, "blockadeUntil", -1);
            Scribe_Values.Look(ref warnedPlayer, "warnedPlayer");
            Scribe_Values.Look(ref warnedCaravan, "warnedCaravan");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) path ??= new List<int>();
        }
    }

    public static class WarbandUtility
    {
        public const int MaxPerFaction = 2;

        /// <summary>Within <paramref name="range"/> tiles of a player settlement or caravan.</summary>
        public static bool NearPlayerAssets(PlanetTile tile, float range)
        {
            if (!tile.Valid) return false;
            WorldGrid grid = Find.WorldGrid;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome && map.Tile.Layer == tile.Layer && grid.ApproxDistanceInTiles(map.Tile, tile) <= range) return true;
            foreach (Caravan c in Find.WorldObjects.Caravans)
                if (c.IsPlayerControlled && c.Tile.Layer == tile.Layer && grid.ApproxDistanceInTiles(c.Tile, tile) <= range) return true;
            return false;
        }

        public static IEnumerable<Warband> All()
        {
            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is Warband w && !w.Destroyed) yield return w;
        }

        public static int CountActive(Faction f)
        {
            int n = 0;
            foreach (Warband w in All())
                if (w.Faction == f) n++;
            return n;
        }

        /// <summary>The force a faction puts in the field: a share of its strength, in combat points.</summary>
        public static float FieldPoints(Faction f)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            float pts = s.militaryStrength * 5f * (1f - s.warWeariness / 200f);
            return Mathf.Clamp(pts, 250f, 3000f);
        }

        /// <summary>Sends a warband. Returns null if there is no route (surface only).</summary>
        public static Warband Launch(Faction from, Faction to, WarbandMission mission, Settlement targetSettlement = null, Faction ally = null)
        {
            PlanetTile start = DelegationUtility.HomeTile(from);
            PlanetTile end;
            if (mission == WarbandMission.Reinforce) end = DelegationUtility.HomeTile(ally);
            else if (mission == WarbandMission.RaidPlayer)
            {
                Map map = VisitorUtility.PlayerCapitalMap();
                end = map != null ? map.Tile : PlanetTile.Invalid;
            }
            else
            {
                targetSettlement ??= WarUtility.NearestSettlement(to, start);
                end = targetSettlement?.Tile ?? PlanetTile.Invalid;
            }
            if (!start.Valid || !end.Valid || start == end) return null;
            PlanetLayer surface = Find.WorldGrid.Surface;
            if (start.Layer != surface || end.Layer != surface) return null;

            var w = (Warband)WorldObjectMaker.MakeWorldObject(DominionWorldObjectDefOf.Dominion_Warband);
            w.SetFaction(from);
            w.mission = mission;
            w.target = to;
            w.targetSettlement = targetSettlement;
            w.ally = ally;
            w.points = FieldPoints(from) * (mission == WarbandMission.Blockade ? 0.6f : mission == WarbandMission.Reinforce ? 0.5f : 1f);
            if (!w.SetRoute(start, end)) return null;
            Find.WorldObjects.Add(w);
            EventBus.Raise(mission == WarbandMission.Reinforce ? DominionEventDefOf.Dominion_Reinforcements : DominionEventDefOf.Dominion_WarbandMarching,
                from, mission == WarbandMission.Reinforce ? ally : to, start.tileId,
                detail: targetSettlement?.Label ?? (mission == WarbandMission.Reinforce ? ally?.Name : to?.Name));
            return w;
        }

        /// <summary>Removes every warband one faction has in the field against the other (the war is over).</summary>
        public static void Recall(Faction a, Faction b)
        {
            foreach (Warband w in All().ToList())
                if ((w.Faction == a && w.target == b) || (w.Faction == b && w.target == a))
                    w.Destroy();
        }

        /// <summary>Resolves the warband's mission. Returns true if it stays on the map afterwards (a blockade).</summary>
        public static bool Arrived(Warband w)
        {
            Faction f = w.Faction;
            Faction enemy = w.target;
            if (f == null || f.defeated) return false;
            switch (w.mission)
            {
                case WarbandMission.Reinforce:
                {
                    Faction ally = w.ally;
                    if (ally == null || ally.defeated) return false;
                    var wars = new List<War>();
                    WarUtility.Registry.WarsOf(ally, wars);
                    foreach (War war in wars)
                        war.AddScore(ally, 6f);
                    if (!ally.IsPlayer) WorldComponent_Dominion.Instance.GetState(ally).militaryStrength += w.points / 50f;
                    WorldComponent_Dominion.Instance.GetRelation(ally, f).favour += 6f;
                    return false;
                }
                case WarbandMission.RaidPlayer:
                    if (enemy == null || !f.HostileTo(enemy)) return false;
                    RaidPlayer(w);
                    return false;
                case WarbandMission.Blockade:
                {
                    if (enemy == null || !f.HostileTo(enemy) || w.targetSettlement == null || w.targetSettlement.Destroyed) return false;
                    EventBus.Raise(DominionEventDefOf.Dominion_Blockade, f, enemy, w.Tile.tileId, detail: w.targetSettlement.Label);
                    return true;
                }
                default:
                    if (enemy == null || !f.HostileTo(enemy)) return false;
                    Siege(w);
                    return false;
            }
        }

        /// <summary>Daily, for a warband blockading a settlement: the town's trade suffers and the war tilts.</summary>
        public static void BlockadeDay(Warband w)
        {
            Faction enemy = w.target;
            if (enemy == null || enemy.IsPlayer || !w.Faction.HostileTo(enemy)) return;
            EconomyEffects.DestroyGoods(enemy, 150f);
            WarUtility.AddScore(w.Faction, enemy, 1.5f);
        }

        /// <summary>Chance an attacking force of <paramref name="points"/> takes the settlement. The garrison has walls.</summary>
        public static float SiegeChance(float points, Faction attacker, Faction defender, Settlement s)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState ds = comp.GetState(defender);
            int count = Mathf.Max(1, comp.Cache.SettlementsOf(defender).Count);
            float garrison = Mathf.Clamp(ds.militaryStrength * 5f / count, 150f, 3000f) + 150f;
            if (s != null && ds.capital == s) garrison *= 1.5f;
            if (Find.TickManager.TicksGame < ds.sabotagedUntilTick) garrison *= 0.85f;
            float attack = points * MilitarySystem.TechFactor(attacker.def.techLevel) / MilitarySystem.TechFactor(defender.def.techLevel);
            return Mathf.Clamp(attack / (attack + garrison * 1.2f), 0.05f, 0.95f);
        }

        /// <summary>The force besieging a tile: the Dominion warband that started it, or what the attacker can field.</summary>
        public static float SiegePointsAt(int tile, Faction attacker) =>
            WorldComponent_Dominion.Instance.Wars.siegePoints.TryGetValue(tile, out float p) ? p : FieldPoints(attacker);

        /// <summary>
        /// The warband reaches an AI settlement. With Faction Territories it becomes an FTV base invasion: it shows on the
        /// map, the player can join it, and it is decided with Dominion's odds. Otherwise the battle is fought off-map now.
        /// </summary>
        private static void Siege(Warband w)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction attacker = w.Faction, defender = w.target;
            Settlement s = w.targetSettlement;
            if (s == null || s.Destroyed || s.Faction != defender)
                s = WarUtility.NearestSettlement(defender, w.Tile);
            if (s == null || defender.IsPlayer) return;
            if (s.HasMap) return; // the player is there; no battle off-screen on a map they can see

            War war = comp.Wars.Find(attacker, defender);
            var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            if (ftv != null && ftv.CanInvade && DominionMod.Settings.warOccupation && ftv.TryStartInvasion(s, attacker))
            {
                comp.Wars.siegePoints[s.Tile.tileId] = w.points;
                if (war != null) war.battles++;
                return; // the ownership watch reports the outcome
            }
            ResolveBattle(attacker, defender, s, w.points);
        }

        /// <summary>An off-map battle at an AI settlement: losses, war score, and occupation (or plunder) if the attacker wins.</summary>
        public static void ResolveBattle(Faction attacker, Faction defender, Settlement s, float points)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState ds = comp.GetState(defender);
            FactionState atk = comp.GetState(attacker);
            bool won = Rand.Chance(SiegeChance(points, attacker, defender, s));
            float garrison = Mathf.Clamp(ds.militaryStrength * 5f / Mathf.Max(1, comp.Cache.SettlementsOf(defender).Count), 150f, 3000f);

            Faction winner = won ? attacker : defender, loser = won ? defender : attacker;
            var ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
            ed?.ModifyPopulation(attacker, -Mathf.RoundToInt(Mathf.Clamp(points / 150f, 1f, 15f) * (won ? 0.5f : 1f)));
            ed?.ModifyPopulation(defender, -Mathf.RoundToInt(Mathf.Clamp(garrison / 150f, 1f, 15f) * (won ? 1f : 0.5f)));
            atk.warWeariness = Mathf.Min(100f, atk.warWeariness + 2f);
            ds.warWeariness = Mathf.Min(100f, ds.warWeariness + 2f);
            EconomyEffects.Shock(loser, -0.02f);

            War war = comp.Wars.Find(attacker, defender);
            if (war != null) war.battles++;
            EventBus.Raise(DominionEventDefOf.Dominion_BattleFought, winner, loser, s.Tile.tileId, detail: s.Label);
            WarUtility.AddScore(winner, loser, won ? 10f : 8f);

            if (!won) return;
            // The victors carry off what they can.
            EconomyEffects.Transfer(defender, attacker, Mathf.RoundToInt(Mathf.Clamp(ds.wealth * 0.02f / Mathf.Max(1, ds.settlementCount), 100f, 1500f)));
            if (WarUtility.CanOccupy(s, attacker))
                WarUtility.Occupy(s, attacker);
            else
            {
                // Their last stronghold holds out, but barely; with Pillage & Plunder, it is sacked.
                // A sack is scored by the Pillage & Plunder hook; without it, the near-miss still counts for something.
                bool sacked = IntegrationRegistry.Get<Integration_PillageAndPlunder>()?.TryPlunder(attacker, s) ?? false;
                if (!sacked && war != null) war.AddScore(attacker, 10f);
            }
        }

        /// <summary>The warband reaches the player's colony: a raid, sized like any other raid on it.</summary>
        private static void RaidPlayer(Warband w)
        {
            Map map = VisitorUtility.PlayerCapitalMap();
            if (map == null || map.Tile != w.Tile) map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && m.Tile == w.Tile) ?? map;
            if (map == null) return;
            float points = StorytellerUtility.DefaultThreatPointsNow(map) * Rand.Range(0.8f, 1.2f);
            var parms = new IncidentParms
            {
                target = map,
                faction = w.Faction,
                points = Mathf.Max(points, w.Faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)),
                forced = true,
            };
            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                DLog.Verbose("Warband raid on the player could not start: " + w.Faction.Name);
            WorldComponent_Dominion.Instance.GetState(w.Faction).lastRaidOnPlayerTick = Find.TickManager.TicksGame;
        }

        /// <summary>A player caravan attacks the warband: a real battle on a caravan map.</summary>
        public static void Intercept(Warband w, Caravan caravan)
        {
            if (w == null || w.Destroyed || caravan == null || caravan.Destroyed || !InterceptUtility.InReach(caravan, w)) return;
            Faction f = w.Faction;
            Faction player = Faction.OfPlayer;
            if (!f.HostileTo(player))
            {
                WarUtility.Registry.pendingPlayerGoal = WarGoal.Punitive;
                DiplomacyUtility.SetRelationKind(player, f, FactionRelationKind.Hostile, player, "Dominion_Reason_WarbandIntercepted".Translate());
            }

            List<Pawn> enemies = InterceptUtility.GenerateFighters(f, Mathf.Min(w.points, 3000f), caravan.Tile);
            EventBus.Raise(DominionEventDefOf.Dominion_WarbandIntercepted, player, f, caravan.Tile.tileId, detail: w.target?.Name);
            WarUtility.AddScore(player, f, 6f);
            w.Destroy();

            if (enemies.Count == 0) return;
            Map map = CaravanIncidentUtility.SetupCaravanAttackMap(caravan, enemies, true);
            LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, true, true, false, false, true, false, true), map, enemies);
            Find.LetterStack.ReceiveLetter("Dominion_Warband_InterceptedLabel".Translate(), "Dominion_Warband_InterceptedText".Translate(f.Name),
                LetterDefOf.ThreatBig, new LookTargets(enemies), f);
        }
    }
}
