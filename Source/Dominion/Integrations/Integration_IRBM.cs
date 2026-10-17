using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Where the world stands with InterRim Ballistic Missile's doomsday clock.</summary>
    public enum NuclearPhase { Calm, Clock, War, Winter }

    /// <summary>
    /// InterRim Ballistic Missile, fully wired in and respecting its settings:
    /// <list type="bullet">
    /// <item>Missile strikes are acts of war (war score, stability, economy, population, grievance; strategic weapons
    /// outrage everyone).</item>
    /// <item>The doomsday clock's phases reach every faction: a clock ticking unsettles the world in step with how close
    /// it is to midnight; a global nuclear war wrecks stability and economies and <b>disbands the Rim Council for
    /// good</b>; the nuclear winter after it starves populations until it lifts. Each phase is news.</item>
    /// <item>AI launches respect diplomacy: no strike on a faction you have a non-aggression pact or alliance with, and
    /// none by a member bound by the council's non-proliferation decree.</item>
    /// <item>Arsenals count: strategic powers weigh more in the council and paranoid neighbours fear them; your loaded
    /// silos do the same for you. The dossier shows each faction's missile capability.</item>
    /// <item>Satellites over a faction's capital raise your intel on it.</item>
    /// </list>
    /// </summary>
    public class Integration_IRBM : DominionIntegration
    {
        public override string PackageId => "kazepsi.irbm";
        public override string Label => "InterRim Ballistic Missile";

        private PropertyInfo registryInstance;
        private MethodInfo isTileObserved;
        private static Type clockType;
        private static FieldInfo clockHours, isTriggered, isWarActive, isWinterActive;
        private static MethodInfo getInventory, notifyStrategicImpact, resetClock, triggerWar, endWar, endWinter;
        private static FieldInfo settingsField;
        private static MethodInfo hasInventory, hasLaunchCapability, launchMissile;
        private static Type siloType, satelliteType;

        /// <summary>Non-null when IRBM is loaded and bound.</summary>
        public static Integration_IRBM Instance
        {
            get
            {
                Integration_IRBM i = IntegrationRegistry.Get<Integration_IRBM>();
                return i != null && i.IsActive ? i : null;
            }
        }

        /// <summary>Set by the council: whether a decree forbids this faction from using missiles.</summary>
        public static Func<Faction, bool> NonProliferationBinds;

        protected internal override void Bind(Binder b)
        {
            Type registry = b.Type("IRBM.SatelliteRegistry");
            registryInstance = b.Property(registry, "Instance");
            isTileObserved = b.Method(registry, "IsTileObserved", new[] { typeof(int) });
            clockType = b.Type("IRBM.DoomsdayClockManager");
            isTriggered = b.Field(clockType, "isTriggered");
            clockHours = b.Field(clockType, "clockHours");
            isWarActive = b.Field(clockType, "isNuclearWarActive");
            isWinterActive = b.Field(clockType, "isNuclearWinterActive");

            // Optional parts: each one missing only switches off what needs it.
            getInventory = AccessTools.Method(clockType, "GetInventory", new[] { typeof(Faction) });
            notifyStrategicImpact = AccessTools.Method(clockType, "Notify_StrategicImpact");
            resetClock = AccessTools.Method(clockType, "ResetClock");
            triggerWar = AccessTools.Method(clockType, "TriggerNuclearWar");
            endWar = AccessTools.Method(clockType, "EndNuclearWar");
            endWinter = AccessTools.Method(clockType, "EndNuclearWinterStatus");
            settingsField = AccessTools.Field(AccessTools.TypeByName("IRBM.IRBM_Mod"), "settings");
            Type ai = AccessTools.TypeByName("IRBM.AIFactionMissileUtility");
            hasInventory = AccessTools.Method(ai, "HasMissileInventoryForCategory", new[] { typeof(Faction), typeof(string) });
            hasLaunchCapability = AccessTools.Method(ai, "HasAnyLaunchCapability", new[] { typeof(Faction) });
            launchMissile = AccessTools.Method(ai, "LaunchMissile");
            siloType = AccessTools.TypeByName("IRBM.Building_MissileSilo");
            satelliteType = AccessTools.TypeByName("IRBM.WorldObject_Satellite");
            missileType = AccessTools.TypeByName("IRBM.WorldObject_Missile");
            onArrived = missileType?.GetMethod("OnArrived", BindingFlags.Public | BindingFlags.Instance);

            b.Note(onArrived != null ? "missile strikes: WorldObject_Missile.OnArrived" : "missile strike hook not found");
            b.Note(launchMissile != null ? "AI launches checked against treaties and decrees: AIFactionMissileUtility.LaunchMissile" : "AI launch hook not found");
            b.Note(triggerWar != null ? "global nuclear war: DoomsdayClockManager.TriggerNuclearWar" : "nuclear war hook not found (checked daily instead)");
            b.Note(settingsField != null ? "settings: IRBM_Mod.settings" : "settings not found (defaults assumed)");
        }

        private static Type missileType;
        private MethodInfo onArrived;

        protected internal override void OnActivated()
        {
            TryPatch(onArrived, prefix: nameof(MissileArrivedPrefix));
            TryPatch(launchMissile, prefix: nameof(LaunchMissilePrefix));
            TryPatch(triggerWar, postfix: nameof(PhaseChangedPostfix));
            TryPatch(endWar, postfix: nameof(PhaseChangedPostfix));
            TryPatch(endWinter, postfix: nameof(PhaseChangedPostfix));
        }

        private static void TryPatch(MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null) return;
            try
            {
                DominionMod.Harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(Integration_IRBM), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(Integration_IRBM), postfix) : null);
            }
            catch (Exception e)
            {
                DLog.Warning($"Could not hook IRBM {target.Name}: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- settings and state

        /// <summary>One of IRBM's settings, or its default when the settings cannot be read.</summary>
        public static T Setting<T>(string name, T fallback)
        {
            object s = settingsField?.GetValue(null);
            if (s == null) return fallback;
            object v = Traverse.Create(s).Field(name).GetValue();
            return v is T t ? t : fallback;
        }

        private static object Clock => clockType == null ? null : Current.Game?.components?.Find(c => clockType.IsInstanceOfType(c));

        /// <summary>The doomsday clock's phase now.</summary>
        public static NuclearPhase Phase
        {
            get
            {
                object clock = Clock;
                if (clock == null) return NuclearPhase.Calm;
                if ((bool)isWinterActive.GetValue(clock)) return NuclearPhase.Winter;
                if ((bool)isWarActive.GetValue(clock)) return NuclearPhase.War;
                return (bool)isTriggered.GetValue(clock) ? NuclearPhase.Clock : NuclearPhase.Calm;
            }
        }

        /// <summary>How far the clock has run toward midnight, 0..1 (12 hours is a global nuclear war).</summary>
        public static float ClockFraction
        {
            get
            {
                object clock = Clock;
                return clock == null ? 0f : Mathf.Clamp01((float)clockHours.GetValue(clock) / 12f);
            }
        }

        /// <summary>Warheads a faction has left in a nuclear war (IRBM only counts them during one), or -1.</summary>
        public static int WarheadsLeft(Faction f)
        {
            object clock = Clock;
            if (clock == null || getInventory == null || Phase != NuclearPhase.War) return -1;
            return (int)getInventory.Invoke(clock, new object[] { f });
        }

        private static readonly UICache<Faction, int> capability = new UICache<Faction, int>(ComputeCapability, 30f);

        /// <summary>0 none, 1 tactical missiles, 2 strategic (nuclear) weapons.</summary>
        public static int Capability(Faction f) => f == null ? 0 : capability.Get(f);

        private static int ComputeCapability(Faction f)
        {
            if (f.IsPlayer) return PlayerStrategicMissiles() > 0 ? 2 : PlayerMissiles() > 0 ? 1 : 0;
            try
            {
                if (hasLaunchCapability != null && !(bool)hasLaunchCapability.Invoke(null, new object[] { f })) return 0;
                if (hasInventory == null) return 0;
                if ((bool)hasInventory.Invoke(null, new object[] { f, "Strategic" })) return 2;
                return (bool)hasInventory.Invoke(null, new object[] { f, "Tactical" }) ? 1 : 0;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("IRBM capability check failed: " + e.Message, 0x1B32);
                return 0;
            }
        }

        public static string CapabilityLabel(Faction f)
        {
            int c = Capability(f);
            string label = ("Dominion_IRBM_Capability_" + c).Translate();
            int left = WarheadsLeft(f);
            if (left >= 0 && !f.IsPlayer) label += "  ·  " + "Dominion_IRBM_WarheadsLeft".Translate(left);
            int sats = Satellites(f);
            if (sats > 0) label += "  ·  " + "Dominion_IRBM_Satellites".Translate(sats);
            if (f.IsPlayer) label += "  ·  " + "Dominion_IRBM_Silos".Translate(PlayerMissiles(), PlayerStrategicMissiles());
            return label;
        }

        public static int Satellites(Faction f)
        {
            if (satelliteType == null) return 0;
            int n = 0;
            foreach (WorldObject o in Find.WorldObjects.AllWorldObjects)
                if (o.Faction == f && satelliteType.IsInstanceOfType(o)) n++;
            return n;
        }

        private static IEnumerable<object> PlayerLoadedMissiles()
        {
            if (siloType == null) yield break;
            foreach (Map m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                foreach (Building b in m.listerBuildings.allBuildingsColonist)
                {
                    if (!siloType.IsInstanceOfType(b)) continue;
                    if (Traverse.Create(b).Field("loadedMissiles").GetValue() is IEnumerable loaded)
                        foreach (object missile in loaded)
                            if (missile != null) yield return missile;
                }
            }
        }

        public static int PlayerMissiles() => PlayerLoadedMissiles().Count();

        public static int PlayerStrategicMissiles() =>
            PlayerLoadedMissiles().Count(m => Traverse.Create(Traverse.Create(m).Field("warheadDef").GetValue()).Field("category").GetValue<string>() == "Strategic");

        // ---------------------------------------------------------------- strikes

        /// <summary>
        /// An AI faction is about to fire. Interceptions and satellite work always go ahead; a strike on a faction it
        /// has a non-aggression pact or alliance with, or by a member bound by non-proliferation, is called off.
        /// </summary>
        public static bool LaunchMissilePrefix(Faction faction, PlanetTile targetTile, object targetSatellite, object targetMissile, MapParent targetMapParent)
        {
            if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null || faction == null) return true;
            if (targetSatellite != null || targetMissile != null) return true;
            try
            {
                MapParent target = targetMapParent ?? (targetTile.Valid ? Find.WorldObjects.MapParentAt(targetTile) : null);
                Faction victim = target?.Faction;
                if (victim == null || victim == faction) return true;
                string why = null;
                if (WorldComponent_Dominion.Instance.Treaties.AnyNonAggression(faction, victim)) why = "Dominion_IRBM_StandDown_Treaty";
                else if (NonProliferationBinds != null && NonProliferationBinds(faction)) why = "Dominion_IRBM_StandDown_Decree";
                if (why == null) return true;
                if (victim.IsPlayer || IntelUtility.IntelOn(faction) >= IntelUtility.RestrictedIntel)
                    Messages.Message(why.Translate(faction.Name, victim.Name), MessageTypeDefOf.NeutralEvent, historical: true);
                return false;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("IRBM launch check failed: " + e.Message, 0x1B33);
                return true;
            }
        }

        /// <summary>
        /// A missile is about to hit a settlement or site: an act of war against its owner (and war if they were at peace),
        /// war score, losses in people and wealth, and a world that thinks less of whoever uses strategic weapons.
        /// </summary>
        public static void MissileArrivedPrefix(WorldObject __instance)
        {
            if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
            try
            {
                Traverse t = Traverse.Create(__instance);
                if (t.Field("isAntiAir").GetValue<bool>() || t.Field("targetSatellite").GetValue() != null || t.Field("targetMissile").GetValue() != null) return;
                Faction attacker = __instance.Faction;
                PlanetTile tile = t.Field("destinationTile").GetValue<PlanetTile>();
                MapParent target = t.Field("targetMapParent").GetValue<MapParent>() ?? (tile.Valid ? Find.WorldObjects.MapParentAt(tile) : null);
                Faction victim = target?.Faction;
                if (attacker == null || victim == null || attacker == victim || victim.Hidden) return;
                object warhead = Traverse.Create(t.Property("MissileComp").GetValue()).Field("warheadDef").GetValue();
                string category = warhead != null ? Traverse.Create(warhead).Field("category").GetValue<string>() : null;
                // Only warheads that do damage count: satellites, recon and anti-air payloads are not strikes.
                if (category != null && category != "Tactical" && category != "Strategic") return;
                bool strategic = category == "Strategic" || warhead != null && Traverse.Create(warhead).Field("doomsdayAdvanceMinutes").GetValue<float>() > 0f;
                MissileStrike(attacker, victim, target, strategic);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("IRBM missile hook failed: " + e.Message, 0x1B31);
            }
        }

        /// <summary>Last strike tick per (attacker, victim): a salvo is one act of war, not ten.</summary>
        private static readonly Dictionary<long, int> lastStrike = new Dictionary<long, int>();

        public static void MissileStrike(Faction attacker, Faction victim, MapParent target, bool strategic)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            long key = ((long)attacker.loadID << 32) | (uint)victim.loadID;
            int now = Find.TickManager.TicksGame;
            if (lastStrike.TryGetValue(key, out int last) && now - last < GenDate.TicksPerDay && now >= last)
            {
                // Further missiles of the same salvo: a little more damage, no new diplomatic fallout.
                WarUtility.AddScore(attacker, victim, strategic ? 4f : 1f);
                if (!victim.IsPlayer && DominionScheduler.IsSimulated(victim))
                    EconomyEffects.Shock(victim, strategic ? -0.03f : -0.01f);
                return;
            }
            lastStrike[key] = now;
            if (!attacker.HostileTo(victim) && WarUtility.CanWage(attacker) && WarUtility.CanWage(victim))
                DiplomacyUtility.SetRelationKind(attacker, victim, FactionRelationKind.Hostile, attacker, "Dominion_Reason_MissileStrike".Translate());
            WarUtility.AddScore(attacker, victim, strategic ? 25f : 8f);
            if (!victim.IsPlayer && DominionScheduler.IsSimulated(victim))
            {
                FactionState s = comp.GetState(victim);
                s.stability = Mathf.Max(0f, s.stability - (strategic ? 12f : 4f));
                EconomyEffects.Shock(victim, strategic ? -0.15f : -0.04f);
                IntegrationRegistry.Get<Integration_EconomicsDemography>()?.ModifyPopulation(victim,
                    -Mathf.Clamp(Mathf.RoundToInt(s.population * (strategic ? 0.08f : 0.01f)), 1, strategic ? 120 : 15));
            }
            comp.GetRelation(victim, attacker).grievance += strategic ? 60f : 20f;
            if (strategic)
                // Nobody likes the faction that used a weapon of mass destruction, whoever it was aimed at.
                foreach (Faction other in comp.Cache.Simulated)
                    if (other != attacker && other != victim) comp.GetRelation(other, attacker).favour -= 10f + 10f * comp.GetState(other).personality.honour;
            EventBus.Raise(DominionEventDefOf.Dominion_MissileStrike, attacker, victim, target?.Tile.tileId ?? -1, detail: target?.Label ?? victim.Name,
                magnitude: strategic ? 2f : 1f, source: "kazepsi.irbm");
        }

        // ---------------------------------------------------------------- the doomsday clock's phases

        private static NuclearPhase lastPhase;
        private static bool phaseKnown;

        protected internal override void OnWorldLoaded()
        {
            phaseKnown = false;
            lastStrike.Clear();
        }

        public static void PhaseChangedPostfix() => CheckPhase();

        public override void DailyGlobal() => CheckPhase();

        /// <summary>Notices a change of phase (immediately through the hooks, or at the latest the next day) and acts on it.</summary>
        private static void CheckPhase()
        {
            if (WorldComponent_Dominion.Instance == null) return;
            NuclearPhase now = Phase;
            if (!phaseKnown)
            {
                // On load, the world is simply as it is; but a council that outlived a nuclear war is still ended.
                phaseKnown = true;
                lastPhase = now;
                if (now >= NuclearPhase.War) DisbandCouncil();
                return;
            }
            if (now == lastPhase) return;
            NuclearPhase was = lastPhase;
            lastPhase = now;
            switch (now)
            {
                case NuclearPhase.Clock:
                    EventBus.Raise(DominionEventDefOf.Dominion_DoomsdayClock, null, null, detail: ClockText(), source: "kazepsi.irbm");
                    break;
                case NuclearPhase.War:
                    NuclearWarBegins();
                    break;
                case NuclearPhase.Winter:
                    EventBus.Raise(DominionEventDefOf.Dominion_NuclearWinter, null, null, detail: Setting("nuclearWinterDurationYears", 10).ToString(), source: "kazepsi.irbm");
                    break;
                case NuclearPhase.Calm when was == NuclearPhase.Winter:
                    EventBus.Raise(DominionEventDefOf.Dominion_NuclearWinterEnds, null, null, source: "kazepsi.irbm");
                    break;
            }
        }

        public static string ClockText()
        {
            float minutesLeft = (1f - ClockFraction) * 720f;
            return "Dominion_IRBM_ClockText".Translate(Mathf.FloorToInt(minutesLeft / 60f), Mathf.FloorToInt(minutesLeft % 60f).ToString("00"));
        }

        /// <summary>
        /// The bombs fall everywhere. Every simulated faction loses stability and much of its economy, the strategic powers
        /// earn the world's hatred, and the Rim Council, which could not stop it, is dissolved for good.
        /// </summary>
        private static void NuclearWarBegins()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in comp.Cache.Simulated)
            {
                FactionState s = comp.GetState(f);
                s.stability = Mathf.Max(0f, s.stability - 20f);
                EconomyEffects.Shock(f, -0.25f);
                foreach (Faction other in comp.Cache.Simulated)
                    if (other != f && Capability(other) == 2) comp.GetRelation(f, other).favour -= 15f;
                // IRBM can make every faction hate the player when the war starts; Dominion's opinions follow suit.
                if (Setting("reduceGoodwillInNuclearWar", true)) comp.GetRelation(f, Faction.OfPlayer).favour -= 30f;
            }
            EventBus.Raise(DominionEventDefOf.Dominion_NuclearWar, null, null, source: "kazepsi.irbm");
            DisbandCouncil();
        }

        private static void DisbandCouncil()
        {
            if (!WorldComponent_Dominion.Instance.Council.disbanded)
                CouncilSystem.DisbandPermanently("Dominion_IRBM_DisbandReason".Translate());
        }

        // ---------------------------------------------------------------- ongoing effects

        public override float IntelModifier(Faction f)
        {
            object reg = registryInstance?.GetValue(null);
            if (reg == null) return 0f;
            PlanetTile tile = DelegationUtility.HomeTile(f);
            return tile.Valid && (bool)isTileObserved.Invoke(reg, new object[] { tile.tileId }) ? 15f : 0f;
        }

        public override float StabilityModifier(Faction f)
        {
            switch (Phase)
            {
                // The closer to midnight, the more unsettled; only if IRBM's clock is switched on.
                case NuclearPhase.Clock: return Setting("enableDoomsdayClock", true) ? -(4f + 8f * ClockFraction) : 0f;
                case NuclearPhase.War: return -20f;
                case NuclearPhase.Winter: return -10f;
                default: return 0f;
            }
        }

        public override float EconomyModifier(Faction f)
        {
            switch (Phase)
            {
                case NuclearPhase.War: return -0.2f;
                case NuclearPhase.Winter: return -0.15f;
                default: return 0f;
            }
        }

        /// <summary>Strategic powers carry weight in the council; tactical ones a little.</summary>
        public override float VoteWeightModifier(Faction f)
        {
            int c = Capability(f);
            return c == 2 ? 0.6f : c == 1 ? 0.2f : 0f;
        }

        /// <summary>A paranoid faction fears a neighbour with nuclear weapons, unless they are allies.</summary>
        public override float OpinionModifier(Faction f, Faction toward)
        {
            if (Capability(toward) < 2 || f.RelationKindWith(toward) == FactionRelationKind.Ally) return 0f;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            return comp != null && comp.TryGetState(f, out FactionState s) ? -8f * s.personality.paranoia : 0f;
        }

        /// <summary>The winter kills: populations shrink a little every day while it lasts.</summary>
        public static void WinterDay()
        {
            if (Phase != NuclearPhase.Winter) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Integration_EconomicsDemography ed = IntegrationRegistry.Get<Integration_EconomicsDemography>();
            if (ed == null || !ed.IsActive) return;
            foreach (Faction f in comp.Cache.Simulated)
            {
                FactionState s = comp.GetState(f);
                ed.ModifyPopulation(f, -Mathf.Max(1, Mathf.RoundToInt(s.population * 0.002f)));
            }
        }

        // ---------------------------------------------------------------- god mode

        /// <summary>Advances the doomsday clock by this many minutes, as a strategic strike would.</summary>
        public static void DevAdvanceClock(float minutes) => notifyStrategicImpact?.Invoke(Clock, new object[] { minutes });

        public static void DevResetClock() => resetClock?.Invoke(Clock, null);

        public static void DevStartNuclearWar()
        {
            if (Clock == null) return;
            if (triggerWar != null) triggerWar.Invoke(Clock, null);
            else DevAdvanceClock(720f);
        }

        public static void DevEndNuclearWar() => endWar?.Invoke(Clock, null);

        public static void DevEndWinter() => endWinter?.Invoke(Clock, null);

        public static bool DevAvailable => Clock != null;
    }

    /// <summary>The nuclear winter's toll, once a day (Dominion's systems run daily; integrations only get DailyGlobal).</summary>
    public class NuclearWinterSystem : SimSystem
    {
        public override int Order => 97;

        public override void DailyGlobal()
        {
            if (Integration_IRBM.Instance != null) Integration_IRBM.WinterDay();
        }
    }
}
