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

        [DebugAction(Category, "Toggle reveal all intel")]
        private static void ToggleRevealIntel()
        {
            IntelUtility.RevealAll = !IntelUtility.RevealAll;
            Messages.Message("Dominion: reveal all intel " + (IntelUtility.RevealAll ? "ON" : "OFF"), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction(Category, "Set intel on faction...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> SetIntel()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return nodes;
            foreach (Faction f in comp.Cache.Simulated)
            {
                var node = new DebugActionNode(f.Name);
                foreach (float v in new[] { 0f, 10f, 25f, 50f, 80f, 100f })
                {
                    Faction cf = f;
                    float cv = v;
                    node.AddChild(new DebugActionNode(v.ToString("F0"), DebugActionType.Action, () => comp.GetState(cf).playerIntel = cv));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "Deliver all pending news now", allowedGameStates = AllowedGameStates.Playing)]
        private static void DeliverAllNews()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            foreach (NewsItem n in comp.News.Pending.ToList())
            {
                if (n.Hidden)
                {
                    n.source = NewsSource.Leak;
                    n.confidence = NewsConfidence.Rumor;
                }
                n.arrivesTick = Find.TickManager.TicksGame;
                NewsSystem.Deliver(n, comp);
            }
        }

        [DebugAction(Category, "Log pending and hidden news", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogPendingNews()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            int now = Find.TickManager.TicksGame;
            DLog.Message($"{comp.News.Pending.Count} pending/hidden news:\n" + string.Join("\n", comp.News.Pending.Select(n =>
                $"#{n.id} {n.def.defName} [{n.primary?.Name} / {n.secondary?.Name}] secrecy={n.secrecy} " +
                (n.Hidden ? "HIDDEN" : $"arrives in {(n.arrivesTick - now).ToStringTicksToPeriod()}") + $" truthful={n.truthful}")));
        }

        [DebugAction(Category, "Add 50 radio coverage", allowedGameStates = AllowedGameStates.Playing)]
        private static void AddCoverage()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp != null) comp.radioCoverage = System.Math.Min(100f, comp.radioCoverage + 50f);
        }

        [DebugAction(Category, "Spread a false rumor", allowedGameStates = AllowedGameStates.Playing)]
        private static void FalseRumor()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            if (NewsSystem.TryCreateFalseRumor(comp, force: true) == null)
                Messages.Message("Dominion: no faction with low enough intel for a rumor.", MessageTypeDefOf.RejectInput, historical: false);
        }

        [DebugAction(Category, "Fill influence", allowedGameStates = AllowedGameStates.Playing)]
        private static void FillInfluence() => WorldComponent_Dominion.Instance?.Player.AddInfluence(PlayerState.MaxInfluence);

        [DebugAction(Category, "Sign treaty...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> SignTreaty()
        {
            var nodes = new List<DebugActionNode>();
            foreach (TreatyDef def in DefDatabase<TreatyDef>.AllDefsListForReading)
            {
                var defNode = new DebugActionNode(def.defName);
                foreach (Faction a in DebugFactions())
                {
                    var aNode = new DebugActionNode(a.Name);
                    foreach (Faction b in DebugFactions())
                    {
                        if (b == a) continue;
                        TreatyDef cd = def;
                        Faction ca = a, cb = b;
                        aNode.AddChild(new DebugActionNode(b.Name, DebugActionType.Action,
                            () => WorldComponent_Dominion.Instance.Treaties.Sign(cd, ca, cb, ca)));
                    }
                    defNode.AddChild(aNode);
                }
                nodes.Add(defNode);
            }
            return nodes;
        }

        [DebugAction(Category, "Force summit with...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> ForceSummit()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return nodes;
            foreach (Faction f in comp.Cache.Simulated)
            {
                var node = new DebugActionNode(f.Name);
                foreach (SummitVenue venue in System.Enum.GetValues(typeof(SummitVenue)))
                {
                    Faction cf = f;
                    SummitVenue cv = venue;
                    node.AddChild(new DebugActionNode(venue.ToString(), DebugActionType.Action, () =>
                    {
                        SummitAgenda agenda = cf.HostileTo(Faction.OfPlayer) ? SummitAgenda.Peace : SummitAgenda.Relations;
                        if (!SummitSystem.Arrange(cf, agenda, null, cv, out string reason, force: true))
                            Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
                    }));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "Dispatch alliance delegation...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> DispatchDelegation()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return nodes;
            foreach (Faction a in comp.Cache.Simulated)
            {
                var node = new DebugActionNode(a.Name);
                foreach (Faction b in comp.Cache.Simulated)
                {
                    if (a == b) continue;
                    Faction ca = a, cb = b;
                    node.AddChild(new DebugActionNode(b.Name, DebugActionType.Action,
                        () => DelegationUtility.DispatchAgreement(ca, cb, AgreementKind.Alliance)));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "Council: announce session (in 1 day)", allowedGameStates = AllowedGameStates.Playing)]
        private static void CouncilAnnounce()
        {
            CouncilState c = WorldComponent_Dominion.Instance?.Council;
            if (c == null) return;
            c.phase = CouncilPhase.Idle;
            CouncilSystem.Announce(GenDate.TicksPerDay);
        }

        [DebugAction(Category, "Council: everyone arrives, open session now", allowedGameStates = AllowedGameStates.Playing)]
        private static void CouncilOpenNow()
        {
            CouncilState c = WorldComponent_Dominion.Instance?.Council;
            if (c == null) return;
            if (c.phase != CouncilPhase.Announced) CouncilSystem.Announce(GenDate.TicksPerHour);
            foreach (Faction f in CouncilUtility.Members())
                if (!f.IsPlayer && !c.attending.Contains(f)) c.attending.Add(f);
            if (c.host != null && !c.host.IsPlayer && c.playerDelegate == null && LeadershipUtility.HeadOfState() != null)
                c.playerDelegate = LeadershipUtility.HeadOfState();
            c.nextSessionTick = Find.TickManager.TicksGame;
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
