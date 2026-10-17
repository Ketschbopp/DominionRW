using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A flat picture of the planet's surface (longitude across, latitude down) with each faction's territory tinted in
    /// its colour, borders where two meet, and fronts in red where they are at war. Territory is FTV's own, tile by tile;
    /// only if FTV's territory cannot be read (or has not been worked out yet) is it drawn as a sphere of influence round
    /// each settlement, spreading over land only. Built once into a texture and rebuilt when settlements change hands,
    /// wars start and end, and once a day while FTV territory is shown, as its borders move.
    /// </summary>
    public static class TerritoryMap
    {
        public const int W = 512;
        public const int H = 256;

        /// <summary>Reach of an ordinary settlement and of a capital, in pixels (about 5 and 8 degrees).</summary>
        private const float Reach = 7.5f;
        private const float CapitalReach = 11.5f;

        private static readonly Color32 Sea = new Color32(22, 31, 42, 255);
        private static readonly Color32 Coast = new Color32(36, 50, 62, 255);
        private static readonly Color32 Land = new Color32(74, 70, 58, 255);
        private static readonly Color32 Front = new Color32(235, 55, 40, 255);

        private static Texture2D texture;
        private static World builtFor;
        private static byte[] land; // 0 sea, 1 land
        private static byte[] hills;
        private static int[] pixelTile; // the surface tile each pixel shows
        private static bool fromFtv;
        private static int[] owner; // index into factions, -1 for none
        private static readonly List<Faction> factions = new List<Faction>();
        private static int signature;
        private static float nextCheck;

        public static Texture2D Texture
        {
            get
            {
                Refresh();
                return texture;
            }
        }

        /// <summary>The faction whose influence covers a point of the map (0..1 across and down), or null.</summary>
        public static Faction OwnerAt(Vector2 p)
        {
            if (owner == null || p.y < 0f || p.y >= 1f) return null;
            int i = Index(p);
            return owner[i] >= 0 && owner[i] < factions.Count ? factions[owner[i]] : null;
        }

        public static bool IsLand(Vector2 p) => land != null && p.y >= 0f && p.y < 1f && land[Index(p)] == 1;

        private static int Index(Vector2 p)
        {
            int x = ((int)(Mathf.Repeat(p.x, 1f) * W)) % W;
            int y = Mathf.Clamp((int)(p.y * H), 0, H - 1);
            return y * W + x;
        }

        /// <summary>A surface tile as 0..1 map coordinates.</summary>
        public static Vector2 Project(PlanetTile tile)
        {
            Vector2 ll = Find.WorldGrid.LongLatOf(tile);
            return new Vector2((ll.x + 180f) / 360f, (90f - ll.y) / 180f);
        }

        private static void Refresh()
        {
            World world = Find.World;
            if (world == null) return;
            if (texture == null)
            {
                texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            }
            if (builtFor != world)
            {
                builtFor = world;
                BuildLand();
                signature = 0;
                nextCheck = 0f;
            }
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 2f;
            int sig = Signature();
            if (sig == signature) return;
            signature = sig;
            BuildOwners();
            Paint();
        }

        // ------------------------------------------------------------------ land and sea

        private static void BuildLand()
        {
            land = new byte[W * H];
            hills = new byte[W * H];
            pixelTile = new int[W * H];
            var known = new bool[W * H];
            var queue = new Queue<int>();
            WorldGrid grid = Find.WorldGrid;
            int count = grid.Surface.TilesCount;
            for (int t = 0; t < count; t++)
            {
                Vector2 p = Project(new PlanetTile(t, grid.Surface));
                int i = Index(p);
                if (known[i]) continue;
                SurfaceTile tile = grid[t];
                known[i] = true;
                land[i] = (byte)(tile.WaterCovered ? 0 : 1);
                hills[i] = (byte)tile.hilliness;
                pixelTile[i] = t;
                queue.Enqueue(i);
            }
            // Tiles are larger than pixels near the poles and smaller elsewhere; fill the gaps from the nearest known pixel.
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % W, y = i / W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ny < 0 || ny >= H) continue;
                    int n = ny * W + (nx + W) % W;
                    if (known[n]) continue;
                    known[n] = true;
                    land[n] = land[i];
                    hills[n] = hills[i];
                    pixelTile[n] = pixelTile[i];
                    queue.Enqueue(n);
                }
            }
        }

        // ------------------------------------------------------------------ influence

        private static int Signature()
        {
            unchecked
            {
                int h = 17;
                // FTV borders move without any settlement changing: look again each day.
                if (FtvTerritory != null) h = h * 31 + GenDate.DaysPassed + 1;
                foreach (Settlement s in Find.WorldObjects.Settlements)
                    if (s.Faction != null && DominionGeo.OnSurface(s.Tile)) h = h * 31 + s.ID * 7 + s.Faction.loadID;
                WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
                if (comp != null)
                    foreach (War w in comp.Wars.wars)
                        if (KnownWar(w)) h = h * 31 + w.attacker.loadID * 1009 + w.defender.loadID;
                return h;
            }
        }

        public static bool KnownWar(War w) =>
            w.attacker != null && w.defender != null && (w.InvolvesPlayer || IntelUtility.IntelOn(w.attacker, w.defender) >= IntelUtility.RestrictedIntel);

        private static Integration_FactionTerritories FtvTerritory
        {
            get
            {
                var ftv = IntegrationRegistry.Get<Integration_FactionTerritories>();
                return ftv != null && ftv.IsActive && ftv.CanReadTerritory ? ftv : null;
            }
        }

        private static void BuildOwners()
        {
            factions.Clear();
            owner = new int[W * H];
            Integration_FactionTerritories ftv = FtvTerritory;
            fromFtv = ftv != null && BuildOwnersFromFtv(ftv);
            if (!fromFtv) BuildInfluence();
        }

        /// <summary>Each land pixel takes the owner of the tile it shows. False if FTV claims no land at all yet.</summary>
        private static bool BuildOwnersFromFtv(Integration_FactionTerritories ftv)
        {
            int count = Find.WorldGrid.Surface.TilesCount;
            var tileOwner = new int[count];
            var byLoadId = new Dictionary<int, int>();
            bool any = false;
            for (int t = 0; t < count; t++)
            {
                tileOwner[t] = -1;
                if (!ftv.TryGetOwner(t, out int id) || id < 0) continue;
                if (!byLoadId.TryGetValue(id, out int fi))
                {
                    Faction f = Integration_FactionTerritories.FactionByLoadId(id);
                    fi = -1;
                    if (f != null && !f.defeated)
                    {
                        fi = factions.Count;
                        factions.Add(f);
                    }
                    byLoadId[id] = fi;
                }
                tileOwner[t] = fi;
                if (fi >= 0) any = true;
            }
            if (!any)
            {
                factions.Clear();
                return false;
            }
            for (int i = 0; i < owner.Length; i++)
            {
                int t = pixelTile[i];
                owner[i] = land[i] == 1 && t >= 0 && t < count ? tileOwner[t] : -1;
            }
            return true;
        }

        /// <summary>No FTV territory: each settlement claims the land round it.</summary>
        private static void BuildInfluence()
        {
            var dist = new float[W * H];
            var src = new Vector2[W * H];
            var reach = new float[W * H];
            for (int i = 0; i < owner.Length; i++)
            {
                owner[i] = -1;
                dist[i] = float.MaxValue;
            }
            var queue = new Queue<int>();
            var index = new Dictionary<Faction, int>();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || !DominionGeo.OnSurface(s.Tile)) continue;
                if (!index.TryGetValue(s.Faction, out int fi))
                {
                    fi = factions.Count;
                    factions.Add(s.Faction);
                    index[s.Faction] = fi;
                }
                Vector2 p = Project(s.Tile);
                int i = Index(p);
                Vector2 px = new Vector2(p.x * W, p.y * H);
                bool capital = DominionUIState.Capital(s.Faction) == s || s.Faction.IsPlayer;
                if (dist[i] <= 0f && !capital) continue;
                dist[i] = 0f;
                owner[i] = fi;
                src[i] = px;
                reach[i] = capital ? CapitalReach : Reach;
                queue.Enqueue(i);
            }
            // Spread outwards over land; each pixel goes to the nearest settlement that can reach it.
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % W, y = i / W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int ny = y + dy;
                        if (ny < 0 || ny >= H) continue;
                        int nx = (x + dx + W) % W;
                        int n = ny * W + nx;
                        if (land[n] == 0) continue;
                        float ddx = Mathf.Abs(nx + 0.5f - src[i].x);
                        if (ddx > W / 2f) ddx = W - ddx;
                        // Degrees of longitude shrink towards the poles.
                        ddx *= Mathf.Cos((ny / (float)H - 0.5f) * Mathf.PI);
                        float ddy = ny + 0.5f - src[i].y;
                        float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                        if (d > reach[i] || d >= dist[n]) continue;
                        dist[n] = d;
                        owner[n] = owner[i];
                        src[n] = src[i];
                        reach[n] = reach[i];
                        queue.Enqueue(n);
                    }
            }
        }

        private static void Paint()
        {
            var atWar = new HashSet<long>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp != null)
                foreach (War w in comp.Wars.wars)
                    if (KnownWar(w))
                    {
                        atWar.Add(Pair(w.attacker, w.defender));
                        atWar.Add(Pair(w.defender, w.attacker));
                    }
            var colors = new Color32[factions.Count];
            for (int f = 0; f < factions.Count; f++)
                colors[f] = factions[f].IsPlayer ? new Color(0.92f, 0.92f, 0.95f) : factions[f].Color;

            var pix = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    Color32 c;
                    if (land[i] == 0)
                    {
                        c = Sea;
                        for (int d = 0; d < 4 && c.r == Sea.r; d++)
                            if (Land_(x + (d == 0 ? 1 : d == 1 ? -1 : 0), y + (d == 2 ? 1 : d == 3 ? -1 : 0))) c = Coast;
                    }
                    else
                    {
                        c = Land;
                        int hill = hills[i];
                        if (hill >= 3) c = Color32.Lerp(Land, new Color32(120, 112, 98, 255), (hill - 2) * 0.22f);
                        int o = owner[i];
                        if (o >= 0)
                        {
                            bool border = false, front = false;
                            for (int d = 0; d < 4; d++)
                            {
                                int nx = (x + (d == 0 ? 1 : d == 1 ? -1 : 0) + W) % W, ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                                if (ny < 0 || ny >= H) continue;
                                int no = owner[ny * W + nx];
                                if (no == o) continue;
                                border = true;
                                if (no >= 0 && atWar.Contains(Pair(factions[o], factions[no]))) front = true;
                            }
                            c = front ? Front : border ? Color32.Lerp(c, Darker(colors[o]), 0.9f) : Color32.Lerp(c, colors[o], 0.42f);
                        }
                    }
                    pix[(H - 1 - y) * W + x] = c; // texture rows run bottom-up
                }
            texture.SetPixels32(pix);
            texture.Apply(false);
        }

        private static bool Land_(int x, int y) => y >= 0 && y < H && land[y * W + (x + W) % W] == 1;

        private static Color32 Darker(Color32 c) => new Color32((byte)(c.r * 0.75f), (byte)(c.g * 0.75f), (byte)(c.b * 0.75f), 255);

        private static long Pair(Faction a, Faction b) => (long)a.loadID << 32 | (uint)b.loadID;
    }
}
