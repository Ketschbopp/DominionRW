using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Keeps treaties honest: expiry, treaties broken by war, defensive pacts calling partners to war, and the
    /// reputation cost of breaking your word. Also applies the reactions to embargoes, borders and denunciations.
    /// </summary>
    public class DiplomacySystem : SimSystem
    {
        public override int Order => 12;

        private struct Obligation
        {
            public Treaty treaty;
            public Faction defender;
            public Faction victim;
            public Faction aggressor;
        }

        // Joining a war from inside an event would nest events; obligations are handled on the next hour.
        private static readonly List<Obligation> obligations = new List<Obligation>();
        private static readonly List<Treaty> buffer = new List<Treaty>();

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            int now = Find.TickManager.TicksGame;
            buffer.Clear();
            buffer.AddRange(comp.Treaties.All);
            foreach (Treaty t in buffer)
            {
                if (t.a.defeated || t.b.defeated)
                {
                    comp.Treaties.Remove(t);
                    continue;
                }
                if (t.expiresTick >= 0 && now >= t.expiresTick)
                {
                    comp.Treaties.Remove(t);
                    EventBus.Raise(DominionEventDefOf.Dominion_TreatyExpired, t.a, t.b, detail: t.def.label, secrecy: t.def.secrecy);
                }
            }
            buffer.Clear();
        }

        /// <summary>Calls to war pending from another save must not carry over.</summary>
        public override void OnWorldLoaded() => obligations.Clear();

        public override void Hourly()
        {
            if (obligations.Count == 0) return;
            var todo = new List<Obligation>(obligations);
            obligations.Clear();
            foreach (Obligation o in todo)
                ResolveObligation(o);
        }

        public override void OnEvent(DominionEvent ev)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction a = ev.primary, b = ev.secondary;
            DominionEventDef def = ev.def;

            if (def == DominionEventDefOf.Dominion_RelationsWarmed && a != null && b != null)
            {
                comp.Treaties.Record(DominionTreatyDefOf.Dominion_Alliance, a, b);
            }
            else if (def == DominionEventDefOf.Dominion_AllianceBroken && a != null && b != null)
            {
                Treaty t = comp.Treaties.Find(a, b, DominionTreatyDefOf.Dominion_Alliance);
                if (t != null) comp.Treaties.Remove(t);
            }
            else if (def == DominionEventDefOf.Dominion_WarDeclared && a != null && b != null)
            {
                // Every treaty between the two is broken by the aggressor.
                comp.Treaties.Between(a, b, buffer);
                var broken = new List<Treaty>(buffer);
                foreach (Treaty t in broken)
                    comp.Treaties.Break(t, a);

                // Partners of the victim with a defensive treaty are called to war.
                foreach (Treaty t in comp.Treaties.All)
                {
                    if (!t.def.defensive || !t.Involves(b)) continue;
                    Faction defender = t.Other(b);
                    if (defender == a) continue;
                    obligations.Add(new Obligation { treaty = t, defender = defender, victim = b, aggressor = a });
                }
            }
            else if (def == DominionEventDefOf.Dominion_TreatyBroken && a != null)
            {
                if (b != null) comp.GetRelation(b, a).grievance = Mathf.Min(100f, comp.GetRelation(b, a).grievance + 40f);
                // Everyone remembers who breaks their word; the honourable most of all.
                foreach (Faction f in comp.Cache.Simulated)
                {
                    if (f == a || f == b) continue;
                    comp.GetRelation(f, a).favour -= 5f + 10f * comp.GetState(f).personality.honour;
                }
            }
            else if (def == DominionEventDefOf.Dominion_EmbargoDeclared && a != null && b != null)
            {
                comp.GetRelation(b, a).grievance += 10f;
            }
            else if (def == DominionEventDefOf.Dominion_BordersChanged && a != null && b != null)
            {
                if (comp.GetRelation(a, b).border == BorderStatus.Closed)
                    comp.GetRelation(b, a).grievance += 5f;
            }
            else if (def == DominionEventDefOf.Dominion_Denounced && a != null && b != null)
            {
                comp.GetRelation(b, a).grievance += 15f;
                foreach (Faction f in comp.Cache.Simulated)
                {
                    if (f == a || f == b) continue;
                    if (f.HostileTo(b)) comp.GetRelation(f, a).favour += 8f;
                    else if (f.RelationKindWith(b) == FactionRelationKind.Ally) comp.GetRelation(f, a).favour -= 8f;
                }
            }
        }

        private static void ResolveObligation(Obligation o)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (!comp.Treaties.All.Contains(o.treaty)) return;
            if (o.defender.defeated || o.aggressor.defeated || o.defender.HostileTo(o.aggressor)) return;

            if (o.defender.IsPlayer)
            {
                AskPlayer(o);
                return;
            }
            FactionState ds = comp.GetState(o.defender);
            float chance = 0.2f + ds.personality.honour * 0.7f;
            if (comp.Treaties.AnyNonAggression(o.defender, o.aggressor)) chance *= 0.5f;
            if (Rand.Chance(chance))
                DiplomacyUtility.SetRelationKind(o.defender, o.aggressor, FactionRelationKind.Hostile, o.defender, o.treaty.def.label);
            else
                comp.Treaties.Break(o.treaty, o.defender);
        }

        private static void AskPlayer(Obligation o)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var node = new DiaNode("Dominion_Obligation_Text".Translate(o.victim.Name, o.aggressor.Name, Find.ActiveLanguageWorker.WithIndefiniteArticle(o.treaty.def.label)));
            var honour = new DiaOption("Dominion_Obligation_Honour".Translate(o.aggressor.Name))
            {
                action = () =>
                {
                    if (!o.aggressor.HostileTo(Faction.OfPlayer))
                        DiplomacyUtility.SetRelationKind(Faction.OfPlayer, o.aggressor, FactionRelationKind.Hostile, Faction.OfPlayer, o.treaty.def.label);
                },
                resolveTree = true,
            };
            var refuse = new DiaOption("Dominion_Obligation_Refuse".Translate())
            {
                action = () => comp.Treaties.Break(o.treaty, Faction.OfPlayer),
                resolveTree = true,
            };
            node.options.Add(honour);
            node.options.Add(refuse);
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_Obligation_Title".Translate()));
        }

        /// <summary>Sum of opinion bonuses from treaties between the pair.</summary>
        public static float TreatyOpinionBonus(Faction x, Faction y)
        {
            float sum = 0f;
            IReadOnlyList<Treaty> all = WorldComponent_Dominion.Instance.Treaties.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Between(x, y)) sum += all[i].def.opinionBonus;
            return sum;
        }
    }
}
