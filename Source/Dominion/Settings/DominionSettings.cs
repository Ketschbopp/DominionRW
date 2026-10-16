using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    public enum SimulationDetail { Low, Normal, High }

    /// <summary>Who counts as the player's head of state.</summary>
    public enum HeadOfStateMode { IdeoLeader, ChosenAtDesk }

    /// <summary>Which pawns can act as the player's diplomats.</summary>
    public enum DiplomatMode { IdeoRole, ChosenAtDesk }

    public class DominionSettings : ModSettings
    {
        // General
        public bool debugLogging;

        // Simulation
        public SimulationDetail simulationDetail = SimulationDetail.Normal;
        public int eventLogCap = 500;

        // Rim Council
        public int councilIntervalDays = 60;
        public TechLevel councilMinTechLevel = TechLevel.Medieval;

        // Leadership
        public HeadOfStateMode headOfStateMode = HeadOfStateMode.IdeoLeader;
        public DiplomatMode diplomatMode = DiplomatMode.IdeoRole;

        // Culture
        public bool allowIdeoReforms = true;
        public int ideoReformMinDays = 120;

        // Performance
        public bool profilingEnabled;

        // Optional integrations the player switched off, by package id. Absent means enabled.
        public HashSet<string> disabledIntegrations = new HashSet<string>();

        public bool IntegrationEnabled(string packageId) => !disabledIntegrations.Contains(packageId);

        public void SetIntegrationEnabled(string packageId, bool enabled)
        {
            if (enabled) disabledIntegrations.Remove(packageId);
            else disabledIntegrations.Add(packageId);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugLogging, "debugLogging");
            Scribe_Values.Look(ref simulationDetail, "simulationDetail", SimulationDetail.Normal);
            Scribe_Values.Look(ref eventLogCap, "eventLogCap", 500);
            Scribe_Values.Look(ref councilIntervalDays, "councilIntervalDays", 60);
            Scribe_Values.Look(ref councilMinTechLevel, "councilMinTechLevel", TechLevel.Medieval);
            Scribe_Values.Look(ref headOfStateMode, "headOfStateMode", HeadOfStateMode.IdeoLeader);
            Scribe_Values.Look(ref diplomatMode, "diplomatMode", DiplomatMode.IdeoRole);
            Scribe_Values.Look(ref allowIdeoReforms, "allowIdeoReforms", true);
            Scribe_Values.Look(ref ideoReformMinDays, "ideoReformMinDays", 120);
            Scribe_Values.Look(ref profilingEnabled, "profilingEnabled");
            Scribe_Collections.Look(ref disabledIntegrations, "disabledIntegrations", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disabledIntegrations == null)
                disabledIntegrations = new HashSet<string>();
        }

        public void ResetToDefaults()
        {
            debugLogging = false;
            simulationDetail = SimulationDetail.Normal;
            eventLogCap = 500;
            councilIntervalDays = 60;
            councilMinTechLevel = TechLevel.Medieval;
            headOfStateMode = HeadOfStateMode.IdeoLeader;
            diplomatMode = DiplomatMode.IdeoRole;
            allowIdeoReforms = true;
            ideoReformMinDays = 120;
            profilingEnabled = false;
            disabledIntegrations.Clear();
        }
    }
}
