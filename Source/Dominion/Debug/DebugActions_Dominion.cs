using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>Dev menu, category "Dominion". Each later module adds its own actions here.</summary>
    public static class DebugActions_Dominion
    {
        private const string Category = "Dominion";

        [DebugAction(Category, "Raise event...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> RaiseEvent()
        {
            var nodes = new List<DebugActionNode>();
            foreach (DominionEventDef def in DefDatabase<DominionEventDef>.AllDefsListForReading.OrderBy(d => d.defName))
            {
                if (def.debugHidden) continue;
                DominionEventDef capturedDef = def;
                var defNode = new DebugActionNode(def.defName);
                foreach (Faction primary in DebugFactions())
                {
                    Faction capturedPrimary = primary;
                    var primaryNode = new DebugActionNode(primary.Name);
                    primaryNode.AddChild(new DebugActionNode("(no second faction)", DebugActionType.Action,
                        () => EventBus.Raise(capturedDef, capturedPrimary)));
                    foreach (Faction secondary in DebugFactions())
                    {
                        if (secondary == primary) continue;
                        Faction capturedSecondary = secondary;
                        primaryNode.AddChild(new DebugActionNode(secondary.Name, DebugActionType.Action,
                            () => EventBus.Raise(capturedDef, capturedPrimary, capturedSecondary)));
                    }
                    defNode.AddChild(primaryNode);
                }
                nodes.Add(defNode);
            }
            return nodes;
        }

        [DebugAction(Category, "Run daily simulation now", allowedGameStates = AllowedGameStates.Playing)]
        private static void RunDailyNow()
        {
            WorldComponent_Dominion.Instance?.Scheduler.RunFullDayNow();
            Messages.Message("Dominion: ran one full simulated day.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction(Category, "Log scheduler state", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogScheduler()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            comp.Scheduler.Rebuild();
            DLog.Message(comp.Scheduler.Describe());
        }

        [DebugAction(Category, "Log event log", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogEvents()
        {
            IReadOnlyList<DominionEvent> events = WorldComponent_Dominion.Instance?.EventLog.Events;
            if (events == null) return;
            DLog.Message($"{events.Count} events:\n" + string.Join("\n", events.Select(e => e + "  " + e.Headline)));
        }

        [DebugAction(Category, "Clear event log", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearEvents() => WorldComponent_Dominion.Instance?.EventLog.Clear();

        [DebugAction(Category, "Integration self-test")]
        private static void SelfTest() => DLog.Message(IntegrationRegistry.SelfTestReport());

        [DebugAction(Category, "Profiler: log report")]
        private static void ProfilerReport() => DLog.Message(DominionProfiler.Report());

        [DebugAction(Category, "Profiler: reset")]
        private static void ProfilerReset() => DominionProfiler.Reset();

        private static IEnumerable<Faction> DebugFactions() =>
            Find.FactionManager.AllFactionsListForReading.Where(f => !f.Hidden || f.IsPlayer);
    }
}
