using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Works out how each faction sees every other faction (and the player) once a day: ideology,
    /// tech gap, borders, shared enemies, grievances, vassal outposts held, and current relations.
    /// Also tracks which pairs are at war.
    /// </summary>
    public class RelationsSystem : SimSystem
    {
        public override int Order => 10;

        /// <summary>Settlements closer than this many tiles count as neighbours.</summary>
        public const float NeighbourDistance = 25f;

        private readonly List<Faction> others = new List<Faction>();

        public override void DailyFaction(Faction faction)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            WorldCache cache = comp.Cache;
            FactionState fs = comp.GetState(faction);
            fs.personality.Refresh(faction);

            others.Clear();
            others.AddRange(cache.Simulated);
            if (Faction.OfPlayerSilentFail != null) others.Add(Faction.OfPlayer);

            bool recomputeDistance = DominionMod.Settings.simulationDetail != SimulationDetail.Low
                                     || GenDate.DaysPassed % 3 == faction.loadID % 3;
            int wars = 0;

            for (int i = 0; i < others.Count; i++)
            {
                Faction other = others[i];
                if (other == faction) continue;
                RelationState r = comp.GetRelation(faction, other);

                if (recomputeDistance || r.distance < 0f)
                    r.distance = NearestDistance(cache.SettlementsOf(faction), cache.SettlementsOf(other));
                r.ideoCompatibility = IdeoCompatibility(faction, other);
                r.sharedEnemies = CountSharedEnemies(faction, other, cache.Simulated);
                r.grievance *= 0.98f;
                if (r.grievance < 0.5f) r.grievance = 0f;
                r.favour *= 0.98f;
                if (Mathf.Abs(r.favour) < 0.5f) r.favour = 0f;

                // Hostility that ended outside Dominion (vanilla, other mods) ends the war record too.
                if (r.AtWar && faction.RelationKindWith(other) != FactionRelationKind.Hostile)
                    r.warSince = -1;
                if (r.AtWar) wars++;

                r.targetOpinion = TargetOpinion(faction, other, fs, r, comp.TryGetRelation(other, faction));
                r.opinion = Mathf.Clamp(r.opinion + (r.targetOpinion - r.opinion) * 0.1f, -100f, 100f);
            }
            fs.activeWars = wars;
        }

        private static float TargetOpinion(Faction f, Faction other, FactionState fs, RelationState r, RelationState theirs)
        {
            if (f.def.permanentEnemy || other.def.permanentEnemy)
                return -80f;

            float t = 0f;
            t += r.ideoCompatibility * 30f;
            t -= Mathf.Abs((int)f.def.techLevel - (int)other.def.techLevel) * 5f;
            t -= fs.personality.paranoia * 10f;
            if (r.distance >= 0f && r.distance < NeighbourDistance)
                t -= (1f - r.distance / NeighbourDistance) * 15f * (0.5f + fs.personality.paranoia);
            t += Mathf.Min(r.sharedEnemies, 3) * 8f;
            t -= r.grievance * 0.6f;
            t -= Mathf.Min(r.outpostsHeldByOther, 3) * 15f;
            t += r.favour;
            t += DiplomacySystem.TreatyOpinionBonus(f, other);
            if (theirs != null)
            {
                if (theirs.embargo) t -= 15f;
                if (theirs.border == BorderStatus.Closed) t -= 8f;
                else if (theirs.border == BorderStatus.Restricted) t -= 3f;
            }

            switch (f.RelationKindWith(other))
            {
                case FactionRelationKind.Ally: t += 15f; break;
                case FactionRelationKind.Hostile: t -= 20f; break;
            }
            if (other.IsPlayer)
                t += f.PlayerGoodwill * 0.5f;

            return Mathf.Clamp(t, -100f, 100f);
        }

        public static float NearestDistance(List<Settlement> a, List<Settlement> b)
        {
            if (a.Count == 0 || b.Count == 0) return -1f;
            WorldGrid grid = Find.WorldGrid;
            float best = float.MaxValue;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                {
                    float d = grid.ApproxDistanceInTiles(a[i].Tile, b[j].Tile);
                    if (d < best) best = d;
                }
            return best;
        }

        /// <summary>-1..1. Same ideology is 1; shared memes raise it, memes that exclude each other lower it.</summary>
        public static float IdeoCompatibility(Faction a, Faction b)
        {
            Ideo ia = a.ideos?.PrimaryIdeo;
            Ideo ib = b.ideos?.PrimaryIdeo;
            if (ia == null || ib == null) return 0f;
            if (ia == ib) return 1f;
            if (!ModsConfig.IdeologyActive)
                return ia.culture != null && ia.culture == ib.culture ? 0.3f : 0f;

            int shared = 0, conflicting = 0;
            foreach (MemeDef ma in ia.memes)
                foreach (MemeDef mb in ib.memes)
                {
                    if (ma == mb) shared++;
                    else if (Excludes(ma, mb)) conflicting++;
                }
            return Mathf.Clamp((shared - 1.5f * conflicting) / 3f, -1f, 1f);
        }

        private static bool Excludes(MemeDef a, MemeDef b)
        {
            if (a.exclusionTags == null || b.exclusionTags == null) return false;
            for (int i = 0; i < a.exclusionTags.Count; i++)
                if (b.exclusionTags.Contains(a.exclusionTags[i])) return true;
            return false;
        }

        private static int CountSharedEnemies(Faction a, Faction b, List<Faction> candidates)
        {
            int n = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Faction h = candidates[i];
                if (h == a || h == b || h.def.permanentEnemy) continue;
                if (a.HostileTo(h) && b.HostileTo(h)) n++;
            }
            return n;
        }

        public override void OnEvent(DominionEvent ev)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction a = ev.primary, b = ev.secondary;
            if (a == null || b == null) return;
            int now = Find.TickManager.TicksGame;

            if (ev.def == DominionEventDefOf.Dominion_WarDeclared)
            {
                RelationState ab = comp.GetRelation(a, b);
                RelationState ba = comp.GetRelation(b, a);
                ab.warSince = ba.warSince = now;
                ab.wasAggressor = true;
                ba.wasAggressor = false;
                ba.grievance = Mathf.Min(100f, ba.grievance + 35f);
                ba.opinion = Mathf.Max(-100f, ba.opinion - 25f);
            }
            else if (ev.def == DominionEventDefOf.Dominion_PeaceMade)
            {
                RelationState ab = comp.GetRelation(a, b);
                RelationState ba = comp.GetRelation(b, a);
                ab.warSince = ba.warSince = -1;
                ab.truceUntil = ba.truceUntil = now + 30 * GenDate.TicksPerDay;
            }
            else if (ev.def == DominionEventDefOf.Dominion_AllianceBroken)
            {
                RelationState ba = comp.GetRelation(b, a);
                ba.grievance = Mathf.Min(100f, ba.grievance + 15f);
            }
        }
    }
}
