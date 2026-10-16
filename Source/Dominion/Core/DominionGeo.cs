using RimWorld.Planet;
using Verse;

namespace Dominion
{
    /// <summary>
    /// World-map geometry that is safe with several planet layers (Odyssey's orbit and space). Dominion's world
    /// simulation lives on the surface; tiles on different layers have no distance between them, and the game
    /// throws if asked for one.
    /// </summary>
    public static class DominionGeo
    {
        public static bool OnSurface(PlanetTile t) => t.Valid && t.Layer == Find.WorldGrid.Surface;

        public static bool SameLayer(PlanetTile a, PlanetTile b) => a.Valid && b.Valid && a.Layer == b.Layer;

        /// <summary>Approximate distance in tiles, or -1 when the tiles are on different layers or invalid.</summary>
        public static float Distance(PlanetTile a, PlanetTile b) =>
            SameLayer(a, b) ? Find.WorldGrid.ApproxDistanceInTiles(a, b) : -1f;
    }
}
