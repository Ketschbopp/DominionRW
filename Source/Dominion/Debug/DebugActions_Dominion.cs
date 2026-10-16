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

        [DebugAction(Category, "Force relation...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> ForceRelation()
        {
            var nodes = new List<DebugActionNode>();
            foreach (Faction a in DebugFactions())
            {
                var aNode = new DebugActionNode(a.Name);
                foreach (Faction b in DebugFactions())
                {
                    if (b == a) continue;
                    var bNode = new DebugActionNode(b.Name);
                    foreach (FactionRelationKind kind in new[] { FactionRelationKind.Hostile, FactionRelationKind.Neutral, FactionRelationKind.Ally })
                    {
                        Faction ca = a, cb = b;
                        FactionRelationKind ck = kind;
                        bNode.AddChild(new DebugActionNode(kind.GetLabel(), DebugActionType.Action,
                            () => DiplomacyUtility.SetRelationKind(ca, cb, ck, ca, "Dev")));
                    }
                    aNode.AddChild(bNode);
                }
                nodes.Add(aNode);
            }
            return nodes;
        }

        [DebugAction(Category, "Set stability...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> SetStability()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return nodes;
            foreach (Faction f in comp.Cache.Simulated)
            {
                var node = new DebugActionNode(f.Name);
                foreach (float v in new[] { 0f, 5f, 15f, 25f, 50f, 75f, 100f })
                {
                    Faction cf = f;
                    float cv = v;
                    node.AddChild(new DebugActionNode(v.ToString("F0"), DebugActionType.Action, () => comp.GetState(cf).stability = cv));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "Log faction states", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogFactionStates()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            var sb = new System.Text.StringBuilder("Dominion faction states:\n");
            foreach (Faction f in comp.Cache.Simulated)
            {
                if (!comp.TryGetState(f, out FactionState s)) continue;
                sb.AppendLine($"{f.Name}: gov={s.government} stab={s.stability:F1} legit={s.legitimacy:F1} weary={s.warWeariness:F1} wars={s.activeWars} " +
                              $"econ={s.economyIndex:F0} mil={s.militaryStrength:F0} pop={s.population}/{s.totalLiving} wealth={s.wealth:F0} " +
                              $"terr={s.territoryTiles} vassalOutposts={s.vassalOutposts} capital={s.capital?.Label ?? "-"} dominantBloc={s.DominantBloc} " +
                              $"aggr={s.personality.aggression:F2} effAggr={s.effectiveAggression:F2}");
                foreach (Faction g in comp.Cache.Simulated)
                {
                    RelationState r = g == f ? null : comp.TryGetRelation(f, g);
                    if (r != null)
                        sb.AppendLine($"    -> {g.Name}: opinion={r.opinion:F0} target={r.targetOpinion:F0} {f.RelationKindWith(g)} war={r.AtWar} grievance={r.grievance:F0} dist={r.distance:F0}");
                }
            }
            DLog.Message(sb.ToString());
        }

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
