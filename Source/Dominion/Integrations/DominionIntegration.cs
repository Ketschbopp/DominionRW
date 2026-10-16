using System.Collections.Generic;

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
    }
}
