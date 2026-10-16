using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The single stream every subsystem and integration reacts to. Dispatch is synchronous and
    /// allocation-free apart from the event itself.
    /// </summary>
    public static class EventBus
    {
        /// <summary>Events raised from inside a listener nest; past this depth they are dropped to stop loops.</summary>
        private const int MaxDepth = 8;

        private static int depth;

        /// <summary>For integrations and other code that is not a <see cref="SimSystem"/>.</summary>
        public static event Action<DominionEvent> Raised;

        public static DominionEvent Raise(DominionEventDef def, Faction primary = null, Faction secondary = null,
            int tile = -1, float magnitude = 1f, string source = null)
        {
            var ev = new DominionEvent(def, primary, secondary) { tile = tile, magnitude = magnitude, source = source };
            Raise(ev);
            return ev;
        }

        public static void Raise(DominionEvent ev)
        {
            if (ev?.def == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null)
            {
                DLog.Verbose("Event raised with no world loaded, ignored: " + ev);
                return;
            }
            if (depth >= MaxDepth)
            {
                DLog.WarningOnce($"Event chain deeper than {MaxDepth}, dropping {ev.def.defName}. A listener is probably re-raising events in a loop.", ev.def.shortHash);
                return;
            }

            depth++;
            try
            {
                ev.id = comp.EventLog.NextId();
                ev.tick = Find.TickManager.TicksGame;
                comp.EventLog.Add(ev);
                DLog.Verbose("Event " + ev);

                using (DominionProfiler.Measure("EventBus", ev.def.defName))
                {
                    try
                    {
                        ev.def.Worker.Apply(ev);
                    }
                    catch (Exception e)
                    {
                        DLog.WarningOnce($"Worker for {ev.def.defName} failed: {e}", ev.def.shortHash ^ 0x1);
                    }

                    IReadOnlyList<SimSystem> systems = SimSystemRegistry.All;
                    for (int i = 0; i < systems.Count; i++)
                    {
                        try
                        {
                            systems[i].OnEvent(ev);
                        }
                        catch (Exception e)
                        {
                            DLog.WarningOnce($"{systems[i].Name}.OnEvent failed on {ev.def.defName}: {e}", systems[i].Name.GetHashCode() ^ ev.def.shortHash);
                        }
                    }

                    if (Raised != null)
                    {
                        foreach (Action<DominionEvent> handler in Raised.GetInvocationList())
                        {
                            try
                            {
                                handler(ev);
                            }
                            catch (Exception e)
                            {
                                DLog.WarningOnce($"Event handler {handler.Method.DeclaringType?.Name}.{handler.Method.Name} failed: {e}", handler.Method.GetHashCode());
                            }
                        }
                    }
                }
            }
            finally
            {
                depth--;
            }
        }
    }
}
