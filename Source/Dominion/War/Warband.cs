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
        /// <summary>
        /// A small, fast party that surveys an enemy settlement (or the player's colony) ahead of an attack. The next
        /// siege or raid on what it scouted is stronger; a caravan that catches the scouts denies it.
        /// </summary>
        Scout,
    }

    /// <summary>
    /// An army on the march: a faction's fighters crossing the world map along a real path. It is drawn only if the
    /// player has intel on its faction or it comes near the player's settlements or caravans. A player caravan on the
    /// same tile can intercept it, which is a battle (and an act of war if the two are not already at war).
    /// </summary>
    public class Warband : WorldObject, IInterceptable
    {
        /// <summary>Ticks per tile at speed 1 (an hour a tile). The warband speed setting divides it (default 2x).</summary>
        public const int BaseTicksPerTile = 2500;

        public static int TicksPerTile => Mathf.Max(250, Mathf.RoundToInt(BaseTicksPerTile / Mathf.Max(0.1f, DominionMod.Settings.warbandSpeed)));
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

        /// <summary>Ticks this party spends on each tile: scouts travel light and move twice as fast.</summary>
        public int StepTicks => mission == WarbandMission.Scout ? Mathf.Max(125, TicksPerTile / 2) : TicksPerTile;

        public int TicksToArrival => Mathf.Max(0, (path.Count - 1 - index) * StepTicks - ticksOnTile);

        public PlanetTile DestinationTile => path.Count > 0 ? new PlanetTile(path[path.Count - 1]) : Tile;

        /// <summary>The tiles still ahead, from the current one to the destination, every <paramref name="step"/>th one (the last always).</summary>
        public IEnumerable<PlanetTile> RouteAhead(int step = 1)
        {
            if (Blockading || path.Count == 0) yield break;
            int last = path.Count - 1;
            for (int i = Mathf.Min(index, last); i < last; i += Mathf.Max(1, step))
                yield return new PlanetTile(path[i]);
            yield return new PlanetTile(path[last]);
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Blockading)
            {
                if (Find.TickManager.TicksGame >= blockadeUntil) Destroy();
                return;
            }
            ticksOnTile += delta;
            if (ticksOnTile < StepTicks) return;
            ticksOnTile = 0;

            if (index >= path.Count - 1)
            {
                Arrive();
                return;
            }
            index++;
            Tile = new PlanetTile(path[index]);
            visibleCheckedTick = -99999;

            if (!warnedPlayer && mission == WarbandMission.RaidPlayer && target != null && target.IsPlayer && TicksToArrival < GenDate.TicksPerDay && VisibleToPlayer)
            {
                warnedPlayer = true;
                DominionLetters.Send("Dominion_Warband_ApproachingLabel".Translate(),
                    "Dominion_Warband_ApproachingText".Translate(Faction.Name, TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)),
                    LetterDefOf.ThreatSmall, this, Faction, important: true);
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
                return Vector3.Lerp(a, b, Mathf.Clamp01(ticksOnTile / (float)StepTicks));
            }
        }

        public override void Draw()
        {
            if (VisibleToPlayer) base.Draw();
        }

        public override bool SelectableNow => VisibleToPlayer && base.SelectableNow;

        public override Color ExpandingIconColor => VisibleToPlayer ? base.ExpandingIconColor : Color.clear;

        public override string Label => (mission == WarbandMission.Scout ? "Dominion_Scouts_Label" : "Dominion_Warband_Label").Translate(Faction?.Name ?? "?");

        /// <summary>What it is doing, in words: "Marching on X", "Reinforcing Y"...</summary>
        public string MissionText => ("Dominion_WarbandMission_" + mission + (mission == WarbandMission.Scout && (target?.IsPlayer ?? false) ? "Player" : "")).Translate(target?.Name ?? "?",
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
            if (Faction != null && Faction.HostileTo(Faction.OfPlayer))
            {
                sb.AppendLine();
                sb.Append("Dominion_Warband_BattleSize".Translate(WarbandUtility.ForPlayerBattle(Faction, Mathf.Min(points, 3000f), WarbandUtility.InterceptShareFor(this), vary: false).ToString("F0")));
            }
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

        public bool CanBeIntercepted => !Destroyed && Faction != null && !Faction.IsPlayer && !Faction.defeated && VisibleToPlayer;

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

    /// <summary>
    /// A fight on a caravan map between the player and an intercepted warband. Checked hourly: when the warband's
    /// fighters are all dead, downed or gone, the player won; when no colonist is left standing, the warband did.
    /// </summary>
    public class WarbandFight : IExposable
    {
        public Faction faction;
        public List<Pawn> enemies = new List<Pawn>();
        public int tile = -1;
        public float points;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) enemies.RemoveAll(p => p == null || p.Discarded);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref enemies, "enemies", LookMode.Reference);
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref points, "points");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                enemies ??= new List<Pawn>();
                enemies.RemoveAll(p => p == null);
            }
        }

        /// <summary>True when decided (and scored).</summary>
        public bool Check()
        {
            Map map = Find.Maps.FirstOrDefault(m => m.Tile.tileId == tile && DominionGeo.OnSurface(m.Tile));
            int dead = enemies.Count(p => p.Dead);
            if (map == null)
            {
                // The map closed (the caravan left): whoever still has most fighters on their feet carried the day.
                int alive = enemies.Count(p => !p.Dead && !p.Downed);
                Resolve(alive <= enemies.Count / 3, dead);
                return true;
            }
            // Fighters that fled the map count as beaten.
            int standing = enemies.Count(p => !p.Dead && !p.Downed && p.Spawned && p.Map == map && p.Faction == faction);
            if (standing == 0)
            {
                Resolve(true, dead);
                return true;
            }
            if (!map.mapPawns.FreeColonistsSpawned.Any(p => !p.Downed))
            {
                Resolve(false, dead);
                return true;
            }
            return false;
        }

        private void Resolve(bool playerWon, int dead)
        {
            Faction player = Faction.OfPlayer;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (faction == null || faction.defeated) return;
            // Bigger warbands are worth more: 6 to 25 points for beating one, 5 to 15 for the warband winning.
            if (playerWon)
            {
                float score = Mathf.Clamp(6f + points / 250f, 6f, 25f);
                WarUtility.AddScore(player, faction, score);
                if (DominionScheduler.IsSimulated(faction))
                {
                    FactionState s = comp.GetState(faction);
                    s.warWeariness = Mathf.Min(100f, s.warWeariness + 5f);
                    s.stability = Mathf.Max(0f, s.stability - 2f);
                }
                if (dead > 0) IntegrationRegistry.Get<Integration_EconomicsDemography>()?.ModifyPopulation(faction, -dead);
                EventBus.Raise(DominionEventDefOf.Dominion_BattleFought, player, faction, tile, detail: "Dominion_Warband_FieldBattle".Translate());
                Messages.Message("Dominion_Warband_Defeated".Translate(faction.Name, score.ToString("F0")), MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                float score = Mathf.Clamp(5f + points / 400f, 5f, 15f);
                WarUtility.AddScore(faction, player, score);
                EventBus.Raise(DominionEventDefOf.Dominion_BattleFought, faction, player, tile, detail: "Dominion_Warband_FieldBattle".Translate());
                Messages.Message("Dominion_Warband_Won".Translate(faction.Name, score.ToString("F0")), MessageTypeDefOf.NegativeEvent);
            }
        }
    }

    public static class WarbandUtility
    {
        /// <summary>Hard ceiling on warbands one faction keeps in the field.</summary>
        public const int MaxPerFaction = 6;

        /// <summary>
        /// How many warbands a faction can field at once: 1 for a small or weak one, up to 6 for a large, strong,
        /// aggressive one (one per three towns, plus one for being stronger than the world average, plus aggression).
        /// </summary>
        /// <summary>A whole warband caught on the road is as big as a raid on the colony; a sortie or an escort is less.</summary>
        public const float InterceptShare = 1f;
        public const float SortieShare = 0.7f;
        public const float EscortShare = 0.5f;
        public const float ScoutShare = 0.25f;

        public static float InterceptShareFor(Warband w) => w.mission == WarbandMission.Scout ? ScoutShare : InterceptShare;

        /// <summary>How long a scouting report stays good, and what it adds to the attack that uses it.</summary>
        public const int ScoutingDays = 20;
        public const float ScoutedSiegeBonus = 1.25f;
        public const float ScoutedRaidBonus = 1.15f;

        private static long ScoutKey(Faction f, int tile) => (long)f.loadID << 32 | (uint)tile;

        /// <summary>Whether <paramref name="f"/> has a fresh scouting report on the tile.</summary>
        public static bool HasScouted(Faction f, int tile) =>
            f != null && WorldComponent_Dominion.Instance.Wars.scoutedUntil.TryGetValue(ScoutKey(f, tile), out int until) && until > Find.TickManager.TicksGame;

        /// <summary>Uses up a scouting report: the force multiplier for an attack on the tile (1 if none).</summary>
        public static float ConsumeScouting(Faction f, int tile, float bonus)
        {
            if (!HasScouted(f, tile)) return 1f;
            WorldComponent_Dominion.Instance.Wars.scoutedUntil.Remove(ScoutKey(f, tile));
            return bonus;
        }

        /// <summary>Scouts are not counted towards a faction's warbands, but each faction sends one party at a time.</summary>
        public static bool ScoutOut(Faction f) => All().Any(x => x.Faction == f && x.mission == WarbandMission.Scout);

        public static int MaxFor(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(f);
            float n = 1f + comp.Cache.SettlementsOf(f).Count / 3f + (RelativeStrength(f) > 1.2f ? 1f : 0f) + s.effectiveAggression;
            return Mathf.Clamp(Mathf.FloorToInt(n), 1, MaxPerFaction);
        }

        /// <summary>A faction's military strength against the average of all simulated factions (1 = average).</summary>
        public static float RelativeStrength(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            float total = 0f;
            int n = 0;
            foreach (Faction g in comp.Cache.Simulated)
            {
                total += comp.GetState(g).militaryStrength;
                n++;
            }
            float avg = n > 0 ? total / n : 0f;
            return avg > 0f ? comp.GetState(f).militaryStrength / avg : 1f;
        }

        /// <summary>
        /// The faction's town nearest to <paramref name="to"/>, so a warband marches from the front, not the capital.
        /// Falls back to the capital (or any town).
        /// </summary>
        public static PlanetTile LaunchTile(Faction f, PlanetTile to)
        {
            PlanetTile best = PlanetTile.Invalid;
            float bestDist = float.MaxValue;
            if (to.Valid)
                foreach (Settlement s in WorldComponent_Dominion.Instance.Cache.SettlementsOf(f))
                {
                    float d = DominionGeo.Distance(s.Tile, to);
                    if (d >= 0f && d < bestDist && s.Tile != to)
                    {
                        bestDist = d;
                        best = s.Tile;
                    }
                }
            if (!best.Valid) best = DelegationUtility.HomeTile(f);
            // A forward encampment (Vanilla Outposts Expanded) nearer the target is a better place to set out from.
            PlanetTile forward = Integration_VanillaOutposts.ForwardBase(f, best, to);
            return forward.Valid ? forward : best;
        }

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
                if (w.Faction == f && w.mission != WarbandMission.Scout) n++;
            return n;
        }

        /// <summary>The force a faction puts in the field: a share of its strength, in combat points.</summary>
        /// <summary>A warband's size for this faction, with the luck of the levy (±25%).</summary>
        public static float FieldPoints(Faction f) => Mathf.Clamp(ExpectedPoints(f) * Rand.Range(0.75f, 1.3f), 200f, 5000f);

        /// <summary>
        /// The force the colony meets in a battle on a caravan map. A warband's own points are its size in the world
        /// simulation, which is often far smaller than the raids the colony is used to, and knows nothing of difficulty.
        /// So the battle is the larger of the two: the simulated force scaled by the difficulty's threat scale, or
        /// <paramref name="share"/> of a raid on the colony (which already follows wealth, difficulty and adaptation),
        /// scaled by how strong the faction is.
        /// </summary>
        public static float ForPlayerBattle(Faction f, float simPoints, float share, bool vary = true)
        {
            float sim = simPoints * Mathf.Clamp(Find.Storyteller.difficulty.threatScale, 0.3f, 2.5f);
            Map home = VisitorUtility.PlayerCapitalMap();
            float colony = home != null ? StorytellerUtility.DefaultThreatPointsNow(home) * share * Mathf.Clamp(RelativeStrength(f), 0.75f, 1.5f) : 0f;
            return Mathf.Max(sim, colony) * SizeSetting * (vary ? Vary() : 1f);
        }

        /// <summary>The player's "Warband fight size" setting.</summary>
        public static float SizeSetting => Mathf.Clamp(DominionMod.Settings.warbandFightSize, 0.1f, 5f);

        /// <summary>One fight's luck, within the player's "Warband fight variance" setting (±).</summary>
        public static float Vary()
        {
            float v = Mathf.Clamp(DominionMod.Settings.warbandFightVariance, 0f, 0.9f);
            return Rand.Range(1f - v, 1f + v);
        }

        /// <summary>A typical warband's size for this faction, without the luck of the levy.</summary>
        public static float ExpectedPoints(Faction f)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            // Strength sets the size; tech, temperament and the luck of the levy vary it.
            float tech = f.def.techLevel >= TechLevel.Spacer ? 1.25f : f.def.techLevel <= TechLevel.Neolithic ? 0.8f : 1f;
            float pts = s.militaryStrength * 5f * (1f - s.warWeariness / 200f) * tech * (0.85f + s.effectiveAggression * 0.3f);
            return Mathf.Clamp(pts, 200f, 5000f);
        }

        /// <summary>Sends a warband. Returns null if there is no route (surface only).</summary>
        public static Warband Launch(Faction from, Faction to, WarbandMission mission, Settlement targetSettlement = null, Faction ally = null)
        {
            PlanetTile home = DelegationUtility.HomeTile(from);
            PlanetTile end;
            if (mission == WarbandMission.Reinforce) end = DelegationUtility.HomeTile(ally);
            else if (mission == WarbandMission.RaidPlayer || mission == WarbandMission.Scout && to != null && to.IsPlayer)
            {
                Map map = VisitorUtility.PlayerCapitalMap();
                end = map != null ? map.Tile : PlanetTile.Invalid;
            }
            else
            {
                targetSettlement ??= WarUtility.NearestSettlement(to, home);
                end = targetSettlement?.Tile ?? PlanetTile.Invalid;
            }
            // March from the town nearest the target.
            PlanetTile start = LaunchTile(from, end);
            if (!start.Valid || !end.Valid || start == end) return null;
            PlanetLayer surface = Find.WorldGrid.Surface;
            if (start.Layer != surface || end.Layer != surface) return null;

            var w = (Warband)WorldObjectMaker.MakeWorldObject(DominionWorldObjectDefOf.Dominion_Warband);
            w.SetFaction(from);
            w.mission = mission;
            w.target = to;
            w.targetSettlement = targetSettlement;
            w.ally = ally;
            w.points = mission == WarbandMission.Scout ? Mathf.Max(100f, FieldPoints(from) * 0.15f)
                : FieldPoints(from) * (mission == WarbandMission.Blockade ? 0.6f : mission == WarbandMission.Reinforce ? 0.5f : 1f);
            w.points *= Integration_VanillaOutposts.LaunchBonus(from, start);
            if (!w.SetRoute(start, end)) return null;
            Find.WorldObjects.Add(w);
            if (mission == WarbandMission.Scout) return w; // scouts slip out quietly: no news
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
                case WarbandMission.Scout:
                {
                    if (enemy == null || !f.HostileTo(enemy)) return false;
                    WorldComponent_Dominion.Instance.Wars.scoutedUntil[ScoutKey(f, w.Tile.tileId)] = Find.TickManager.TicksGame + ScoutingDays * GenDate.TicksPerDay;
                    if (enemy.IsPlayer && w.VisibleToPlayer)
                        Messages.Message("Dominion_Scouts_SurveyedColony".Translate(f.Name), w, MessageTypeDefOf.ThreatSmall, historical: true);
                    return false;
                }
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
            garrison *= Integration_VanillaOutposts.GarrisonFactor(attacker, defender, s);
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
            // A scouted town is attacked where it is weakest.
            float points = w.points * ConsumeScouting(attacker, s.Tile.tileId, ScoutedSiegeBonus);
            var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
            if (ftv != null && ftv.CanInvade && DominionMod.Settings.warOccupation && ftv.TryStartInvasion(s, attacker))
            {
                comp.Wars.siegePoints[s.Tile.tileId] = points;
                if (war != null) war.battles++;
                return; // the ownership watch reports the outcome
            }
            ResolveBattle(attacker, defender, s, points);
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
            // Peaceful difficulty has no raids: the warband turns back at the border.
            if (!Find.Storyteller.difficulty.allowBigThreats) return;
            // Sized from the storyteller's own raids on the colony (wealth, colonists, difficulty, adaptation). A warband
            // is an army sent on purpose, so it is bigger than an ordinary raid: 110% to 200%, by how strong this faction
            // is and how big the warband was (about 140% for an average one).
            float strength = Mathf.Clamp(RelativeStrength(w.Faction), 0.6f, 1.8f);
            float size = Mathf.Clamp(w.points / Mathf.Max(200f, ExpectedPoints(w.Faction)), 0.7f, 1.4f);
            float points = StorytellerUtility.DefaultThreatPointsNow(map) * Mathf.Clamp(0.9f + 0.5f * strength * size, 1.1f, 2f) * SizeSetting * Vary();
            // Raiders who scouted the colony first come better prepared.
            points *= ConsumeScouting(w.Faction, map.Tile.tileId, ScoutedRaidBonus);
            var parms = new IncidentParms
            {
                target = map,
                faction = w.Faction,
                points = Mathf.Max(points, w.Faction.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)),
                forced = true,
            };
            var before = new HashSet<Pawn>(map.mapPawns.SpawnedPawnsInFaction(w.Faction));
            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                DLog.Verbose("Warband raid on the player could not start: " + w.Faction.Name);
            else
            {
                // Beating the raid is beating the warband: it is scored like an interception.
                List<Pawn> raiders = map.mapPawns.SpawnedPawnsInFaction(w.Faction).Where(p => !before.Contains(p)).ToList();
                if (raiders.Count > 0)
                    WarUtility.Registry.fights.Add(new WarbandFight { faction = w.Faction, enemies = raiders, tile = map.Tile.tileId, points = w.points });
            }
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

            List<Pawn> enemies = InterceptUtility.GenerateFighters(f, ForPlayerBattle(f, Mathf.Min(w.points, 3000f), InterceptShareFor(w)), caravan.Tile);
            EventBus.Raise(DominionEventDefOf.Dominion_WarbandIntercepted, player, f, caravan.Tile.tileId, detail: w.target?.Name);
            // Catching them on the road is worth a little; beating them is scored when the fight is decided.
            WarUtility.AddScore(player, f, 2f);
            float points = w.points;
            w.Destroy();

            if (enemies.Count == 0) return;
            Map map = CaravanIncidentUtility.SetupCaravanAttackMap(caravan, enemies, true);
            WarUtility.Registry.fights.Add(new WarbandFight { faction = f, enemies = new List<Pawn>(enemies), tile = map.Tile.tileId, points = points });
            LordMaker.MakeNewLord(f, new LordJob_AssaultColony(f, true, true, false, false, true, false, true), map, enemies);
            DominionLetters.Send("Dominion_Warband_InterceptedLabel".Translate(), "Dominion_Warband_InterceptedText".Translate(f.Name),
                LetterDefOf.ThreatBig, new LookTargets(enemies), f, important: true);
        }
    }
}
