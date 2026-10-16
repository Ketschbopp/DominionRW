using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Tabbed mod settings. New modules add a tab here.</summary>
    public static class SettingsWindow
    {
        private enum Tab { General, Simulation, News, Espionage, War, Council, Leadership, Culture, Events, Ending, Integrations, Performance }

        private static Tab current = Tab.General;
        private static Vector2 scroll;
        private static float lastContentHeight = 600f;

        public static void Draw(Rect inRect, DominionSettings s)
        {
            var tabs = new List<TabRecord>();
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                Tab captured = t;
                tabs.Add(new TabRecord(("Dominion_SettingsTab_" + t).Translate(), () =>
                {
                    current = captured;
                    scroll = Vector2.zero;
                }, current == t));
            }

            Rect body = inRect;
            body.yMin += TabDrawer.TabHeight + 4f;
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);

            Rect inner = body.ContractedBy(12f);
            Rect view = new Rect(0f, 0f, inner.width - 16f, lastContentHeight);
            Widgets.BeginScrollView(inner, ref scroll, view);
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(view);

            switch (current)
            {
                case Tab.General: DrawGeneral(l, s); break;
                case Tab.Simulation: DrawSimulation(l, s); break;
                case Tab.News: DrawNews(l, s); break;
                case Tab.Espionage: DrawEspionage(l, s); break;
                case Tab.War: DrawWar(l, s); break;
                case Tab.Council: DrawCouncil(l, s); break;
                case Tab.Leadership: DrawLeadership(l, s); break;
                case Tab.Culture: DrawCulture(l, s); break;
                case Tab.Events: DrawEvents(l, s); break;
                case Tab.Ending: DrawEnding(l, s); break;
                case Tab.Integrations: DrawIntegrations(l, s); break;
                case Tab.Performance: DrawPerformance(l, s); break;
            }

            lastContentHeight = Mathf.Max(l.CurHeight + 12f, inner.height);
            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawGeneral(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_DebugLogging".Translate(), ref s.debugLogging, "Dominion_Setting_DebugLogging_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_ShowCapitals".Translate(), ref s.showCapitals, "Dominion_Setting_ShowCapitals_Desc".Translate());
            l.Gap();
            if (l.ButtonText("Dominion_Setting_ResetDefaults".Translate()))
                s.ResetToDefaults();
        }

        private static void DrawSimulation(Listing_Standard l, DominionSettings s)
        {
            l.Label("Dominion_Setting_SimulationDetail".Translate(), tooltip: "Dominion_Setting_SimulationDetail_Desc".Translate());
            foreach (SimulationDetail d in Enum.GetValues(typeof(SimulationDetail)))
                if (l.RadioButton(("Dominion_SimulationDetail_" + d).Translate(), s.simulationDetail == d))
                    s.simulationDetail = d;
            l.Gap();
            l.Label("Dominion_Setting_EventLogCap".Translate(s.eventLogCap));
            s.eventLogCap = (int)l.Slider(s.eventLogCap, 50, 5000);
            l.GapLine();
            l.CheckboxLabeled("Dominion_Setting_AiDiplomacy".Translate(), ref s.aiDiplomacyEnabled, "Dominion_Setting_AiDiplomacy_Desc".Translate());
            l.Label("Dominion_Setting_AiAggression".Translate(s.aiAggression.ToStringPercent()));
            s.aiAggression = l.Slider(s.aiAggression, 0f, 3f);
            l.Gap();
            l.CheckboxLabeled("Dominion_Setting_PoliticalEvents".Translate(), ref s.politicalEventsEnabled, "Dominion_Setting_PoliticalEvents_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_Emigration".Translate(), ref s.emigration, "Dominion_Setting_Emigration_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_EmbargoDrain".Translate(), ref s.embargoDrain, "Dominion_Setting_EmbargoDrain_Desc".Translate());
            l.Label("Dominion_Setting_StabilityDrift".Translate(s.stabilityDriftSpeed.ToStringPercent()));
            s.stabilityDriftSpeed = l.Slider(s.stabilityDriftSpeed, 0.1f, 3f);
        }

        private static void DrawEspionage(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_AiIntelligence".Translate(), ref s.aiIntelligence, "Dominion_Setting_AiIntelligence_Desc".Translate());
            if (s.aiIntelligence)
                l.CheckboxLabeled("Dominion_Setting_AiCovertOps".Translate(), ref s.aiCovertOps, "Dominion_Setting_AiCovertOps_Desc".Translate());
            l.Label("Dominion_Setting_OperationSuccess".Translate(s.operationSuccess.ToStringPercent()));
            s.operationSuccess = l.Slider(s.operationSuccess, 0.25f, 2f);
            l.Label("Dominion_Setting_PhysicalDetection".Translate(s.physicalDetection.ToStringPercent()), tooltip: "Dominion_Setting_PhysicalDetection_Desc".Translate());
            s.physicalDetection = l.Slider(s.physicalDetection, 0f, 3f);
        }

        private static void DrawWar(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_Warbands".Translate(), ref s.warbands, "Dominion_Setting_Warbands_Desc".Translate());
            if (s.warbands)
            {
                l.Label("Dominion_Setting_WarbandFrequency".Translate(s.warbandFrequency.ToStringPercent()));
                s.warbandFrequency = l.Slider(s.warbandFrequency, 0.1f, 3f);
                l.CheckboxLabeled("Dominion_Setting_WarbandRaids".Translate(), ref s.warbandRaidsOnPlayer, "Dominion_Setting_WarbandRaids_Desc".Translate());
                l.CheckboxLabeled("Dominion_Setting_Occupation".Translate(), ref s.warOccupation, "Dominion_Setting_Occupation_Desc".Translate());
            }
            l.Gap();
            l.Label("Dominion_Setting_CapitulationScore".Translate(s.capitulationScore.ToString("F0")), tooltip: "Dominion_Setting_CapitulationScore_Desc".Translate());
            s.capitulationScore = Mathf.Round(l.Slider(s.capitulationScore, 40f, 100f) / 5f) * 5f;
            l.CheckboxLabeled("Dominion_Setting_JointWars".Translate(), ref s.aiJointWars, "Dominion_Setting_JointWars_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_ColdWars".Translate(), ref s.coldWars, "Dominion_Setting_ColdWars_Desc".Translate());
        }

        private static void DrawNews(Listing_Standard l, DominionSettings s)
        {
            l.Label("Dominion_Setting_NewsLogCap".Translate(s.newsLogCap));
            s.newsLogCap = (int)l.Slider(s.newsLogCap, 50, 2000);
            l.CheckboxLabeled("Dominion_Setting_FalseRumors".Translate(), ref s.falseRumors, "Dominion_Setting_FalseRumors_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_InstantNews".Translate(), ref s.instantNews, "Dominion_Setting_InstantNews_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_NewsThoughts".Translate(), ref s.colonistNewsThoughts, "Dominion_Setting_NewsThoughts_Desc".Translate());
            l.CheckboxLabeled("Dominion_Setting_LetterForMajor".Translate(), ref s.letterForMajorNews, "Dominion_Setting_LetterForMajor_Desc".Translate());
            l.GapLine();
            l.Label("Dominion_Setting_NotifyHeader".Translate());
            foreach (EventCategory c in Enum.GetValues(typeof(EventCategory)))
            {
                Rect row = l.GetRect(28f);
                Widgets.Label(row.LeftPart(0.5f), ("Dominion_EventCategory_" + c).Translate());
                NewsNotifyMode mode = s.NotifyModeFor(c);
                if (Widgets.ButtonText(row.RightPart(0.45f), ("Dominion_NotifyMode_" + mode).Translate()))
                {
                    EventCategory captured = c;
                    var options = new List<FloatMenuOption>();
                    foreach (NewsNotifyMode m in Enum.GetValues(typeof(NewsNotifyMode)))
                    {
                        NewsNotifyMode cm = m;
                        options.Add(new FloatMenuOption(("Dominion_NotifyMode_" + m).Translate(), () => s.notifyModes[captured] = cm));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
        }

        private static void DrawCouncil(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_CouncilEnabled".Translate(), ref s.councilEnabled);
            l.Label("Dominion_Setting_CouncilInterval".Translate(s.councilIntervalDays));
            s.councilIntervalDays = (int)l.Slider(s.councilIntervalDays, 15, 240);
            l.Gap();
            l.Label("Dominion_Setting_CouncilMinTech".Translate(s.councilMinTechLevel.ToStringHuman()),
                tooltip: "Dominion_Setting_CouncilMinTech_Desc".Translate());
            if (l.ButtonText(s.councilMinTechLevel.ToStringHuman().CapitalizeFirst()))
            {
                var options = new List<FloatMenuOption>();
                foreach (TechLevel t in Enum.GetValues(typeof(TechLevel)))
                {
                    if (t == TechLevel.Undefined) continue;
                    TechLevel captured = t;
                    options.Add(new FloatMenuOption(t.ToStringHuman().CapitalizeFirst(), () => s.councilMinTechLevel = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            l.Gap();
            l.Label("Dominion_Setting_CouncilNoVeto".Translate());
        }

        private static void DrawLeadership(Listing_Standard l, DominionSettings s)
        {
            l.Label("Dominion_Setting_HeadOfState".Translate());
            foreach (HeadOfStateMode m in Enum.GetValues(typeof(HeadOfStateMode)))
                if (l.RadioButton(("Dominion_HeadOfStateMode_" + m).Translate(), s.headOfStateMode == m))
                    s.headOfStateMode = m;
            l.Gap();
            l.Label("Dominion_Setting_Diplomats".Translate());
            foreach (DiplomatMode m in Enum.GetValues(typeof(DiplomatMode)))
                if (l.RadioButton(("Dominion_DiplomatMode_" + m).Translate(), s.diplomatMode == m))
                    s.diplomatMode = m;
            if (!ModsConfig.IdeologyActive)
            {
                l.Gap(4f);
                l.Label("Dominion_Setting_NoIdeologyFallback".Translate());
            }
        }

        private static void DrawCulture(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_AllowIdeoReforms".Translate(), ref s.allowIdeoReforms, "Dominion_Setting_AllowIdeoReforms_Desc".Translate());
            // Precept reforms themselves are not implemented yet (see the design doc); no interval to set.
        }

        private static void DrawEvents(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_WorldEvents".Translate(), ref s.worldEvents, "Dominion_Setting_WorldEvents_Desc".Translate());
            if (s.worldEvents)
            {
                l.Label("Dominion_Setting_WorldEventFrequency".Translate(s.worldEventFrequency.ToStringPercent()));
                s.worldEventFrequency = l.Slider(s.worldEventFrequency, 0.1f, 3f);
                l.CheckboxLabeled("Dominion_Setting_PlagueSpread".Translate(), ref s.plagueSpread, "Dominion_Setting_PlagueSpread_Desc".Translate());
            }
        }

        private static void DrawEnding(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_EndingEnabled".Translate(), ref s.endingEnabled, "Dominion_Setting_EndingEnabled_Desc".Translate());
            if (!s.endingEnabled) return;
            l.Label("Dominion_Setting_EndingShare".Translate(s.endingShare.ToStringPercent()));
            s.endingShare = Mathf.Round(l.Slider(s.endingShare, 0.5f, 1f) * 20f) / 20f;
            l.CheckboxLabeled("Dominion_Setting_EndingOutlaws".Translate(), ref s.endingIncludeOutlaws, "Dominion_Setting_EndingOutlaws_Desc".Translate());
        }

        private static void DrawIntegrations(Listing_Standard l, DominionSettings s)
        {
            l.Label("Dominion_Setting_IntegrationsHeader".Translate());
            l.GapLine();
            foreach (DominionIntegration integration in IntegrationRegistry.All)
            {
                string status = ("Dominion_IntegrationStatus_" + integration.Status).Translate();
                string label = integration.Label + "  (" + status + ")";
                if (integration.Required)
                {
                    l.Label(label + "  " + "Dominion_Integration_Required".Translate());
                    continue;
                }
                bool enabled = s.IntegrationEnabled(integration.PackageId);
                bool before = enabled;
                l.CheckboxLabeled(label, ref enabled, integration.PackageId);
                if (enabled != before) s.SetIntegrationEnabled(integration.PackageId, enabled);
            }
            l.Gap();
            l.Label("Dominion_Setting_IntegrationsRestart".Translate());
        }

        private static void DrawPerformance(Listing_Standard l, DominionSettings s)
        {
            l.CheckboxLabeled("Dominion_Setting_Profiling".Translate(), ref s.profilingEnabled, "Dominion_Setting_Profiling_Desc".Translate());
        }
    }
}
