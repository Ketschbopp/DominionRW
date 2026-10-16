using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A stateless piece of simulation logic. State lives in <see cref="WorldComponent_Dominion"/>.
    /// Subclasses are found by reflection and created once at startup.
    ///
    /// Timing: <see cref="DailyFaction"/> runs once per in-game day for each simulated faction,
    /// with factions spread across the day so no single tick does all the work.
    /// <see cref="DailyGlobal"/> runs once per day at midnight, <see cref="Hourly"/> once per hour.
    /// </summary>
    public abstract class SimSystem
    {
        /// <summary>Lower runs first within the same slot.</summary>
        public virtual int Order => 0;

        public virtual string Name => GetType().Name;

        public virtual void DailyFaction(Faction faction) { }

        public virtual void DailyGlobal() { }

        public virtual void Hourly() { }

        public virtual void OnEvent(DominionEvent ev) { }
    }

    public static class SimSystemRegistry
    {
        private static readonly List<SimSystem> systems = new List<SimSystem>();

        public static IReadOnlyList<SimSystem> All => systems;

        public static void Initialize()
        {
            systems.Clear();
            foreach (Type t in GenTypes.AllSubclassesNonAbstract(typeof(SimSystem)))
            {
                try
                {
                    systems.Add((SimSystem)Activator.CreateInstance(t));
                }
                catch (Exception e)
                {
                    DLog.Error($"Could not create sim system {t.FullName}: {e}");
                }
            }
            systems.SortStable((a, b) => a.Order.CompareTo(b.Order));
            DLog.Verbose($"{systems.Count} sim systems registered.");
        }
    }
}
