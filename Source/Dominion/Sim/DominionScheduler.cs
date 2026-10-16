using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Spreads daily work across the day. The day is split into <see cref="SlotsPerDay"/> slots
    /// (every <see cref="TicksPerSlot"/> ticks). Each simulated faction is hashed into one slot,
    /// so on a given tick at most a few factions are processed, and on most ticks nothing runs.
    /// </summary>
    public class DominionScheduler
    {
        public const int TicksPerSlot = 250;
        public const int SlotsPerDay = GenDate.TicksPerDay / TicksPerSlot; // 240
        private const int SlotsPerHour = GenDate.TicksPerHour / TicksPerSlot; // 10

        private readonly List<Faction>[] buckets = new List<Faction>[SlotsPerDay];
        private int bucketedFactionCount = -1;
        private int lastRebuildDay = -1;

        public DominionScheduler()
        {
            for (int i = 0; i < SlotsPerDay; i++)
                buckets[i] = new List<Faction>();
        }

        public static bool IsSimulated(Faction f) =>
            f != null && !f.IsPlayer && !f.Hidden && !f.defeated && !f.temporary && !f.deactivated
            && f.def.humanlikeFaction;

        public static int SlotFor(Faction f)
        {
            // Stable across saves: based on the faction's load id, scrambled so neighbours spread out.
            uint h = (uint)f.loadID * 2654435761u;
            return (int)(h % SlotsPerDay);
        }

        public void Tick(int ticksGame)
        {
            if (ticksGame % TicksPerSlot != 0) return;

            int slot = (ticksGame / TicksPerSlot) % SlotsPerDay;
            int day = ticksGame / GenDate.TicksPerDay;

            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            if (all.Count != bucketedFactionCount || (slot == 0 && day != lastRebuildDay))
            {
                Rebuild();
                lastRebuildDay = day;
            }

            IReadOnlyList<SimSystem> systems = SimSystemRegistry.All;

            for (int i = 0; i < systems.Count; i++)
            {
                SimSystem s = systems[i];
                using (DominionProfiler.Measure(s.Name, "Slot"))
                {
                    try
                    {
                        s.Slot(slot);
                    }
                    catch (Exception e)
                    {
                        Failed(s.Name + ".Slot", e);
                    }
                }
            }

            if (slot % SlotsPerHour == 0)
            {
                for (int i = 0; i < systems.Count; i++)
                    Run(systems[i], "Hourly", s => s.Hourly());
            }

            if (slot == 0)
            {
                for (int i = 0; i < systems.Count; i++)
                    Run(systems[i], "DailyGlobal", s => s.DailyGlobal());
            }

            List<Faction> bucket = buckets[slot];
            for (int f = 0; f < bucket.Count; f++)
            {
                Faction faction = bucket[f];
                if (!IsSimulated(faction)) continue;
                for (int i = 0; i < systems.Count; i++)
                {
                    SimSystem s = systems[i];
                    using (DominionProfiler.Measure(s.Name, "DailyFaction"))
                    {
                        try
                        {
                            s.DailyFaction(faction);
                        }
                        catch (Exception e)
                        {
                            Failed(s.Name + ".DailyFaction", e, faction.Name);
                        }
                    }
                }
            }
        }

        /// <summary>Runs every system's daily pass for every faction immediately (dev menu).</summary>
        public void RunFullDayNow()
        {
            Rebuild();
            IReadOnlyList<SimSystem> systems = SimSystemRegistry.All;
            for (int i = 0; i < systems.Count; i++)
                Run(systems[i], "DailyGlobal", s => s.DailyGlobal());
            foreach (List<Faction> bucket in buckets)
                foreach (Faction faction in bucket)
                    if (IsSimulated(faction))
                        for (int i = 0; i < systems.Count; i++)
                        {
                            Faction captured = faction;
                            Run(systems[i], "DailyFaction", s => s.DailyFaction(captured));
                        }
        }

        public void Rebuild()
        {
            for (int i = 0; i < SlotsPerDay; i++)
                buckets[i].Clear();
            List<Faction> all = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (IsSimulated(all[i]))
                    buckets[SlotFor(all[i])].Add(all[i]);
            bucketedFactionCount = all.Count;
        }

        public string Describe()
        {
            int simulated = 0, used = 0;
            foreach (List<Faction> b in buckets)
            {
                simulated += b.Count;
                if (b.Count > 0) used++;
            }
            return $"Scheduler: {simulated} simulated factions across {used}/{SlotsPerDay} slots, {SimSystemRegistry.All.Count} systems.";
        }

        // ---------------------------------------------------------------- failures

        /// <summary>How often each system phase has thrown since the game started, and its last error.</summary>
        public static readonly Dictionary<string, int> FailureCounts = new Dictionary<string, int>();
        public static readonly Dictionary<string, string> LastFailure = new Dictionary<string, string>();

        /// <summary>
        /// A system threw. Logged in full the first time, counted every time, and the player is told once per phase,
        /// so a broken system is never silent (see the dev action "Health check").
        /// </summary>
        public static void Failed(string what, Exception e, string context = null)
        {
            FailureCounts[what] = (FailureCounts.TryGetValue(what, out int n) ? n : 0) + 1;
            LastFailure[what] = (context != null ? context + ": " : "") + e.Message;
            if (n > 0) return;
            DLog.Error($"{what} failed{(context != null ? " for " + context : "")}: {e}");
            if (Current.ProgramState == ProgramState.Playing)
                Messages.Message($"Dominion: {what} failed and is being skipped. Details in the log; Dev menu → Dominion → Health check.",
                    MessageTypeDefOf.RejectInput, historical: false);
        }

        private static void Run(SimSystem s, string phase, Action<SimSystem> action)
        {
            using (DominionProfiler.Measure(s.Name, phase))
            {
                try
                {
                    action(s);
                }
                catch (Exception e)
                {
                    Failed(s.Name + "." + phase, e);
                }
            }
        }
    }
}
