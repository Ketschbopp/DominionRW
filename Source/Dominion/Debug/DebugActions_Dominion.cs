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
                        () => RaiseAndReport(capturedDef, capturedPrimary, null)));
                    foreach (Faction secondary in DebugFactions())
                    {
                        if (secondary == primary) continue;
                        Faction capturedSecondary = secondary;
                        primaryNode.AddChild(new DebugActionNode(secondary.Name, DebugActionType.Action,
                            () => RaiseAndReport(capturedDef, capturedPrimary, capturedSecondary)));
                    }
                    defNode.AddChild(primaryNode);
                }
                nodes.Add(defNode);
            }
            return nodes;
        }

        /// <summary>
        /// Raises an event and delivers its news at once (normally news takes hours or days to arrive), so the
        /// result can be seen straight away. Most events only report: the systems that cause them apply the effects.
        /// </summary>
        private static void RaiseAndReport(DominionEventDef def, Faction a, Faction b)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            DominionEvent ev = EventBus.Raise(def, a, b);
            int delivered = DeliverNewsOf(comp, n => n.eventId == ev.id, includeHidden: true);
            Messages.Message($"Dominion: raised {def.defName}; {delivered} news item(s) delivered to the newspaper.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>Delivers matching pending news now. Hidden items are delivered as leaks when asked to.</summary>
        private static int DeliverNewsOf(WorldComponent_Dominion comp, System.Func<NewsItem, bool> match, bool includeHidden)
        {
            int count = 0;
            foreach (NewsItem n in comp.News.Pending.ToList())
            {
                if (!match(n) || (n.Hidden && !includeHidden)) continue;
                if (n.Hidden)
                {
                    n.source = NewsSource.Leak;
                    n.confidence = NewsConfidence.Rumor;
                }
                n.arrivesTick = Find.TickManager.TicksGame;
                NewsSystem.Deliver(n, comp);
                count++;
            }
            return count;
        }

        [DebugAction(Category, "Run daily simulation now", allowedGameStates = AllowedGameStates.Playing)]
        private static void RunDailyNow() => RunDays(false);

        [DebugAction(Category, "Make every faction act now (and run a day)", allowedGameStates = AllowedGameStates.Playing)]
        private static void ActNow() => RunDays(true);

        /// <summary>One simulated day, then whatever news it produced is delivered at once and summarised.</summary>
        private static void RunDays(bool forceAi)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return;
            if (forceAi)
                foreach (FactionState s in comp.AllStates)
                    s.nextAiActionTick = 0;
            int firstEvent = comp.EventLog.PeekNextId;
            int failuresBefore = DominionScheduler.FailureCounts.Values.Sum();
            comp.Scheduler.RunFullDayNow();
            int events = comp.EventLog.PeekNextId - firstEvent;
            int news = DeliverNewsOf(comp, n => n.eventId >= firstEvent, includeHidden: false);
            int failures = DominionScheduler.FailureCounts.Values.Sum() - failuresBefore;
            Messages.Message($"Dominion: ran one simulated day: {events} event(s), {news} news item(s) delivered" +
                (failures > 0 ? $", {failures} system failure(s) (see Health check)." : "."), failures > 0 ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction(Category, "Health check", allowedGameStates = AllowedGameStates.Playing)]
        private static void HealthCheck()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var sb = new System.Text.StringBuilder();
            if (comp == null)
            {
                Find.WindowStack.Add(new Dialog_MessageBox("Dominion: no world component. The mod did not load into this save."));
                return;
            }
            comp.Scheduler.Rebuild();
            sb.AppendLine(comp.Scheduler.Describe());
            sb.AppendLine($"Day {GenDate.DaysPassed}, tick {Find.TickManager.TicksGame}. Events logged: {comp.EventLog.PeekNextId}. " +
                          $"News: {comp.News.Delivered.Count} delivered, {comp.News.Pending.Count(n => !n.Hidden)} on the way, {comp.News.Pending.Count(n => n.Hidden)} hidden. " +
                          $"Radio coverage {comp.radioCoverage:F0}.");
            sb.AppendLine();
            sb.AppendLine("Factions (settlements on the surface, intel, stability, economy, next AI action):");
            int now = Find.TickManager.TicksGame;
            foreach (Faction f in comp.Cache.Simulated)
            {
                FactionState s = comp.GetState(f);
                string next = s.nextAiActionTick > now ? (s.nextAiActionTick - now).ToStringTicksToPeriod() : "now";
                sb.AppendLine($"  {f.Name}: {comp.Cache.SettlementsOf(f).Count} towns, intel {s.playerIntel:F0}, stability {s.stability:F0}, economy {s.economyIndex:F0}, wars {s.activeWars}, AI in {next}");
            }
            sb.AppendLine();
            if (DominionScheduler.FailureCounts.Count == 0) sb.AppendLine("No system failures since the game started.");
            else
            {
                sb.AppendLine("System failures since the game started (count, last error):");
                foreach (KeyValuePair<string, int> kv in DominionScheduler.FailureCounts.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value}x, {DominionScheduler.LastFailure[kv.Key]}");
            }
            sb.AppendLine();
            sb.AppendLine(IntegrationRegistry.SelfTestReport());
            string text = sb.ToString();
            DLog.Message("Health check\n" + text);
            Find.WindowStack.Add(new Dialog_MessageBox(text.Truncate(6000f)));
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
            int n = DeliverNewsOf(comp, _ => true, includeHidden: true);
            Messages.Message($"Dominion: delivered {n} news item(s).", MessageTypeDefOf.NeutralEvent, historical: false);
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

        [DebugAction(Category, "World event: fire on faction...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> FireWorldEvent()
        {
            var nodes = new List<DebugActionNode>();
            foreach (DominionEventDef def in DefDatabase<DominionEventDef>.AllDefsListForReading.Where(d => d.world != null).OrderBy(d => d.defName))
            {
                DominionEventDef capturedDef = def;
                var defNode = new DebugActionNode(def.defName);
                foreach (Faction f in WorldComponent_Dominion.Instance.Cache.Simulated)
                {
                    Faction captured = f;
                    defNode.AddChild(new DebugActionNode(f.Name, DebugActionType.Action,
                        () => WorldEventSystem.Fire(capturedDef, captured, WorldEventSystem.PickPartner(captured, capturedDef.world.partner), null)));
                }
                nodes.Add(defNode);
            }
            return nodes;
        }

        [DebugAction(Category, "Federation: put Charter in force", allowedGameStates = AllowedGameStates.Playing)]
        private static void FederationCharter()
        {
            UnificationState st = WorldComponent_Dominion.Instance.Unification;
            if (st.CharterInForce) return;
            st.charterTick = Find.TickManager.TicksGame;
            EventBus.Raise(DominionEventDefOf.Dominion_FederationCharter, Faction.OfPlayer);
        }

        [DebugAction(Category, "Federation: every major faction joins", allowedGameStates = AllowedGameStates.Playing)]
        private static void FederationAllJoin()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in FederationUtility.Majors().ToList())
            {
                if (f.HostileTo(Faction.OfPlayer) || FederationUtility.IsMember(f)) continue;
                comp.Treaties.Sign(DominionTreatyDefOf.Dominion_FederationMembership, Faction.OfPlayer, f, Faction.OfPlayer);
            }
        }

        [DebugAction(Category, "Federation: log progress", allowedGameStates = AllowedGameStates.Playing)]
        private static void FederationLog()
        {
            FederationUtility.Progress p = FederationUtility.Compute();
            DLog.Message($"Unification: {p.unified}/{p.required} needed of {p.total} (federation {p.federation}, subjugated {p.subjugated}, conquered {p.conquered}); dominant {FederationUtility.DominantPath(p)}");
        }

        [DebugAction(Category, "Federation: proclaim now (ending)", allowedGameStates = AllowedGameStates.Playing)]
        private static void FederationProclaim()
        {
            WorldComponent_Dominion.Instance.Unification.proclaimed = false;
            FederationUtility.Proclaim();
        }

        [DebugAction(Category, "Federation: reset ending", allowedGameStates = AllowedGameStates.Playing)]
        private static void FederationReset()
        {
            UnificationState st = WorldComponent_Dominion.Instance.Unification;
            st.offered = false;
            st.proclaimed = false;
            st.proclaimedTick = -1;
        }

        [DebugAction(Category, "War: log wars", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogWars()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var sb = new System.Text.StringBuilder($"{comp.Wars.wars.Count} wars:\n");
            foreach (War w in comp.Wars.wars)
                sb.AppendLine($"#{w.id} {w.attacker?.Name} vs {w.defender?.Name}: goal={w.goal} score={w.score:F1} battles={w.battles} alliedTo={w.alliedTo?.Name ?? "-"} target={w.goalSettlement?.Label ?? "-"}");
            sb.AppendLine($"Warbands: {WarbandUtility.All().Count()}, occupied: {comp.Wars.originalOwner.Count}, blockades: {comp.Wars.blockades.Count}");
            DLog.Message(sb.ToString());
        }

        [DebugAction(Category, "War: set score...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> SetWarScore()
        {
            var nodes = new List<DebugActionNode>();
            foreach (War w in WorldComponent_Dominion.Instance.Wars.wars)
            {
                var node = new DebugActionNode(w.attacker.Name + " vs " + w.defender.Name);
                foreach (float v in new[] { -100f, -80f, -40f, 0f, 40f, 80f, 100f })
                {
                    War cw = w;
                    float cv = v;
                    node.AddChild(new DebugActionNode(v.ToString("F0"), DebugActionType.Action, () => cw.score = cv));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "War: force capitulation...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> ForceCapitulation()
        {
            var nodes = new List<DebugActionNode>();
            foreach (War w in WorldComponent_Dominion.Instance.Wars.wars)
            {
                War cw = w;
                nodes.Add(new DebugActionNode(w.defender.Name + " surrenders to " + w.attacker.Name, DebugActionType.Action, () => WarUtility.Capitulate(cw, cw.defender)));
                nodes.Add(new DebugActionNode(w.attacker.Name + " surrenders to " + w.defender.Name, DebugActionType.Action, () => WarUtility.Capitulate(cw, cw.attacker)));
            }
            return nodes;
        }

        [DebugAction(Category, "War: launch warband...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> LaunchWarband()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction a in comp.Cache.Simulated)
            {
                var node = new DebugActionNode(a.Name);
                foreach (Faction b in DebugFactions())
                {
                    if (a == b) continue;
                    Faction ca = a, cb = b;
                    node.AddChild(new DebugActionNode(b.Name, DebugActionType.Action, () =>
                    {
                        if (WarbandUtility.Launch(ca, cb, cb.IsPlayer ? WarbandMission.RaidPlayer : WarbandMission.Siege) == null)
                            Messages.Message("Dominion: no route for a warband.", MessageTypeDefOf.RejectInput, historical: false);
                    }));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "War: start cold war...", allowedGameStates = AllowedGameStates.Playing)]
        private static List<DebugActionNode> StartColdWar()
        {
            var nodes = new List<DebugActionNode>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction a in DebugFactions())
            {
                var node = new DebugActionNode(a.Name);
                foreach (Faction b in DebugFactions())
                {
                    if (a == b) continue;
                    Faction ca = a, cb = b;
                    node.AddChild(new DebugActionNode(b.Name, DebugActionType.Action, () => ColdWarSystem.Begin(ca, cb)));
                }
                nodes.Add(node);
            }
            return nodes;
        }

        [DebugAction(Category, "Ops: finish abstract operations now", allowedGameStates = AllowedGameStates.Playing)]
        private static void FinishOps()
        {
            foreach (Operation op in WorldComponent_Dominion.Instance.Intel.operations)
                if (!op.physical) op.endTick = Find.TickManager.TicksGame;
        }

        [DebugAction(Category, "Ops: physical objective done", allowedGameStates = AllowedGameStates.Playing)]
        private static void PhysicalDone()
        {
            Operation op = WorldComponent_Dominion.Instance.Intel.ActivePhysical;
            if (op != null) op.achieved = true;
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
