using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>TV watchers get a mood memory from whatever your Broadcast Center has on air. Checked once an hour per watcher.</summary>
    [HarmonyPatch(typeof(JobDriver_WatchTelevision), "WatchTickAction")]
    public static class Patch_WatchTelevision
    {
        public static void Postfix(JobDriver_WatchTelevision __instance, int delta)
        {
            Pawn pawn = __instance.pawn;
            if (pawn?.Faction == null || !pawn.Faction.IsPlayer) return;
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % GenDate.TicksPerHour >= delta) return;
            BroadcastProgramDef onAir = WorldComponent_Dominion.Instance?.Broadcast.OnAirNow;
            if (onAir?.tvThought != null)
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(onAir.tvThought, null, null);
        }
    }

    /// <summary>
    /// Vanilla Books Expanded newspapers print Dominion's latest headlines. Only applies to things whose class is
    /// VBE's Newspaper, so it does nothing without that mod.
    /// </summary>
    [HarmonyPatch(typeof(Book), nameof(Book.GenerateBook))]
    public static class Patch_Book_GenerateBook_Newspaper
    {
        private static readonly AccessTools.FieldRef<Book, string> TitleRef = SafeRef("title");
        private static readonly AccessTools.FieldRef<Book, string> FlavorRef = SafeRef("descriptionFlavor");

        private static AccessTools.FieldRef<Book, string> SafeRef(string field)
        {
            try { return AccessTools.FieldRefAccess<Book, string>(field); }
            catch { return null; }
        }

        public static void Postfix(Book __instance)
        {
            if (__instance.GetType().Name != "Newspaper") return;
            if (!IntegrationRegistry.IsActive<Integration_VanillaBooks>()) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            IReadOnlyList<NewsItem> news = comp?.News.Delivered;
            if (news == null || news.Count == 0) return;

            var sb = new StringBuilder();
            int shown = 0;
            for (int i = news.Count - 1; i >= 0 && shown < 3; i--, shown++)
                sb.AppendLine("• " + news[i].headline);
            BroadcastProgramDef onAir = comp.Broadcast.OnAirNow;
            if (onAir != null)
                sb.AppendLine().Append("Dominion_Newspaper_Slant".Translate(onAir.label));

            try
            {
                if (TitleRef != null) TitleRef(__instance) = "Dominion_Newspaper_Title".Translate(news[news.Count - 1].headline);
                if (FlavorRef != null) FlavorRef(__instance) = sb.ToString().TrimEnd();
            }
            catch (System.Exception e)
            {
                DLog.WarningOnce("Could not print headlines in a newspaper: " + e.Message, 0x7EB5);
            }
        }
    }
}
