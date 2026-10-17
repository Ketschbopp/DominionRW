using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>A world object a player caravan can chase down and attack: delegations and warbands.</summary>
    public interface IInterceptable
    {
        /// <summary>Tile the object will be on <paramref name="steps"/> tiles from now (its current tile at 0).</summary>
        PlanetTile TileAhead(int steps);

        /// <summary>Whether a caravan may go after it now.</summary>
        bool CanBeIntercepted { get; }

        /// <summary>The fight: the caravan has caught up.</summary>
        void Engage(Caravan caravan);
    }

    /// <summary>A caravan ordered to chase down a delegation or warband. Saved in the war registry.</summary>
    public class Pursuit : IExposable
    {
        public Caravan caravan;
        public WorldObject target;

        /// <summary>The destination we last gave the caravan; any other destination means the player changed their mind.</summary>
        public int lastDestination = -1;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (caravan != null && caravan.Destroyed) caravan = null;
                if (target != null && target.Destroyed) target = null;
            }
            Scribe_References.Look(ref caravan, "caravan");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref lastDestination, "lastDestination", -1);
        }
    }

    /// <summary>
    /// Interception as a chase: the caravan is steered toward where the target will be, and the fight starts as soon as
    /// the two are on the same or neighbouring tiles, so the player does not have to click at the exact moment they meet.
    /// </summary>
    public static class InterceptUtility
    {
        /// <summary>Within this many tiles a caravan has caught up.</summary>
        public const float ReachTiles = 1.5f;

        private static List<Pursuit> Pursuits => WorldComponent_Dominion.Instance.Wars.pursuits;

        public static bool InReach(Caravan caravan, WorldObject target)
        {
            if (caravan == null || target == null || caravan.Tile.Layer != target.Tile.Layer) return false;
            return caravan.Tile == target.Tile || (DominionGeo.Distance(caravan.Tile, target.Tile) is float d && d >= 0f && d <= ReachTiles);
        }

        /// <summary>The object as something a caravan can attack: our own world objects, or another mod's through an adapter.</summary>
        public static IInterceptable AsInterceptable(WorldObject o) => o as IInterceptable ?? Integration_VanillaOutposts.AsTarget(o);

        public static bool IsPursuing(Caravan caravan, WorldObject target) => Pursuits.Any(p => p.caravan == caravan && p.target == target);

        /// <summary>The float menu entry for a caravan. Acts of war are confirmed first.</summary>
        public static FloatMenuOption Option(Caravan caravan, WorldObject target, string label, bool actOfWar)
        {
            if (!(AsInterceptable(target) is IInterceptable it) || !it.CanBeIntercepted) return null;
            return new FloatMenuOption(label, () =>
            {
                if (actOfWar)
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_Intercept_ActOfWarConfirm".Translate(target.Faction?.Name ?? "?"),
                        () => Order(caravan, target), destructive: true));
                else Order(caravan, target);
            });
        }

        /// <summary>Sends the caravan after the target, or attacks at once if it is already within reach.</summary>
        public static void Order(Caravan caravan, WorldObject target)
        {
            if (caravan == null || caravan.Destroyed || target == null || target.Destroyed || !(AsInterceptable(target) is IInterceptable it)) return;
            Pursuits.RemoveAll(p => p.caravan == caravan);
            if (InReach(caravan, target))
            {
                it.Engage(caravan);
                return;
            }
            var p = new Pursuit { caravan = caravan, target = target };
            Pursuits.Add(p);
            Steer(p, it);
            Messages.Message("Dominion_Intercept_Ordered".Translate(caravan.Label, target.Label), caravan, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>Every scheduler slot: steer the chasers, and start the fight when one catches up.</summary>
        public static void Tick()
        {
            List<Pursuit> list = Pursuits;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count) continue;
                Pursuit p = list[i];
                if (p.caravan == null || p.caravan.Destroyed || !p.caravan.IsPlayerControlled || p.target == null || p.target.Destroyed
                    || !(AsInterceptable(p.target) is IInterceptable it) || !it.CanBeIntercepted)
                {
                    list.RemoveAt(i);
                    continue;
                }
                // The player sent the caravan somewhere else: the chase is off.
                if (p.lastDestination >= 0 && p.caravan.pather.Moving && p.caravan.pather.Destination.tileId != p.lastDestination)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (InReach(p.caravan, p.target))
                {
                    list.RemoveAt(i);
                    it.Engage(p.caravan);
                    continue;
                }
                Steer(p, it);
            }
        }

        /// <summary>Heads for the tile the target will reach about when the caravan does.</summary>
        private static void Steer(Pursuit p, IInterceptable it)
        {
            Caravan c = p.caravan;
            float dist = DominionGeo.Distance(c.Tile, p.target.Tile);
            if (dist < 0f) return; // not on the same layer: nothing to chase
            PlanetTile meet = it.TileAhead(Mathf.Clamp(Mathf.RoundToInt(dist * 0.5f), 0, 6));
            if (!meet.Valid || meet.Layer != c.Tile.Layer) meet = p.target.Tile;
            if (p.lastDestination == meet.tileId && c.pather.Moving) return;
            c.pather.StartPath(meet, null, repathImmediately: true);
            p.lastDestination = c.pather.Moving ? c.pather.Destination.tileId : meet.tileId;
        }

        /// <summary>
        /// Fighters for a battle on a caravan map: the faction's combat group if it has one, else its ordinary members.
        /// Never empty for a humanlike faction.
        /// </summary>
        public static List<Pawn> GenerateFighters(Faction f, float points, PlanetTile tile)
        {
            var result = new List<Pawn>();
            bool hasCombat = f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat);
            if (hasCombat)
            {
                try
                {
                    var parms = new PawnGroupMakerParms
                    {
                        groupKind = PawnGroupKindDefOf.Combat,
                        faction = f,
                        tile = tile,
                        points = Mathf.Max(points, f.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat)),
                        generateFightersOnly = true,
                    };
                    result.AddRange(PawnGroupMakerUtility.GeneratePawns(parms, false));
                }
                catch (System.Exception e)
                {
                    DLog.Warning("Generating fighters for " + f.Name + " failed: " + e.Message);
                }
            }
            if (result.Count == 0)
            {
                PawnKindDef kind = f.def.basicMemberKind ?? f.RandomPawnKind();
                int n = Mathf.Clamp(Mathf.RoundToInt(points / 150f), 2, 8);
                if (kind != null)
                    for (int i = 0; i < n; i++)
                        result.Add(PawnGenerator.GeneratePawn(kind, f, null));
            }
            return result;
        }
    }
}
