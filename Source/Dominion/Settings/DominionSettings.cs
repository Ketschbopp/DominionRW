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
        public bool showCapitals = true;

        // Simulation
        public SimulationDetail simulationDetail = SimulationDetail.Normal;
        public int eventLogCap = 500;
        public bool aiDiplomacyEnabled = true;
        public float aiAggression = 1f;
        public bool politicalEventsEnabled = true;
        public float stabilityDriftSpeed = 1f;
        public bool emigration = true;
        public bool embargoDrain = true;

        // Intelligence
        public bool aiIntelligence = true;
        public float operationSuccess = 1f;
        public bool aiCovertOps = true;
        public float physicalDetection = 1f;

        // War
        public bool warbands = true;
        public float warbandFrequency = 1f;
        public float warbandSpeed = 2f;
        public float warbandFightSize = 1f;
        public float warbandFightVariance = 0.1f;
        public bool warbandRaidsOnPlayer = true;
        public bool warOccupation = true;
        public float capitulationScore = 75f;
        public bool aiJointWars = true;
        public bool coldWars = true;

        // News
        public int newsLogCap = 300;
        public bool falseRumors = true;
        public bool instantNews;
        public bool visitorShuttles = true;
        public bool colonistNewsThoughts = true;
        public bool letterForMajorNews = true;
        public LetterMode letterMode = LetterMode.Important;
        public Dictionary<EventCategory, NewsNotifyMode> notifyModes = new Dictionary<EventCategory, NewsNotifyMode>();

        // Rim Council
        public bool councilEnabled = true;
        public int councilIntervalDays = 60;
        public TechLevel councilMinTechLevel = TechLevel.Medieval;

        // Leadership
        public HeadOfStateMode headOfStateMode = HeadOfStateMode.IdeoLeader;
        public DiplomatMode diplomatMode = DiplomatMode.IdeoRole;

        // Culture
        public bool allowIdeoReforms = true;
        public int ideoReformMinDays = 120;

        // Ending and world events
        public bool endingEnabled = true;
        public float endingShare = 1f;
        public bool endingIncludeOutlaws;
        public bool worldEvents = true;
        public float worldEventFrequency = 1f;
        public bool plagueSpread = true;

        // Performance
        public bool profilingEnabled;

        // Optional integrations the player switched off, by package id. Absent means enabled.
        public HashSet<string> disabledIntegrations = new HashSet<string>();

        /// <summary>How many days the AI waits between its own diplomatic moves, scaled by simulation detail.</summary>
        public float AiCooldownFactor => simulationDetail == SimulationDetail.Low ? 2f : simulationDetail == SimulationDetail.High ? 0.75f : 1f;

        public NewsNotifyMode NotifyModeFor(EventCategory c) =>
            notifyModes.TryGetValue(c, out NewsNotifyMode m) ? m : NewsNotifyMode.Message;

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
            Scribe_Values.Look(ref showCapitals, "showCapitals", true);
            Scribe_Values.Look(ref simulationDetail, "simulationDetail", SimulationDetail.Normal);
            Scribe_Values.Look(ref eventLogCap, "eventLogCap", 500);
            Scribe_Values.Look(ref aiDiplomacyEnabled, "aiDiplomacyEnabled", true);
            Scribe_Values.Look(ref aiAggression, "aiAggression", 1f);
            Scribe_Values.Look(ref politicalEventsEnabled, "politicalEventsEnabled", true);
            Scribe_Values.Look(ref emigration, "emigration", true);
            Scribe_Values.Look(ref embargoDrain, "embargoDrain", true);
            Scribe_Values.Look(ref stabilityDriftSpeed, "stabilityDriftSpeed", 1f);
            Scribe_Values.Look(ref aiIntelligence, "aiIntelligence", true);
            Scribe_Values.Look(ref operationSuccess, "operationSuccess", 1f);
            Scribe_Values.Look(ref aiCovertOps, "aiCovertOps", true);
            Scribe_Values.Look(ref physicalDetection, "physicalDetection", 1f);
            Scribe_Values.Look(ref warbands, "warbands", true);
            Scribe_Values.Look(ref warbandFrequency, "warbandFrequency", 1f);
            Scribe_Values.Look(ref warbandSpeed, "warbandSpeed", 2f);
            Scribe_Values.Look(ref warbandFightSize, "warbandFightSize", 1f);
            Scribe_Values.Look(ref warbandFightVariance, "warbandFightVariance", 0.1f);
            Scribe_Values.Look(ref warbandRaidsOnPlayer, "warbandRaidsOnPlayer", true);
            Scribe_Values.Look(ref warOccupation, "warOccupation", true);
            Scribe_Values.Look(ref capitulationScore, "capitulationScore", 75f);
            Scribe_Values.Look(ref aiJointWars, "aiJointWars", true);
            Scribe_Values.Look(ref coldWars, "coldWars", true);
            Scribe_Values.Look(ref newsLogCap, "newsLogCap", 300);
            Scribe_Values.Look(ref falseRumors, "falseRumors", true);
            Scribe_Values.Look(ref instantNews, "instantNews", false);
            Scribe_Values.Look(ref visitorShuttles, "visitorShuttles", true);
            Scribe_Values.Look(ref colonistNewsThoughts, "colonistNewsThoughts", true);
            Scribe_Values.Look(ref letterForMajorNews, "letterForMajorNews", true);
            Scribe_Values.Look(ref letterMode, "letterMode", LetterMode.Important);
            Scribe_Collections.Look(ref notifyModes, "notifyModes", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref councilEnabled, "councilEnabled", true);
            Scribe_Values.Look(ref councilIntervalDays, "councilIntervalDays", 60);
            Scribe_Values.Look(ref councilMinTechLevel, "councilMinTechLevel", TechLevel.Medieval);
            Scribe_Values.Look(ref headOfStateMode, "headOfStateMode", HeadOfStateMode.IdeoLeader);
            Scribe_Values.Look(ref diplomatMode, "diplomatMode", DiplomatMode.IdeoRole);
            Scribe_Values.Look(ref allowIdeoReforms, "allowIdeoReforms", true);
            Scribe_Values.Look(ref ideoReformMinDays, "ideoReformMinDays", 120);
            Scribe_Values.Look(ref endingEnabled, "endingEnabled", true);
            Scribe_Values.Look(ref endingShare, "endingShare", 1f);
            Scribe_Values.Look(ref endingIncludeOutlaws, "endingIncludeOutlaws");
            Scribe_Values.Look(ref worldEvents, "worldEvents", true);
            Scribe_Values.Look(ref worldEventFrequency, "worldEventFrequency", 1f);
            Scribe_Values.Look(ref plagueSpread, "plagueSpread", true);
            Scribe_Values.Look(ref profilingEnabled, "profilingEnabled");
            Scribe_Collections.Look(ref disabledIntegrations, "disabledIntegrations", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && disabledIntegrations == null)
                disabledIntegrations = new HashSet<string>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && notifyModes == null)
                notifyModes = new Dictionary<EventCategory, NewsNotifyMode>();
        }

        public void ResetToDefaults()
        {
            debugLogging = false;
            showCapitals = true;
            simulationDetail = SimulationDetail.Normal;
            eventLogCap = 500;
            aiDiplomacyEnabled = true;
            aiAggression = 1f;
            politicalEventsEnabled = true;
            emigration = true;
            embargoDrain = true;
            stabilityDriftSpeed = 1f;
            aiIntelligence = true;
            operationSuccess = 1f;
            aiCovertOps = true;
            physicalDetection = 1f;
            warbands = true;
            warbandFrequency = 1f;
            warbandSpeed = 2f;
            warbandFightSize = 1f;
            warbandFightVariance = 0.1f;
            warbandRaidsOnPlayer = true;
            warOccupation = true;
            capitulationScore = 75f;
            aiJointWars = true;
            coldWars = true;
            newsLogCap = 300;
            falseRumors = true;
            instantNews = false;
            visitorShuttles = true;
            colonistNewsThoughts = true;
            letterForMajorNews = true;
            letterMode = LetterMode.Important;
            notifyModes.Clear();
            councilEnabled = true;
            councilIntervalDays = 60;
            councilMinTechLevel = TechLevel.Medieval;
            headOfStateMode = HeadOfStateMode.IdeoLeader;
            diplomatMode = DiplomatMode.IdeoRole;
            allowIdeoReforms = true;
            ideoReformMinDays = 120;
            endingEnabled = true;
            endingShare = 1f;
            endingIncludeOutlaws = false;
            worldEvents = true;
            worldEventFrequency = 1f;
            plagueSpread = true;
            profilingEnabled = false;
            disabledIntegrations.Clear();
        }
    }
}
