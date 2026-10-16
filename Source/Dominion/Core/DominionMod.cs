using HarmonyLib;
using UnityEngine;
using Verse;

namespace Dominion
{
    public class DominionMod : Mod
    {
        public const string PackageId = "ketschbopp.dominion";

        public static DominionMod Instance { get; private set; }
        public static DominionSettings Settings { get; private set; }
        public static Harmony Harmony { get; private set; }

        public DominionMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<DominionSettings>();
            Harmony = new Harmony(PackageId);
            Harmony.PatchAll(typeof(DominionMod).Assembly);
            DLog.Message("Loaded, Harmony patches applied.");
        }

        public override string SettingsCategory() => "Dominion_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect) => SettingsWindow.Draw(inRect, Settings);
    }

    /// <summary>Runs once all defs and every mod's assemblies are loaded.</summary>
    [StaticConstructorOnStartup]
    public static class DominionStartup
    {
        static DominionStartup()
        {
            SimSystemRegistry.Initialize();
            IntegrationRegistry.Initialize();
        }
    }
}
