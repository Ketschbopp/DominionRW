using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum RadioOrderKind { Advisory, Investigate }

    /// <summary>
    /// A job for whoever next sits at a radio receiver: read a calming advisory to the colony, or chase down a
    /// rumour by calling around on the radio.
    /// </summary>
    public class RadioOrder : IExposable
    {
        public RadioOrderKind kind;

        /// <summary>For investigations: the rumour being checked.</summary>
        public int newsId = -1;

        /// <summary>Work done so far, in ticks of average-skill work.</summary>
        public float progress;

        public float WorkNeeded => kind == RadioOrderKind.Advisory ? RadioOrders.AdvisoryWork : RadioOrders.InvestigateWork;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref newsId, "newsId", -1);
            Scribe_Values.Look(ref progress, "progress");
        }
    }

    /// <summary>The colony's radio desk: queued advisories and investigations, and the advisory in force.</summary>
    public class RadioOrders : IExposable
    {
        public const float AdvisoryWork = 1500f;
        public const float InvestigateWork = 3000f;

        /// <summary>A successful advisory keeps colonists calm about bad news for this long.</summary>
        public const int AdvisoryDays = 2;

        private List<RadioOrder> orders = new List<RadioOrder>();

        /// <summary>Until this tick, colonists shrug off bad news: they have been told what to expect.</summary>
        public int advisoryUntilTick = -1;

        public IReadOnlyList<RadioOrder> Orders => orders;

        public bool AdvisoryInForce => Find.TickManager.TicksGame < advisoryUntilTick;

        public bool AdvisoryQueued => orders.Any(o => o.kind == RadioOrderKind.Advisory);

        public bool Investigating(NewsItem n) => n != null && orders.Any(o => o.kind == RadioOrderKind.Investigate && o.newsId == n.id);

        public RadioOrder Next => orders.Count > 0 ? orders[0] : null;

        public void QueueAdvisory()
        {
            if (AdvisoryQueued) return;
            // Advisories go to the front: calming the colony is more urgent than chasing gossip.
            orders.Insert(0, new RadioOrder { kind = RadioOrderKind.Advisory });
        }

        public void QueueInvestigation(NewsItem n)
        {
            if (Investigating(n)) return;
            orders.Add(new RadioOrder { kind = RadioOrderKind.Investigate, newsId = n.id });
        }

        public void Cancel(RadioOrderKind kind, int newsId = -1) =>
            orders.RemoveAll(o => o.kind == kind && (kind == RadioOrderKind.Advisory || o.newsId == newsId));

        public void Remove(RadioOrder o) => orders.Remove(o);

        /// <summary>Drops investigations of stories that left the log or no longer need checking.</summary>
        public void Prune(NewsLog log) =>
            orders.RemoveAll(o => o.kind == RadioOrderKind.Investigate && !RadioOrderUtility.CanInvestigate(log.FindDelivered(o.newsId)));

        public void ExposeData()
        {
            Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
            Scribe_Values.Look(ref advisoryUntilTick, "advisoryUntilTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                orders ??= new List<RadioOrder>();
                orders.RemoveAll(o => o == null);
            }
        }
    }

    /// <summary>What advisories and investigations do, and whether the player can order them.</summary>
    public static class RadioOrderUtility
    {
        /// <summary>Bad-news moods an advisory talks colonists out of.</summary>
        public static IEnumerable<ThoughtDef> BadNewsThoughts()
        {
            yield return DominionThoughtDefOf.Dominion_News_WarNearby;
            yield return DominionThoughtDefOf.Dominion_News_AllyAttacked;
            yield return DominionThoughtDefOf.Dominion_News_UnrestNearby;
            yield return DominionThoughtDefOf.Dominion_ForeignPropaganda;
        }

        /// <summary>Null when the colony can take radio orders, otherwise why not.</summary>
        public static string CannotOrderReason()
        {
            if (!DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished)
                return "Dominion_Radio_NeedResearch".Translate(DominionResearchDefOf.Dominion_RadioMonitoring.LabelCap);
            foreach (Map m in Find.Maps)
                if (m.IsPlayerHome && m.listerBuildings.ColonistsHaveBuilding(DominionThingDefOf.Dominion_RadioReceiver))
                    return null;
            return "Dominion_Radio_NeedReceiver".Translate();
        }

        /// <summary>Only unverified rumours that are not about the player can be investigated.</summary>
        public static bool CanInvestigate(NewsItem n) =>
            n != null && n.confidence == NewsConfidence.Rumor && !n.corrected && n.correctsId < 0 && !n.investigated &&
            !IntelUtility.InvolvesPlayer(n.primary, n.secondary);

        /// <summary>Cached for the UI: counting walks every colonist's memories.</summary>
        public static readonly UICache<int> UpsetColonists = new UICache<int>(ColonistsWithBadNews, 1f);

        public static int ColonistsWithBadNews()
        {
            int count = 0;
            foreach (Pawn p in HomeColonists())
            {
                foreach (ThoughtDef t in BadNewsThoughts())
                    if (p.needs?.mood?.thoughts?.memories?.GetFirstMemoryOfDef(t) != null)
                    {
                        count++;
                        break;
                    }
            }
            return count;
        }

        private static IEnumerable<Pawn> HomeColonists()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                if (!maps[m].IsPlayerHome) continue;
                foreach (Pawn p in maps[m].mapPawns.FreeColonistsSpawned)
                    yield return p;
            }
        }

        public static float AdvisoryChance(Pawn speaker)
        {
            float social = speaker.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
            float talking = speaker.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 1f;
            return Mathf.Clamp((0.4f + social * 0.05f) * talking, 0.2f, 0.97f);
        }

        public static float InvestigateChance(Pawn listener)
        {
            float intellectual = listener.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 5;
            float coverage = WorldComponent_Dominion.Instance?.radioCoverage ?? 0f;
            return Mathf.Clamp(0.35f + intellectual * 0.035f + coverage * 0.003f, 0.25f, 0.97f);
        }

        /// <summary>Work speed for the order: Social for advisories, Intellectual for investigations.</summary>
        public static float WorkSpeed(Pawn pawn, RadioOrder order)
        {
            SkillDef skill = order.kind == RadioOrderKind.Advisory ? SkillDefOf.Social : SkillDefOf.Intellectual;
            float level = pawn.skills?.GetSkill(skill)?.Level ?? 5;
            return 0.5f + level / 10f;
        }

        public static SkillDef Skill(RadioOrder order) => order.kind == RadioOrderKind.Advisory ? SkillDefOf.Social : SkillDefOf.Intellectual;

        public static void Complete(RadioOrder order, Pawn pawn)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.RadioOrders.Remove(order);
            if (order.kind == RadioOrderKind.Advisory) CompleteAdvisory(pawn, comp);
            else CompleteInvestigation(order, pawn, comp);
        }

        private static void CompleteAdvisory(Pawn speaker, WorldComponent_Dominion comp)
        {
            if (!Rand.Chance(AdvisoryChance(speaker)))
            {
                foreach (Pawn p in HomeColonists())
                    p.needs?.mood?.thoughts?.memories?.TryGainMemory(DominionThoughtDefOf.Dominion_Advisory_Unconvincing, null, null);
                Messages.Message("Dominion_Radio_AdvisoryFailed".Translate(speaker.Named("PAWN")), speaker, MessageTypeDefOf.NegativeEvent);
                return;
            }
            int calmed = 0;
            foreach (Pawn p in HomeColonists())
            {
                MemoryThoughtHandler memories = p.needs?.mood?.thoughts?.memories;
                if (memories == null) continue;
                bool had = false;
                foreach (ThoughtDef t in BadNewsThoughts())
                {
                    if (memories.GetFirstMemoryOfDef(t) == null) continue;
                    memories.RemoveMemoriesOfDef(t);
                    had = true;
                }
                if (had) calmed++;
                if (p != speaker) memories.TryGainMemory(DominionThoughtDefOf.Dominion_Advisory_Reassured, null, null);
            }
            comp.RadioOrders.advisoryUntilTick = Find.TickManager.TicksGame + RadioOrders.AdvisoryDays * GenDate.TicksPerDay;
            Messages.Message("Dominion_Radio_AdvisoryDone".Translate(speaker.Named("PAWN"), calmed, RadioOrders.AdvisoryDays), speaker, MessageTypeDefOf.PositiveEvent);
        }

        private static void CompleteInvestigation(RadioOrder order, Pawn listener, WorldComponent_Dominion comp)
        {
            NewsItem n = comp.News.FindDelivered(order.newsId);
            if (n == null || !CanInvestigate(n)) return;
            if (!Rand.Chance(InvestigateChance(listener)))
            {
                Messages.Message("Dominion_Radio_InvestigateInconclusive".Translate(listener.Named("PAWN"), n.headline), listener, MessageTypeDefOf.NeutralEvent);
                return;
            }
            n.investigated = true;
            if (n.truthful)
            {
                n.confidence = NewsConfidence.Confirmed;
                // Rewrite it: the original text was written as an unconfirmed report.
                NewsText.Resolve(n);
                Messages.Message("Dominion_Radio_InvestigateTrue".Translate(listener.Named("PAWN"), n.headline), listener, MessageTypeDefOf.NeutralEvent);
                return;
            }
            // A false rumour: run the correction now and stop colonists fretting over it.
            NewsSystem.PublishCorrection(n, comp, 0);
            ThoughtDef worry = NewsThoughts.ThoughtFor(n);
            if (worry != null && worry.stages[0].baseMoodEffect < 0f)
                foreach (Pawn p in HomeColonists())
                    p.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(worry);
            Messages.Message("Dominion_Radio_InvestigateFalse".Translate(listener.Named("PAWN"), n.headline), listener, MessageTypeDefOf.PositiveEvent);
        }

        // ------------------------------------------------------------------ orders from the UI

        public static void OrderAdvisory()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            string reason = CannotOrderReason();
            if (reason != null)
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (comp.RadioOrders.AdvisoryQueued)
            {
                comp.RadioOrders.Cancel(RadioOrderKind.Advisory);
                return;
            }
            comp.RadioOrders.QueueAdvisory();
            Messages.Message("Dominion_Radio_AdvisoryQueued".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void OrderInvestigation(NewsItem n)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            string reason = CannotOrderReason();
            if (reason != null)
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (comp.RadioOrders.Investigating(n))
            {
                comp.RadioOrders.Cancel(RadioOrderKind.Investigate, n.id);
                return;
            }
            comp.RadioOrders.QueueInvestigation(n);
            Messages.Message("Dominion_Radio_InvestigateQueued".Translate(n.headline), MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
