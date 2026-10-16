using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    public enum EventCategory { General, Diplomacy, War, Military, Economy, Politics, Espionage, Disaster, Culture, Council }

    /// <summary>How hidden an event is. Higher values need more intel before they appear as news.</summary>
    public enum Secrecy { Public, Restricted, Secret, BlackOps }

    /// <summary>
    /// Describes a kind of world event. Every meaningful thing that happens, from Dominion, vanilla or
    /// a linked mod, is raised on the <see cref="EventBus"/> as a <see cref="DominionEvent"/> of one of these defs.
    /// </summary>
    public class DominionEventDef : Def
    {
        public EventCategory category = EventCategory.General;
        public Secrecy secrecy = Secrecy.Public;

        /// <summary>0..1, how strongly this event competes for news coverage.</summary>
        public float newsworthiness = 0.5f;

        /// <summary>Headline used until grammar-based news lands in M2. {0} and {1} are the faction names.</summary>
        [MustTranslate]
        public string headline;

        /// <summary>Grammar for news reports about this event: needs "headline" and "story" roots. Optional.</summary>
        public RulePackDef newsRules;

        /// <summary>Makes this a world event: rolled daily per faction under its conditions, with its effects. Optional.</summary>
        public WorldEventProps world;

        /// <summary>Hidden from the dev-menu event list.</summary>
        public bool debugHidden;

        public Type workerClass = typeof(DominionEventWorker);

        [Unsaved]
        private DominionEventWorker workerInt;

        public DominionEventWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (DominionEventWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
                yield return e;
            if (workerClass == null || !typeof(DominionEventWorker).IsAssignableFrom(workerClass))
                yield return "workerClass must derive from Dominion.DominionEventWorker";
            if (newsworthiness < 0f || newsworthiness > 1f)
                yield return "newsworthiness must be between 0 and 1";
        }
    }

    /// <summary>Optional C# behaviour for an event def. The default does nothing beyond logging.</summary>
    public class DominionEventWorker
    {
        public DominionEventDef def;

        /// <summary>Called once when the event is raised, before listeners are notified.</summary>
        public virtual void Apply(DominionEvent ev) { }

        public virtual string GetHeadline(DominionEvent ev)
        {
            string a = ev.primary?.Name ?? "?";
            string b = ev.secondary?.Name ?? "?";
            if (def.headline.NullOrEmpty())
                return def.LabelCap;
            return string.Format(def.headline, a, b, ev.detail ?? "");
        }
    }

    [DefOf]
    public static class DominionEventDefOf
    {
        public static DominionEventDef Dominion_DebugTest;
        public static DominionEventDef Dominion_WarDeclared;
        public static DominionEventDef Dominion_PeaceMade;
        public static DominionEventDef Dominion_RelationsWarmed;
        public static DominionEventDef Dominion_LeaderDied;
        public static DominionEventDef Dominion_AllianceBroken;
        public static DominionEventDef Dominion_Riots;
        public static DominionEventDef Dominion_Coup;
        public static DominionEventDef Dominion_Revolution;
        public static DominionEventDef Dominion_CapitalLost;
        public static DominionEventDef Dominion_CapitalEstablished;
        public static DominionEventDef Dominion_NewsCorrection;
        public static DominionEventDef Dominion_TreatySigned;
        public static DominionEventDef Dominion_TreatyCancelled;
        public static DominionEventDef Dominion_TreatyBroken;
        public static DominionEventDef Dominion_TreatyExpired;
        public static DominionEventDef Dominion_EmbargoDeclared;
        public static DominionEventDef Dominion_EmbargoLifted;
        public static DominionEventDef Dominion_BordersChanged;
        public static DominionEventDef Dominion_Denounced;
        public static DominionEventDef Dominion_TributePaid;
        public static DominionEventDef Dominion_DelegationDispatched;
        public static DominionEventDef Dominion_DelegationIntercepted;
        public static DominionEventDef Dominion_SummitHeld;
        public static DominionEventDef Dominion_SummitFailed;
        public static DominionEventDef Dominion_HostagesExchanged;
        public static DominionEventDef Dominion_EnvoyMission;
        public static DominionEventDef Dominion_EnvoyCaptured;
        public static DominionEventDef Dominion_CouncilConvened;
        public static DominionEventDef Dominion_DecreePassed;
        public static DominionEventDef Dominion_MotionRejected;
        public static DominionEventDef Dominion_DecreeViolated;
        public static DominionEventDef Dominion_DecreeExpired;
        public static DominionEventDef Dominion_AgentsExposed;
        public static DominionEventDef Dominion_ForeignAgentsCaught;
        public static DominionEventDef Dominion_PowerStruggle;
        public static DominionEventDef Dominion_SettlementLooted;
        public static DominionEventDef Dominion_VassalOutpostGained;
        public static DominionEventDef Dominion_VassalOutpostLost;
        public static DominionEventDef Dominion_Emigration;
        public static DominionEventDef Dominion_FederationCharter;
        public static DominionEventDef Dominion_FederationJoined;
        public static DominionEventDef Dominion_FederationLeft;
        public static DominionEventDef Dominion_FederationProclaimed;
        public static DominionEventDef Dominion_FactionAbsorbed;
        public static DominionEventDef Dominion_AidSent;
        public static DominionEventDef Dominion_JoinedWar;
        public static DominionEventDef Dominion_CallToArmsRefused;
        public static DominionEventDef Dominion_WarbandMarching;
        public static DominionEventDef Dominion_Reinforcements;
        public static DominionEventDef Dominion_BattleFought;
        public static DominionEventDef Dominion_SettlementOccupied;
        public static DominionEventDef Dominion_SettlementReturned;
        public static DominionEventDef Dominion_WarbandIntercepted;
        public static DominionEventDef Dominion_Blockade;
        public static DominionEventDef Dominion_Capitulation;
        public static DominionEventDef Dominion_PeaceTerms;
        public static DominionEventDef Dominion_PuppetFreed;
        public static DominionEventDef Dominion_ColdWarBegan;
        public static DominionEventDef Dominion_ColdWarEnded;
        public static DominionEventDef Dominion_ArmsRace;
        public static DominionEventDef Dominion_BorderSkirmish;
        public static DominionEventDef Dominion_ProxyWar;
        public static DominionEventDef Dominion_Assassination;
        public static DominionEventDef Dominion_Sabotage;
        public static DominionEventDef Dominion_FalseFlag;
        public static DominionEventDef Dominion_TechnologyStolen;
        public static DominionEventDef Dominion_PuppetInstalled;
        public static DominionEventDef Dominion_HostageRescued;
        public static DominionEventDef Dominion_VisitIncident;

        static DominionEventDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionEventDefOf));
        }
    }
}
