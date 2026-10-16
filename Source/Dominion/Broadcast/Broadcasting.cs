using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>A programme a Broadcast Center can air. Effects are in the worker; text and numbers here.</summary>
    public class BroadcastProgramDef : Def
    {
        public Type workerClass;

        /// <summary>The programme is aimed at one faction.</summary>
        public bool needsTarget;

        /// <summary>Mood memory for colonists watching TV while this programme is on air.</summary>
        public ThoughtDef tvThought;

        public int order;

        [Unsaved] private BroadcastProgramWorker workerInt;

        public BroadcastProgramWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (BroadcastProgramWorker)Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                return workerInt;
            }
        }
    }

    /// <summary>Applies a programme's daily effect. <paramref name="airtime"/> is skill-weighted hours on air today.</summary>
    public abstract class BroadcastProgramWorker
    {
        public BroadcastProgramDef def;

        public abstract void ApplyDaily(float airtime, Faction target);
    }

    /// <summary>Airtime accumulated today per programme (and target), plus what is currently on TV. Saved.</summary>
    public class BroadcastState : IExposable
    {
        private Dictionary<string, float> airtime = new Dictionary<string, float>();
        public BroadcastProgramDef onAir;
        public int onAirUntil = -1;
        public int counterProgrammingUntil = -1;

        public static string Key(BroadcastProgramDef def, Faction target) => def.defName + ":" + (target?.loadID ?? -1);

        public void AddAirtime(BroadcastProgramDef def, Faction target, float hours)
        {
            string key = Key(def, target);
            airtime[key] = (airtime.TryGetValue(key, out float v) ? v : 0f) + hours;
            onAir = def;
            onAirUntil = Find.TickManager.TicksGame + GenDate.TicksPerHour * 6;
        }

        public IEnumerable<KeyValuePair<string, float>> Airtime => airtime;

        public void ResetDay() => airtime.Clear();

        public BroadcastProgramDef OnAirNow => Find.TickManager.TicksGame < onAirUntil ? onAir : null;

        public bool CounterProgrammingActive => Find.TickManager.TicksGame < counterProgrammingUntil;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref airtime, "airtime", LookMode.Value, LookMode.Value);
            Scribe_Defs.Look(ref onAir, "onAir");
            Scribe_Values.Look(ref onAirUntil, "onAirUntil", -1);
            Scribe_Values.Look(ref counterProgrammingUntil, "counterProgrammingUntil", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) airtime ??= new Dictionary<string, float>();
        }
    }

    /// <summary>Once a day, turns the day's airtime into effects, then starts a new day.</summary>
    public class BroadcastSystem : SimSystem
    {
        public override int Order => 55;

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            BroadcastState b = comp.Broadcast;
            foreach (KeyValuePair<string, float> kv in b.Airtime.ToList())
            {
                int sep = kv.Key.LastIndexOf(':');
                BroadcastProgramDef def = DefDatabase<BroadcastProgramDef>.GetNamedSilentFail(kv.Key.Substring(0, sep));
                if (def == null) continue;
                int.TryParse(kv.Key.Substring(sep + 1), out int targetId);
                Faction target = targetId >= 0 ? comp.Cache.FactionById(targetId) : null;
                if (def.needsTarget && target == null) continue;
                def.Worker.ApplyDaily(kv.Value, target);
            }
            b.ResetDay();

            // Cultural influence fades, and grows a little through trade agreements and alliances.
            Faction player = Faction.OfPlayerSilentFail;
            foreach (Faction f in comp.Cache.Simulated)
            {
                FactionState s = comp.GetState(f);
                float growth = 0f;
                if (player != null)
                    foreach (Treaty t in comp.Treaties.All)
                        if (t.Between(f, player)) growth += t.def.alliance ? 0.3f : 0.15f;
                s.playerCulture = Mathf.Clamp(s.playerCulture * 0.99f + growth, 0f, 100f);
            }
        }

        public static void AddCulture(Faction f, float amount)
        {
            FactionState s = WorldComponent_Dominion.Instance.GetState(f);
            s.playerCulture = Mathf.Clamp(s.playerCulture + amount, 0f, 100f);
        }
    }

    // ------------------------------------------------------------------ programme workers

    /// <summary>News bulletins: colonists feel informed, and the broadcast network adds to radio coverage.</summary>
    public class Program_News : BroadcastProgramWorker
    {
        public override void ApplyDaily(float airtime, Faction target)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.radioCoverage = Mathf.Min(100f, comp.radioCoverage + airtime * 1.5f);
            if (airtime >= 2f) BroadcastUtility.GiveColonistsThought(DominionThoughtDefOf.Dominion_Broadcast_Informed);
        }
    }

    /// <summary>Your culture on the airwaves: influence on every faction within reach, strongest nearby.</summary>
    public class Program_Cultural : BroadcastProgramWorker
    {
        public override void ApplyDaily(float airtime, Faction target)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (Faction f in comp.Cache.Simulated)
            {
                RelationState r = comp.TryGetRelation(f, Faction.OfPlayer);
                if (r == null || r.distance < 0f || r.distance > BroadcastUtility.ReachTiles) continue;
                float reach = 1f - r.distance / BroadcastUtility.ReachTiles;
                BroadcastSystem.AddCulture(f, airtime * 0.5f * reach);
                r.favour += Mathf.Min(2f, airtime * 0.1f * reach);
            }
        }
    }

    /// <summary>Programmes made for one faction's audience: their opinion of you and your influence on them rise.</summary>
    public class Program_PublicDiplomacy : BroadcastProgramWorker
    {
        public override void ApplyDaily(float airtime, Faction target)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.GetRelation(target, Faction.OfPlayer).favour += Mathf.Min(5f, airtime * 0.5f);
            BroadcastSystem.AddCulture(target, airtime);
        }
    }

    /// <summary>Counter-programming: drowns out foreign propaganda for a day and a half.</summary>
    public class Program_CounterProgramming : BroadcastProgramWorker
    {
        public override void ApplyDaily(float airtime, Faction target)
        {
            if (airtime >= 1f)
                WorldComponent_Dominion.Instance.Broadcast.counterProgrammingUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay * 3 / 2;
        }
    }

    /// <summary>Entertainment: no political effect, but TV watchers enjoy it more.</summary>
    public class Program_Entertainment : BroadcastProgramWorker
    {
        public override void ApplyDaily(float airtime, Faction target) { }
    }

    public static class BroadcastUtility
    {
        /// <summary>Cultural broadcasts reach factions with settlements within this many tiles.</summary>
        public const float ReachTiles = 60f;

        public static void GiveColonistsThought(ThoughtDef thought)
        {
            if (thought == null) return;
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome)
                    foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                        p.needs?.mood?.thoughts?.memories?.TryGainMemory(thought, null, null);
        }
    }
}
