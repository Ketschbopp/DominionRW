using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Internal politics: stability, legitimacy, war weariness and blocs drift daily, and low stability
    /// leads to riots, coups and revolutions. Factions are never split; regimes change inside the faction.
    /// </summary>
    public class PoliticsSystem : SimSystem
    {
        public override int Order => 20;

        private const int RiotCooldownDays = 20;
        private const int CoupCooldownDays = 90;
        private const int RevolutionCooldownDays = 60;
        private const int RevolutionAfterLowDays = 10;

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(faction);
            DominionSettings settings = DominionMod.Settings;
            int now = Find.TickManager.TicksGame;

            if (!s.governmentInitialized)
                Initialize(s, faction);

            // A new leader starts with limited legitimacy.
            if (faction.leader != s.lastKnownLeader)
            {
                if (s.lastKnownLeader != null)
                    s.legitimacy = Mathf.Min(s.legitimacy, 45f);
                s.lastKnownLeader = faction.leader;
                s.leaderSinceTick = now;
            }

            float hawks = s.Bloc(Bloc.Hawks), doves = s.Bloc(Bloc.Doves);

            // War weariness
            if (s.activeWars > 0)
                s.warWeariness += 0.5f * s.activeWars * (1.2f - hawks) * (1f - s.warWeariness / 120f); // tapers near the top
            else
                s.warWeariness -= 1f;
            s.warWeariness = Mathf.Clamp(s.warWeariness, 0f, 100f);

            // Legitimacy grows slowly under a sitting leader.
            if (faction.leader != null)
                s.legitimacy += (85f - s.legitimacy) * 0.01f;
            else
                s.legitimacy -= 0.5f;
            s.legitimacy = Mathf.Clamp(s.legitimacy, 0f, 100f);

            // Stability drifts toward a target set by legitimacy, economy, wars and government.
            float target = 55f
                           + (s.legitimacy - 50f) * 0.3f
                           + (s.economyIndex - 100f) * 0.15f
                           - s.warWeariness * 0.4f
                           - s.activeWars * 4f
                           + GovernmentUtility.StabilityBonus(s.government)
                           - s.Bloc(Bloc.Reformists) * 10f
                           + (s.capital == null ? -15f : 0f)
                           + IntegrationRegistry.Sum(i => i.StabilityModifier(faction));
            target = Mathf.Clamp(target, 0f, 100f);
            s.stability += (target - s.stability) * 0.05f * settings.stabilityDriftSpeed + Rand.Range(-1f, 1f);
            s.stability = Mathf.Clamp(s.stability, 0f, 100f);

            DriftBlocs(s, faction);
            s.effectiveAggression = Mathf.Clamp01(s.personality.aggression * (0.6f + s.Bloc(Bloc.Hawks)) - s.Bloc(Bloc.Doves) * 0.3f);

            s.lowStabilityDays = s.stability < 10f ? s.lowStabilityDays + 1 : 0;

            if (settings.politicalEventsEnabled)
                RollUnrest(s, faction, now, hawks);
        }

        private static void Initialize(FactionState s, Faction faction)
        {
            s.personality.Refresh(faction);
            s.government = GovernmentUtility.Determine(faction);
            GovernmentUtility.InitializeBlocs(s);
            Rand.PushState(faction.loadID * 31 + 5);
            try
            {
                s.stability = Rand.Range(50f, 75f);
                s.legitimacy = Rand.Range(50f, 80f);
            }
            finally
            {
                Rand.PopState();
            }
            s.lastKnownLeader = faction.leader;
            s.leaderSinceTick = Find.TickManager.TicksGame;
            // Spread the AI's first moves out instead of every faction acting on day one.
            s.nextAiActionTick = Find.TickManager.TicksGame + Rand.Range(3, 20) * GenDate.TicksPerDay;
            s.governmentInitialized = true;
        }

        private static void DriftBlocs(FactionState s, Faction faction)
        {
            AIPersonality p = s.personality;
            float[] target =
            {
                0.15f + p.aggression * 0.2f + (s.activeWars > 0 ? 0.1f : 0f) - s.warWeariness / 400f,
                0.15f + (1f - p.aggression) * 0.15f + s.warWeariness / 250f,
                0.10f + p.greed * 0.2f + (s.economyIndex - 100f) / 500f,
                GovernmentUtility.ZealotBase(faction),
                0.05f + Mathf.Max(0f, 60f - s.stability) / 200f + (100f - s.legitimacy) / 400f,
            };
            float sum = 0f;
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = Mathf.Max(0.01f, target[i]);
                sum += target[i];
            }
            for (int i = 0; i < FactionState.BlocCount; i++)
                s.blocs[i] += (target[i] / sum - s.blocs[i]) * 0.05f;
            s.NormalizeBlocs();
        }

        private static void RollUnrest(FactionState s, Faction faction, int now, float hawks)
        {
            int day = GenDate.TicksPerDay;

            if (s.lowStabilityDays >= RevolutionAfterLowDays && now - s.lastCoupTick > RevolutionCooldownDays * day && Rand.Chance(0.15f))
            {
                EventBus.Raise(DominionEventDefOf.Dominion_Revolution, faction);
                return;
            }
            if (s.stability < 20f && s.legitimacy < 40f && now - s.lastCoupTick > CoupCooldownDays * day && Rand.Chance(0.03f + hawks * 0.05f))
            {
                EventBus.Raise(DominionEventDefOf.Dominion_Coup, faction);
                return;
            }
            if (s.stability < 30f && now - s.lastRiotTick > RiotCooldownDays * day && Rand.Chance((30f - s.stability) / 100f * 0.3f))
                EventBus.Raise(DominionEventDefOf.Dominion_Riots, faction);
        }

        public override void OnEvent(DominionEvent ev)
        {
            Faction f = ev.primary;
            if (f == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            int now = Find.TickManager.TicksGame;
            DominionEventDef def = ev.def;

            // Effects on the other side apply even when the player is the one acting.
            Faction g = ev.secondary;
            bool gSimulated = g != null && DominionScheduler.IsSimulated(g);
            if (def == DominionEventDefOf.Dominion_WarDeclared && gSimulated)
            {
                // Rally around the flag for the side that was attacked.
                FactionState defender = comp.GetState(g);
                defender.stability = Mathf.Min(100f, defender.stability + 3f);
            }
            else if (def == DominionEventDefOf.Dominion_PeaceMade && gSimulated)
            {
                FactionState other = comp.GetState(g);
                other.warWeariness = Mathf.Max(0f, other.warWeariness - 20f);
            }

            // Only factions the sim tracks; never create state for the player or hidden factions on every event.
            if (!DominionScheduler.IsSimulated(f) || !comp.TryGetState(f, out FactionState s)) return;

            if (def == DominionEventDefOf.Dominion_LeaderDied)
            {
                s.legitimacy = Mathf.Min(s.legitimacy, 35f);
                s.stability = Mathf.Max(0f, s.stability - 8f * ev.magnitude);
            }
            else if (def == DominionEventDefOf.Dominion_Riots)
            {
                s.lastRiotTick = now;
                s.stability = Mathf.Max(0f, s.stability - 2f * ev.magnitude);
                s.legitimacy = Mathf.Max(0f, s.legitimacy - 4f * ev.magnitude);
                EconomyEffects.Shock(f, -0.03f * ev.magnitude);
                s.blocs[(int)Bloc.Reformists] += 0.03f;
                s.NormalizeBlocs();
            }
            else if (def == DominionEventDefOf.Dominion_Coup)
            {
                ReplaceLeader(f);
                s.government = GovernmentUtility.AfterCoup(s.government);
                s.stability = Mathf.Max(s.stability + 20f, 35f);
                s.legitimacy = 30f;
                s.warWeariness *= 0.5f;
                s.blocs[(int)Bloc.Hawks] += 0.2f;
                s.NormalizeBlocs();
                s.lastCoupTick = now;
                s.lowStabilityDays = 0;
                s.lastKnownLeader = f.leader;
                s.leaderSinceTick = now;
            }
            else if (def == DominionEventDefOf.Dominion_Revolution)
            {
                ReplaceLeader(f);
                s.government = GovernmentUtility.AfterRevolution(s.government);
                s.stability = 50f;
                s.legitimacy = 55f;
                s.SetBlocs(0.15f, 0.2f, 0.15f, GovernmentUtility.ZealotBase(f), 0.4f);
                s.lastCoupTick = now;
                s.lowStabilityDays = 0;
                s.lastKnownLeader = f.leader;
                s.leaderSinceTick = now;
            }
            else if (def == DominionEventDefOf.Dominion_CapitalLost)
            {
                s.stability = Mathf.Max(0f, s.stability - 20f * ev.magnitude);
                s.legitimacy = Mathf.Max(0f, s.legitimacy - 15f * ev.magnitude);
            }
            else if (def == DominionEventDefOf.Dominion_PeaceMade)
            {
                s.warWeariness = Mathf.Max(0f, s.warWeariness - 20f);
            }
        }

        /// <summary>Installs a new leader. The old one is not killed; they stay alive as a deposed world pawn.</summary>
        public static void ReplaceLeader(Faction f)
        {
            if (f.IsPlayer) return;
            Pawn old = f.leader;
            f.leader = null;
            if (!f.TryGenerateNewLeader())
                f.leader = old;
        }
    }
}
