using System.Collections.Generic;
using RimWorld;

namespace Dominion
{
    public enum IntegrationStatus
    {
        /// <summary>Not checked yet.</summary>
        Pending,
        /// <summary>The mod is not in the active mod list.</summary>
        NotInstalled,
        /// <summary>Switched off in Dominion's settings.</summary>
        Disabled,
        /// <summary>The mod is active but some of its members could not be found (usually a version mismatch).</summary>
        Failed,
        Active,
    }

    /// <summary>
    /// Connects Dominion to one other mod. Subclasses are found by reflection, so adding a mod means
    /// adding one class. Nothing here references the other mod at compile time.
    /// </summary>
    public abstract class DominionIntegration
    {
        public abstract string PackageId { get; }

        public abstract string Label { get; }

        /// <summary>Required mods are listed in About.xml modDependencies and cannot be switched off.</summary>
        public virtual bool Required => false;

        public IntegrationStatus Status { get; internal set; } = IntegrationStatus.Pending;

        public IReadOnlyList<string> BindingReport { get; internal set; } = new List<string>();

        public bool IsActive => Status == IntegrationStatus.Active;

        /// <summary>Resolve everything this integration needs. Called once at startup if the mod is active.</summary>
        protected internal abstract void Bind(Binder b);

        /// <summary>Called once after a successful bind. Subscribe to the event bus here.</summary>
        protected internal virtual void OnActivated() { }

        /// <summary>Called after every world load or new game, for active integrations only.</summary>
        protected internal virtual void OnWorldLoaded() { }

        // Modifiers the core systems add up over all active integrations. Keep them cheap: they run daily per faction.

        /// <summary>Added to <paramref name="f"/>'s target opinion of <paramref name="toward"/>.</summary>
        public virtual float OpinionModifier(Faction f, Faction toward) => 0f;

        /// <summary>Added to the player's intel target on <paramref name="f"/>.</summary>
        public virtual float IntelModifier(Faction f) => 0f;

        /// <summary>Added to <paramref name="f"/>'s target stability.</summary>
        public virtual float StabilityModifier(Faction f) => 0f;

        /// <summary>Economy index change (0.05 = +5%).</summary>
        public virtual float EconomyModifier(Faction f) => 0f;

        /// <summary>Added to a Rim Council member's vote weight.</summary>
        public virtual float VoteWeightModifier(Faction f) => 0f;

        /// <summary>Once a day, for active integrations.</summary>
        public virtual void DailyGlobal() { }

        /// <summary>Every Dominion event, for active integrations.</summary>
        public virtual void OnEvent(DominionEvent ev) { }
    }
}
