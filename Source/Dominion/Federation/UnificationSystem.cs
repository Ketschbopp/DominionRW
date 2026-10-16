using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Once a day: records major factions that fell (and how), lets unhappy members leave the Federation,
    /// and offers the ending when enough of the world is unified.
    /// </summary>
    public class UnificationSystem : SimSystem
    {
        public override int Order => 60;

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            UnificationState st = comp.Unification;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            TrackFallen(comp, st);
            ReviewMembers(comp);

            if (!DominionMod.Settings.endingEnabled || st.proclaimed || st.offered) return;
            FederationUtility.Progress p = FederationUtility.Compute();
            if (!p.Complete) return;
            st.offered = true;
            OfferProclamation(p);
        }

        private static void TrackFallen(WorldComponent_Dominion comp, UnificationState st)
        {
            var majorsNow = new HashSet<int>();
            foreach (Faction f in FederationUtility.Majors())
                majorsNow.Add(f.loadID);

            // Factions that were major yesterday and are gone today.
            foreach (KeyValuePair<int, UnifyPath> kv in st.lastStatus)
            {
                if (majorsNow.Contains(kv.Key) || st.fallen.ContainsKey(kv.Key)) continue;
                Faction f = comp.Cache.FactionById(kv.Key);
                if (f != null && !f.defeated && DominionScheduler.IsSimulated(f) && comp.Cache.SettlementsOf(f).Count > 0)
                    continue; // still around, just not counted any more (e.g. outlaw setting changed)
                // Counted fresh: the daily territory pass skips factions that were just defeated.
                int held = OutpostsHeldByPlayer(kv.Key);
                st.peakSettlements.TryGetValue(kv.Key, out int peak);
                // Subjugated: you hold at least half of what they once had. Conquered: you were at war and beat them in
                // battle yourself. A member destroyed by someone else, or a faction others finished off, is not yours.
                UnifyPath path = kv.Value == UnifyPath.Subjugated || (held > 0 && held * 2 >= Mathf.Max(1, peak)) ? UnifyPath.Subjugated
                    : st.lastHostile.Contains(kv.Key) && st.beatenByPlayer.Contains(kv.Key) ? UnifyPath.Conquered
                    : UnifyPath.None;
                st.fallen[kv.Key] = path;
                st.fallenNames[kv.Key] = f?.Name ?? "?";
                if (path != UnifyPath.None && f != null)
                    EventBus.Raise(DominionEventDefOf.Dominion_FactionAbsorbed, f, Faction.OfPlayer, detail: ("Dominion_UnifyPath_" + path).Translate());
            }

            // A fallen faction that came back counts as living again.
            foreach (int id in majorsNow)
                if (st.fallen.Remove(id)) st.fallenNames.Remove(id);

            st.lastStatus.Clear();
            st.lastHostile.Clear();
            foreach (Faction f in FederationUtility.Majors())
            {
                st.lastStatus[f.loadID] = FederationUtility.StatusOf(f);
                if (f.HostileTo(Faction.OfPlayer)) st.lastHostile.Add(f.loadID);
                int n = comp.Cache.SettlementsOf(f).Count;
                if (!st.peakSettlements.TryGetValue(f.loadID, out int peak) || n > peak) st.peakSettlements[f.loadID] = n;
            }
        }

        private static int OutpostsHeldByPlayer(int originalId)
        {
            int player = Faction.OfPlayer.loadID, held = 0;
            IntegrationRegistry.Get<Integration_FactionTerritories>()?.ForEachVassalOutpost((master, original) =>
            {
                if (master == player && original == originalId) held++;
            });
            return held;
        }

        /// <summary>Members who have come to dislike you may leave; the Federation is not a prison.</summary>
        private static void ReviewMembers(WorldComponent_Dominion comp)
        {
            List<Treaty> leaving = null;
            foreach (Treaty t in comp.Treaties.All)
            {
                if (!t.def.federation) continue;
                Faction member = t.Other(Faction.OfPlayer);
                if (member == null || member.IsPlayer) continue;
                float op = comp.GetRelation(member, Faction.OfPlayer).opinion;
                if (op < -15f && Rand.Chance(Mathf.Clamp01(-op / 1000f)))
                    (leaving ??= new List<Treaty>()).Add(t);
            }
            if (leaving == null) return;
            foreach (Treaty t in leaving)
                comp.Treaties.Cancel(t, t.Other(Faction.OfPlayer));
        }

        public static void OfferProclamation(FederationUtility.Progress p)
        {
            var node = new DiaNode("Dominion_Federation_ReadyText".Translate(p.unified, p.total));
            node.options.Add(new DiaOption("Dominion_Federation_Proclaim".Translate())
            {
                action = FederationUtility.Proclaim,
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_Federation_Later".Translate()) { resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_Federation_ReadyLabel".Translate()));
        }

        public override void OnEvent(DominionEvent ev)
        {
            if (ev.primary == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (ev.def == DominionEventDefOf.Dominion_FederationJoined)
            {
                FactionState s = comp.GetState(ev.primary);
                s.stability = Mathf.Min(100f, s.stability + 5f);
                comp.GetRelation(ev.primary, Faction.OfPlayer).favour += 10f;
            }
            else if (ev.def == DominionEventDefOf.Dominion_FederationLeft)
            {
                RelationState r = comp.GetRelation(ev.primary, Faction.OfPlayer);
                r.grievance = Mathf.Min(100f, r.grievance + 10f);
            }
        }
    }
}
