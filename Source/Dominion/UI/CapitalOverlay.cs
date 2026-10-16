using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Marks every capital on the world map: a gold seal-shaped outline around the settlement, an inner ring in the
    /// faction's colour and a small crown above it, at every zoom level. Hovering shows whose capital it is.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CapitalOverlay
    {
        private static readonly Texture2D Ring = ContentFinder<Texture2D>.Get("UI/Overlays/Dominion_CapitalRing");
        private static readonly Texture2D Crown = ContentFinder<Texture2D>.Get("UI/Overlays/Dominion_CapitalCrown");
        private static readonly Color Gold = new Color(1f, 0.82f, 0.3f, 0.95f);

        private static readonly List<(Settlement settlement, Faction faction)> capitals = new List<(Settlement, Faction)>();
        private static int builtFrame = -1;

        /// <summary>The faction whose capital this settlement is, or null.</summary>
        public static Faction CapitalOf(Settlement s)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null || s == null || s.Faction == null) return null;
            if (s.Faction.IsPlayer) return comp.Player.capital == s ? s.Faction : null;
            return comp.TryGetState(s.Faction, out FactionState st) && st.capital == s ? s.Faction : null;
        }

        private static void Rebuild()
        {
            capitals.Clear();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            foreach (FactionState st in comp.AllStates)
                if (st.capital != null && !st.capital.Destroyed && st.capital.Faction == st.faction && DominionScheduler.IsSimulated(st.faction))
                    capitals.Add((st.capital, st.faction));
            Settlement mine = comp.Player.capital;
            if (mine != null && !mine.Destroyed && mine.Faction != null && mine.Faction.IsPlayer)
                capitals.Add((mine, mine.Faction));
        }

        public static void OnGUI()
        {
            if (!DominionMod.Settings.showCapitals) return;
            if (Time.frameCount - builtFrame > 60)
            {
                builtFrame = Time.frameCount;
                Rebuild();
            }
            float tile = GenWorldUI.CurUITileSize();
            for (int i = 0; i < capitals.Count; i++)
            {
                (Settlement s, Faction f) = capitals[i];
                if (s.Destroyed || s.Faction != f) continue;
                Vector3 pos = s.DrawPos;
                if (WorldRendererUtility.HiddenBehindTerrainNow(pos) || ExpandableWorldObjectsUtility.HiddenByRules(s)) continue;
                Vector2 c = GenWorldUI.WorldToUIPosition(pos);
                float icon = ExpandableWorldObjectsUtility.IsExpanded(s) ? ExpandableWorldObjectsUtility.ExpandedIconScreenRect(s, 1f).width : tile;
                float size = Mathf.Clamp(Mathf.Max(icon, tile) * 1.6f, 22f, 160f);
                Rect ring = new Rect(c.x - size / 2f, c.y - size / 2f, size, size);

                GUI.color = Gold;
                GUI.DrawTexture(ring, Ring);
                Color fc = f.Color;
                fc.a = 0.9f;
                GUI.color = fc;
                GUI.DrawTexture(ring.ContractedBy(size * 0.13f), Ring);
                GUI.color = Gold;
                float crown = Mathf.Max(12f, size * 0.32f);
                GUI.DrawTexture(new Rect(c.x - crown / 2f, ring.y - crown * 0.7f, crown, crown), Crown);
                GUI.color = Color.white;

                if (Mouse.IsOver(ring))
                    TooltipHandler.TipRegion(ring, "Dominion_Capital_Tooltip".Translate(s.Label, f.Name));
            }
        }
    }

    [HarmonyPatch(typeof(ExpandableWorldObjectsUtility), nameof(ExpandableWorldObjectsUtility.ExpandableWorldObjectsOnGUI))]
    public static class Patch_ExpandableWorldObjectsOnGUI_Capitals
    {
        public static void Postfix()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            try { CapitalOverlay.OnGUI(); }
            catch (System.Exception e) { DLog.WarningOnce("Drawing capitals failed: " + e.Message, 0x7CA1); }
        }
    }

    /// <summary>"Capital of ..." at the top of a capital's inspect pane.</summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetInspectString))]
    public static class Patch_Settlement_GetInspectString_Capital
    {
        public static void Postfix(Settlement __instance, ref string __result)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            Faction f = CapitalOverlay.CapitalOf(__instance);
            if (f == null) return;
            var sb = new StringBuilder("Dominion_Capital_Inspect".Translate(f.Name).ToString());
            if (!__result.NullOrEmpty()) sb.AppendLine().Append(__result);
            __result = sb.ToString();
        }
    }
}
