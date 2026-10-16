using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Pillage &amp; Plunder. Every plunder, by the player or between factions, is a war act Dominion hears about at once:
    /// news, war score, loot moving between economies (E&amp;D), and a damaged economy while the town recovers. Dominion
    /// warbands that beat a faction's last stronghold sack it through P&amp;P instead of taking it.
    /// </summary>
    public class Integration_PillageAndPlunder : DominionIntegration
    {
        public override string PackageId => "lordfelix.pillageandplunder";
        public override string Label => "Pillage & Plunder";

        private PropertyInfo instance;
        private MethodInfo isPlundered;
        private MethodInfo tryNpcPlunder;
        private MethodInfo notifyPlundered;
        private readonly Dictionary<int, int> recovering = new Dictionary<int, int>();

        /// <summary>Set while P&amp;P runs an NPC plunder, so the notification that follows is not taken for the player's.</summary>
        [ThreadStatic] private static Faction npcAttacker;

        protected internal override void OnWorldLoaded() => recovering.Clear();

        protected internal override void Bind(Binder b)
        {
            Type manager = b.Type("PillageAndPlunder.PlunderedSettlementManager");
            instance = b.Property(manager, "Instance");
            isPlundered = b.Method(manager, "IsSettlementPlundered", new[] { typeof(Settlement) });
            tryNpcPlunder = b.Method(manager, "TryNpcPlunder", new[] { typeof(Faction), typeof(Settlement) });
            notifyPlundered = b.Method(manager, "Notify_SettlementPlundered", new[] { typeof(Settlement), typeof(int) });
        }

        protected internal override void OnActivated()
        {
            try
            {
                DominionMod.Harmony.Patch(tryNpcPlunder,
                    prefix: new HarmonyLib.HarmonyMethod(typeof(Integration_PillageAndPlunder), nameof(NpcPlunderPrefix)),
                    postfix: new HarmonyLib.HarmonyMethod(typeof(Integration_PillageAndPlunder), nameof(NpcPlunderPostfix)));
                DominionMod.Harmony.Patch(notifyPlundered,
                    postfix: new HarmonyLib.HarmonyMethod(typeof(Integration_PillageAndPlunder), nameof(PlunderedPostfix)));
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook Pillage & Plunder: " + e.Message);
            }
        }

        public static void NpcPlunderPrefix(Faction attacker) => npcAttacker = attacker;

        public static void NpcPlunderPostfix(Faction attacker, Settlement victim, bool __result)
        {
            npcAttacker = null;
            if (__result) Safe(() => Plundered(attacker, victim));
        }

        public static void PlunderedPostfix(Settlement settlement)
        {
            // NPC plunders are reported by the postfix above, once the attacker is known.
            if (npcAttacker != null) return;
            Safe(() => Plundered(Faction.OfPlayerSilentFail, settlement));
        }

        private static void Safe(Action a)
        {
            if (Current.ProgramState != ProgramState.Playing || WorldComponent_Dominion.Instance == null) return;
            try { a(); }
            catch (Exception e) { DLog.WarningOnce("Pillage & Plunder hook failed: " + e.Message, 0x7A9D); }
        }

        /// <summary>A settlement was plundered by <paramref name="attacker"/>.</summary>
        private static void Plundered(Faction attacker, Settlement s)
        {
            Faction victim = s?.Faction;
            if (victim == null || attacker == null || victim == attacker || victim.IsPlayer) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState vs = comp.GetState(victim);
            vs.stability = Mathf.Max(0f, vs.stability - 3f);
            EconomyEffects.Shock(victim, -0.04f);
            // The loot: a share of the town's wealth goes to the attacker (the player's comes from the map itself).
            int loot = Mathf.RoundToInt(Mathf.Clamp(vs.wealth * 0.03f / Mathf.Max(1, vs.settlementCount), 100f, 2000f));
            if (attacker.IsPlayer) EconomyEffects.DestroyGoods(victim, loot);
            else EconomyEffects.Transfer(victim, attacker, loot);
            comp.GetRelation(victim, attacker).grievance += 20f;
            WarUtility.AddScore(attacker, victim, 12f);
            EventBus.Raise(DominionEventDefOf.Dominion_SettlementLooted, victim, attacker, s.Tile.tileId, detail: s.Label,
                source: "lordfelix.pillageandplunder");
        }

        /// <summary>A Dominion warband sacks a settlement it cannot hold. True if P&amp;P allowed it.</summary>
        public bool TryPlunder(Faction attacker, Settlement victim)
        {
            object m = IsActive ? instance?.GetValue(null) : null;
            if (m == null || attacker == null || victim == null) return false;
            try { return (bool)tryNpcPlunder.Invoke(m, new object[] { attacker, victim }); }
            catch (Exception e)
            {
                DLog.WarningOnce("Pillage & Plunder plunder failed: " + e.Message, 0x7A9E);
                return false;
            }
        }

        public override void DailyGlobal()
        {
            object m = instance?.GetValue(null);
            if (m == null) return;
            recovering.Clear();
            foreach (Settlement s in Find.WorldObjects.Settlements)
            {
                if (s.Faction == null || s.Faction.IsPlayer || !(bool)isPlundered.Invoke(m, new object[] { s })) continue;
                recovering[s.Faction.loadID] = (recovering.TryGetValue(s.Faction.loadID, out int n) ? n : 0) + 1;
            }
        }

        /// <summary>Towns still recovering from a plunder hold the economy back.</summary>
        public override float EconomyModifier(Faction f) => recovering.TryGetValue(f.loadID, out int n) ? -0.05f * n : 0f;
    }
}
