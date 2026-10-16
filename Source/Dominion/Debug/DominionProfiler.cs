using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Dominion
{
    /// <summary>
    /// Allocation-free timing of Dominion subsystems. Disabled unless profiling is switched on
    /// in settings, so normal play pays only one bool check per measured block.
    /// </summary>
    public static class DominionProfiler
    {
        private class Entry
        {
            public long ticks;
            public long maxTicks;
            public int calls;
        }

        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public static bool Enabled => DominionMod.Settings != null && DominionMod.Settings.profilingEnabled;

        public static Scope Measure(string key) => Enabled ? new Scope(key, Stopwatch.GetTimestamp()) : default;

        public readonly struct Scope : IDisposable
        {
            private readonly string key;
            private readonly long start;

            public Scope(string key, long start)
            {
                this.key = key;
                this.start = start;
            }

            public void Dispose()
            {
                if (key == null) return;
                long elapsed = Stopwatch.GetTimestamp() - start;
                if (!entries.TryGetValue(key, out Entry e))
                {
                    e = new Entry();
                    entries[key] = e;
                }
                e.ticks += elapsed;
                e.calls++;
                if (elapsed > e.maxTicks) e.maxTicks = elapsed;
            }
        }

        public static string Report()
        {
            if (entries.Count == 0)
                return Enabled ? "No samples yet." : "Profiling is off (Mod settings > Dominion > Performance).";
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder("Dominion profiler (total ms / calls / avg ms / max ms):\n");
            foreach (var kv in entries.OrderByDescending(kv => kv.Value.ticks))
            {
                Entry e = kv.Value;
                sb.AppendLine($"  {kv.Key,-40} {e.ticks * msPerTick,10:F2} {e.calls,8} {e.ticks * msPerTick / e.calls,9:F4} {e.maxTicks * msPerTick,9:F4}");
            }
            return sb.ToString();
        }

        public static void Reset() => entries.Clear();
    }
}
