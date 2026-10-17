using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum DominionTab { News, Overview, Diplomacy, Treaties, War, Council, Intelligence, Federation, Events, Integrations }

    /// <summary>
    /// What the Dominion window remembers between openings, and how one part of it sends you to another: the faction
    /// selected in one tab is selected in every tab, and "jump" buttons open a tab (opening the window if needed).
    /// </summary>
    public static class DominionUIState
    {
        /// <summary>The faction selected anywhere in the window. Shared by Overview, Diplomacy, Intelligence and War.</summary>
        public static Faction selected;

        /// <summary>Last tab shown; restored when the window opens again.</summary>
        public static DominionTab tab = DominionTab.News;

        /// <summary>A tab change requested from outside the window (a letter, a jump button); applied on the next frame.</summary>
        public static DominionTab? pendingTab;

        public static NewsItem pendingNews;

        /// <summary>The window's size after the player resized it; null until they do.</summary>
        public static Vector2? windowSize;

        private static MainButtonDef button;

        private static MainButtonDef Button => button ??= DefDatabase<MainButtonDef>.GetNamedSilentFail("Dominion");

        /// <summary>The selected faction if it is still in <paramref name="list"/>, else the first one (and selects it).</summary>
        public static Faction SelectedIn(List<Faction> list)
        {
            if (selected != null && list.Contains(selected)) return selected;
            selected = list.Count > 0 ? list[0] : null;
            return selected;
        }

        /// <summary>Opens the Dominion window on <paramref name="target"/>, selecting <paramref name="f"/> if given.</summary>
        public static void Show(DominionTab target, Faction f = null)
        {
            if (f != null) selected = f;
            pendingTab = target;
            if (Button != null && Find.MainTabsRoot.OpenTab != Button) Find.MainTabsRoot.SetCurrentTab(Button, true);
        }

        public static void ShowNews(NewsItem n)
        {
            pendingNews = n;
            Show(DominionTab.News);
        }

        /// <summary>Jumps the camera to a faction's capital (or any settlement of it) on the world map.</summary>
        public static void JumpToCapital(Faction f)
        {
            Settlement s = Capital(f);
            if (s != null) CameraJumper.TryJumpAndSelect(s);
            else Messages.Message("Dominion_UI_NoSettlement".Translate(f.Name), MessageTypeDefOf.RejectInput, historical: false);
        }

        public static Settlement Capital(Faction f)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (f == null || comp == null) return null;
            if (f.IsPlayer) return comp.Player.capital != null && !comp.Player.capital.Destroyed ? comp.Player.capital : null;
            if (comp.TryGetState(f, out FactionState s) && s.capital != null && !s.capital.Destroyed) return s.capital;
            List<Settlement> all = comp.Cache.SettlementsOf(f);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// The small row of jump buttons shown next to a faction: its diplomacy, war and intelligence pages and its
        /// capital on the world map. <paramref name="except"/> hides the button for the tab already shown.
        /// </summary>
        public static void JumpButtons(Rect rect, Faction f, DominionTab except, bool onPaper = false)
        {
            var buttons = new List<(string label, string tip, System.Action act)>();
            if (except != DominionTab.Overview)
                buttons.Add(("Dominion_UI_Jump_Dossier".Translate(), "Dominion_UI_Jump_Dossier_Desc".Translate(f.Name), () => Show(DominionTab.Overview, f)));
            if (except != DominionTab.Diplomacy)
                buttons.Add(("Dominion_UI_Jump_Diplomacy".Translate(), "Dominion_UI_Jump_Diplomacy_Desc".Translate(f.Name), () => Show(DominionTab.Diplomacy, f)));
            if (except != DominionTab.War)
                buttons.Add(("Dominion_UI_Jump_War".Translate(), "Dominion_UI_Jump_War_Desc".Translate(f.Name), () => Show(DominionTab.War, f)));
            if (except != DominionTab.Intelligence)
                buttons.Add(("Dominion_UI_Jump_Intel".Translate(), "Dominion_UI_Jump_Intel_Desc".Translate(f.Name), () => Show(DominionTab.Intelligence, f)));
            buttons.Add(("Dominion_UI_Jump_Map".Translate(), "Dominion_UI_Jump_Map_Desc".Translate(f.Name), () => JumpToCapital(f)));
            float w = rect.width / buttons.Count;
            for (int i = 0; i < buttons.Count; i++)
            {
                Rect r = new Rect(rect.x + i * w + 1f, rect.y, w - 2f, rect.height);
                bool clicked = onPaper ? DominionPaper.InkButton(r, buttons[i].label, buttons[i].tip) : ButtonWithTip(r, buttons[i].label, buttons[i].tip);
                if (clicked) buttons[i].act();
            }
        }

        public static bool ButtonWithTip(Rect r, string label, string tip, bool active = true)
        {
            TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonText(r, label.Truncate(r.width - 4f), active: active) && active;
        }
    }
}
