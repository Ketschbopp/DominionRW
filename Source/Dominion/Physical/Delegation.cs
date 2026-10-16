using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum DelegationPurpose
    {
        /// <summary>Two AI factions conclude an agreement (peace, alliance or a treaty) when it arrives.</summary>
        Agreement,
        /// <summary>An envoy brings the player a treaty offer.</summary>
        OfferToPlayer,
        /// <summary>A faction leader travels to a summit hosted by the player.</summary>
        SummitToPlayer,
    }

    public enum AgreementKind { Peace, Alliance, Treaty }

    /// <summary>
    /// A diplomatic party travelling the world map along a real path at caravan speed. It is only shown to the
    /// player if it involves them or they have enough intel on its faction, and it can be intercepted by a
    /// player caravan on the same tile, which ends the mission and starts a fight.
    /// </summary>
    public class Delegation : WorldObject
    {
        public const int TicksPerTile = 2000;
        public const float VisibleIntel = IntelUtility.RestrictedIntel;

        public DelegationPurpose purpose;
        public Faction target;
        public AgreementKind agreement;
        public TreatyDef treaty;
        public int summitId = -1;

        private List<int> path = new List<int>();
        private int index;
        private int ticksOnTile;
        private int destination = -1;
        private bool warnedCaravan;

        public bool InvolvesPlayer => (Faction?.IsPlayer ?? false) || (target?.IsPlayer ?? false);

        public bool VisibleToPlayer => InvolvesPlayer || IntelUtility.IntelOn(Faction) >= VisibleIntel || IntelUtility.IntelOn(target) >= VisibleIntel;

        public PlanetTile Destination => new PlanetTile(destination);

        /// <summary>Sets the route. Returns false if no path exists.</summary>
        public bool SetRoute(PlanetTile from, PlanetTile to)
        {
            destination = to.tileId;
            WorldPath wp = Find.WorldGrid.Surface.Pather.FindPath(from, to, null, null);
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

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
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

            if (!InvolvesPlayer && VisibleToPlayer)
            {
                Caravan caravan = Find.WorldObjects.PlayerControlledCaravanAt(Tile);
                if (caravan != null && !warnedCaravan)
                {
                    warnedCaravan = true; // once per journey, so it does not stall at every caravan
                    // Linger a while so the player has a chance to act.
                    ticksOnTile = -GenDate.TicksPerHour * 2;
                    Messages.Message("Dominion_Delegation_PassingCaravan".Translate(Faction.Name, caravan.Label), this, MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
        }

        public override Vector3 DrawPos
        {
            get
            {
                if (path.Count == 0) return base.DrawPos;
                Vector3 a = Find.WorldGrid.GetTileCenter(new PlanetTile(path[index]));
                if (index >= path.Count - 1) return a;
                Vector3 b = Find.WorldGrid.GetTileCenter(new PlanetTile(path[index + 1]));
                return Vector3.Lerp(a, b, Mathf.Clamp01(ticksOnTile / (float)TicksPerTile));
            }
        }

        public override void Draw()
        {
            if (VisibleToPlayer) base.Draw();
        }

        public override bool SelectableNow => VisibleToPlayer && base.SelectableNow;

        // Zoomed out, the map draws expanding icons separately from Draw(); hide those too.
        public override Color ExpandingIconColor => VisibleToPlayer ? base.ExpandingIconColor : Color.clear;

        public override string Label => "Dominion_Delegation_Label".Translate(Faction?.Name ?? "?");

        private void Arrive()
        {
            DelegationUtility.Arrived(this);
            if (!Destroyed) Destroy();
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Dominion_Delegation_Inspect".Translate(target?.Name ?? "?", TicksToArrival.ToStringTicksToPeriod(allowSeconds: false)));
            return sb.ToString();
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (FloatMenuOption o in base.GetFloatMenuOptions(caravan))
                yield return o;
            if (!caravan.IsPlayerControlled || Faction == null || Faction.IsPlayer) yield break;
            if (caravan.Tile == Tile)
                yield return new FloatMenuOption("Dominion_Delegation_Intercept".Translate(Faction.Name), () => DelegationUtility.Intercept(this, caravan));
            else
                yield return new FloatMenuOption("Dominion_Delegation_MoveToIntercept".Translate(), () =>
                {
                    // Head for a tile a little ahead on its route.
                    int ahead = Mathf.Min(path.Count - 1, index + 3);
                    caravan.pather.StartPath(new PlanetTile(path[ahead]), null, repathImmediately: true);
                });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref purpose, "purpose");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref agreement, "agreement");
            Scribe_Defs.Look(ref treaty, "treaty");
            Scribe_Values.Look(ref summitId, "summitId", -1);
            Scribe_Collections.Look(ref path, "path", LookMode.Value);
            Scribe_Values.Look(ref index, "index");
            Scribe_Values.Look(ref ticksOnTile, "ticksOnTile");
            Scribe_Values.Look(ref destination, "destination", -1);
            Scribe_Values.Look(ref warnedCaravan, "warnedCaravan");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) path ??= new List<int>();
        }
    }
}
