using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// What the radio desk can be told to do. The first two keep their values for old saves.
    /// Colony advisories: Advisory (keep calm), Morale, CounterPropaganda. Story responses: Investigate, Debunk, Spread, Suppress.
    /// </summary>
    public enum RadioOrderKind { Advisory, Investigate, Morale, CounterPropaganda, Debunk, Spread, Suppress }

    /// <summary>A job for whoever next sits at a radio receiver.</summary>
    public class RadioOrder : IExposable
    {
        public RadioOrderKind kind;

        /// <summary>For story responses: the story.</summary>
        public int newsId = -1;

        /// <summary>Work done so far, in ticks of average-skill work.</summary>
        public float progress;

        public RadioOrderSpec Spec => RadioOrderSpec.Of(kind);

        public float WorkNeeded => Spec.work;

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref newsId, "newsId", -1);
            Scribe_Values.Look(ref progress, "progress");
        }
    }

    /// <summary>The fixed properties of each kind of radio order.</summary>
    public class RadioOrderSpec
    {
        public RadioOrderKind kind;
        public bool aboutStory;
        public float work;
        public SkillDef skill;
        public Texture2D icon;

        public string Label => ("Dominion_Radio_Order_" + kind).Translate();
        public string Desc => ("Dominion_Radio_Order_" + kind + "_Desc").Translate();

        private static Dictionary<RadioOrderKind, RadioOrderSpec> all;

        public static RadioOrderSpec Of(RadioOrderKind k)
        {
            if (all == null)
            {
                all = new Dictionary<RadioOrderKind, RadioOrderSpec>();
                void Add(RadioOrderKind kind, bool story, float work, SkillDef skill, Texture2D icon) =>
                    all[kind] = new RadioOrderSpec { kind = kind, aboutStory = story, work = work, skill = skill, icon = icon };
                Add(RadioOrderKind.Advisory, false, 1500f, SkillDefOf.Social, DominionTheme.GizmoAdvisory);
                Add(RadioOrderKind.Morale, false, 800f, SkillDefOf.Social, DominionTheme.IconMorale);
                Add(RadioOrderKind.CounterPropaganda, false, 1500f, SkillDefOf.Social, DominionTheme.IconCounter);
                Add(RadioOrderKind.Investigate, true, 3000f, SkillDefOf.Intellectual, DominionTheme.GizmoInvestigate);
                Add(RadioOrderKind.Debunk, true, 600f, SkillDefOf.Social, DominionTheme.IconDebunk);
                Add(RadioOrderKind.Spread, true, 800f, SkillDefOf.Social, DominionTheme.IconSpread);
                Add(RadioOrderKind.Suppress, true, 400f, SkillDefOf.Social, DominionTheme.IconSuppress);
            }
            return all[k];
        }

        public static readonly RadioOrderKind[] Advisories = { RadioOrderKind.Advisory, RadioOrderKind.Morale, RadioOrderKind.CounterPropaganda };
        public static readonly RadioOrderKind[] StoryResponses = { RadioOrderKind.Investigate, RadioOrderKind.Debunk, RadioOrderKind.Spread, RadioOrderKind.Suppress };
    }

    /// <summary>The colony's radio desk: queued orders and the calm advisory in force.</summary>
    public class RadioOrders : IExposable
    {
        /// <summary>A successful calm advisory keeps colonists calm about bad news for this long.</summary>
        public const int AdvisoryDays = 2;

        /// <summary>A successful counter-propaganda broadcast blocks foreign propaganda for this long.</summary>
        public const int CounterDays = 5;

        private List<RadioOrder> orders = new List<RadioOrder>();

        /// <summary>Until this tick, colonists shrug off bad news: they have been told what to expect.</summary>
        public int advisoryUntilTick = -1;

        public IReadOnlyList<RadioOrder> Orders => orders;

        /// <summary>The radio desk handles this many orders in its queue at once; batch orders stop there.</summary>
        public const int Capacity = 10;

        public int Free => System.Math.Max(0, Capacity - orders.Count);

        public bool AdvisoryInForce => Find.TickManager.TicksGame < advisoryUntilTick;

        public bool AdvisoryQueued => Queued(RadioOrderKind.Advisory);

        public bool Queued(RadioOrderKind kind, int newsId = -1) => orders.Any(o => o.kind == kind && o.newsId == newsId);

        public RadioOrder For(NewsItem n) => n == null ? null : orders.FirstOrDefault(o => o.newsId == n.id);

        public bool Investigating(NewsItem n) => n != null && Queued(RadioOrderKind.Investigate, n.id);

        public RadioOrder Next => orders.Count > 0 ? orders[0] : null;

        public void Queue(RadioOrderKind kind, int newsId = -1)
        {
            if (Queued(kind, newsId)) return;
            var o = new RadioOrder { kind = kind, newsId = newsId };
            // Colony advisories go first: the colony's mood is more urgent than any one story.
            if (!o.Spec.aboutStory) orders.Insert(0, o);
            else orders.Add(o);
        }

        public void Cancel(RadioOrderKind kind, int newsId = -1) => orders.RemoveAll(o => o.kind == kind && o.newsId == newsId);

        public void Remove(RadioOrder o) => orders.Remove(o);

        /// <summary>Drops story responses that no longer apply (the story left the log, or was settled meanwhile).</summary>
        public void Prune(NewsLog log) =>
            orders.RemoveAll(o => o.Spec.aboutStory && RadioOrderUtility.CannotRespondReason(o.kind, log.FindDelivered(o.newsId), checkQueue: false) != null);

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

    /// <summary>What radio orders do, when they can be given, and the menus that give them.</summary>
    public static class RadioOrderUtility
    {
        /// <summary>Bad-news moods a calm advisory talks colonists out of.</summary>
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

        /// <summary>The story upsets colonists (it gave them a bad mood when it arrived).</summary>
        public static bool IsBadNews(NewsItem n)
        {
            ThoughtDef t = n != null ? NewsThoughts.ThoughtFor(n) : null;
            return t != null && t.stages[0].baseMoodEffect < 0f;
        }

        /// <summary>The story is welcome news: peace near us, or a blow to a faction that is hostile to us.</summary>
        public static bool IsGoodNews(NewsItem n)
        {
            if (n == null || n.corrected || n.correctsId >= 0) return false;
            ThoughtDef t = NewsThoughts.ThoughtFor(n);
            if (t != null && t.stages[0].baseMoodEffect > 0f) return true;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null || n.primary == null || n.primary.IsPlayer || !n.primary.HostileTo(player)) return false;
            // Misfortune for an enemy.
            return n.Category == EventCategory.Disaster || n.def == DominionEventDefOf.Dominion_Riots || n.def == DominionEventDefOf.Dominion_Coup
                   || n.def == DominionEventDefOf.Dominion_Revolution || n.def == DominionEventDefOf.Dominion_CapitalLost;
        }

        /// <summary>Null if this response can be ordered for the story, otherwise why not.</summary>
        public static string CannotRespondReason(RadioOrderKind kind, NewsItem n, bool checkQueue = true)
        {
            if (n == null) return "Dominion_Radio_NoStory".Translate();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (checkQueue && comp.RadioOrders.For(n) != null) return "Dominion_Radio_StoryBusy".Translate();
            switch (kind)
            {
                case RadioOrderKind.Investigate:
                case RadioOrderKind.Debunk:
                    return CanInvestigate(n) ? null : "Dominion_Radio_NotRumour".Translate();
                case RadioOrderKind.Spread:
                    return IsGoodNews(n) ? null : "Dominion_Radio_NotGoodNews".Translate();
                case RadioOrderKind.Suppress:
                    return IsBadNews(n) ? null : "Dominion_Radio_NotBadNews".Translate();
            }
            return null;
        }

        /// <summary>Null if this colony advisory can be ordered now, otherwise why not.</summary>
        public static string CannotAdviseReason(RadioOrderKind kind)
        {
            RadioOrders orders = WorldComponent_Dominion.Instance.RadioOrders;
            if (kind == RadioOrderKind.Advisory && orders.AdvisoryInForce)
                return "Dominion_Radio_AdvisoryInForce".Translate((orders.advisoryUntilTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            if (kind == RadioOrderKind.CounterPropaganda && WorldComponent_Dominion.Instance.Broadcast.CounterProgrammingActive)
                return "Dominion_Radio_CounterInForce".Translate();
            return null;
        }

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

        private static void GiveAll(ThoughtDef t, Pawn except = null)
        {
            if (t == null) return;
            foreach (Pawn p in HomeColonists())
                if (p != except) p.needs?.mood?.thoughts?.memories?.TryGainMemory(t, null, null);
        }

        private static void RemoveAll(ThoughtDef t)
        {
            if (t == null) return;
            foreach (Pawn p in HomeColonists())
                p.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(t);
        }

        // ------------------------------------------------------------------ odds and work

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

        public static float WorkSpeed(Pawn pawn, RadioOrder order)
        {
            float level = pawn.skills?.GetSkill(order.Spec.skill)?.Level ?? 5;
            return 0.5f + level / 10f;
        }

        public static SkillDef Skill(RadioOrder order) => order.Spec.skill;

        // ------------------------------------------------------------------ results

        public static void Complete(RadioOrder order, Pawn pawn)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            comp.RadioOrders.Remove(order);
            NewsItem n = order.Spec.aboutStory ? comp.News.FindDelivered(order.newsId) : null;
            if (order.Spec.aboutStory && CannotRespondReason(order.kind, n, checkQueue: false) != null) return;
            switch (order.kind)
            {
                case RadioOrderKind.Advisory: CompleteCalm(pawn, comp); break;
                case RadioOrderKind.Morale: CompleteMorale(pawn); break;
                case RadioOrderKind.CounterPropaganda: CompleteCounter(pawn, comp); break;
                case RadioOrderKind.Investigate: CompleteInvestigation(n, pawn, comp); break;
                case RadioOrderKind.Debunk: CompleteDebunk(n, pawn, comp); break;
                case RadioOrderKind.Spread: CompleteSpread(n, pawn); break;
                case RadioOrderKind.Suppress: CompleteSuppress(n, pawn); break;
            }
        }

        private static void CompleteCalm(Pawn speaker, WorldComponent_Dominion comp)
        {
            if (!Rand.Chance(AdvisoryChance(speaker)))
            {
                GiveAll(DominionThoughtDefOf.Dominion_Advisory_Unconvincing);
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

        private static void CompleteMorale(Pawn speaker)
        {
            // Music, jokes and the weather: it always lands, a little.
            GiveAll(DominionThoughtDefOf.Dominion_Advisory_Morale, speaker);
            Messages.Message("Dominion_Radio_MoraleDone".Translate(speaker.Named("PAWN")), speaker, MessageTypeDefOf.PositiveEvent);
        }

        private static void CompleteCounter(Pawn speaker, WorldComponent_Dominion comp)
        {
            if (!Rand.Chance(AdvisoryChance(speaker)))
            {
                Messages.Message("Dominion_Radio_CounterFailed".Translate(speaker.Named("PAWN")), speaker, MessageTypeDefOf.NegativeEvent);
                return;
            }
            RemoveAll(DominionThoughtDefOf.Dominion_ForeignPropaganda);
            int until = Find.TickManager.TicksGame + RadioOrders.CounterDays * GenDate.TicksPerDay;
            comp.Broadcast.counterProgrammingUntil = Mathf.Max(comp.Broadcast.counterProgrammingUntil, until);
            Messages.Message("Dominion_Radio_CounterDone".Translate(speaker.Named("PAWN"), RadioOrders.CounterDays), speaker, MessageTypeDefOf.PositiveEvent);
        }

        private static void CompleteInvestigation(NewsItem n, Pawn listener, WorldComponent_Dominion comp)
        {
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
            Expose(n, comp);
            Messages.Message("Dominion_Radio_InvestigateFalse".Translate(listener.Named("PAWN"), n.headline), listener, MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>
        /// Calling a rumour false on air without checking it. Right: a correction runs and the worry goes. Wrong: the
        /// story turns out true, and the colony feels lied to.
        /// </summary>
        private static void CompleteDebunk(NewsItem n, Pawn speaker, WorldComponent_Dominion comp)
        {
            n.investigated = true;
            if (!n.truthful)
            {
                Expose(n, comp);
                Messages.Message("Dominion_Radio_DebunkRight".Translate(speaker.Named("PAWN"), n.headline), speaker, MessageTypeDefOf.PositiveEvent);
                return;
            }
            n.confidence = NewsConfidence.Confirmed;
            NewsText.Resolve(n);
            GiveAll(DominionThoughtDefOf.Dominion_Advisory_Misled);
            Messages.Message("Dominion_Radio_DebunkWrong".Translate(speaker.Named("PAWN"), n.headline), speaker, MessageTypeDefOf.NegativeEvent);
        }

        private static void Expose(NewsItem n, WorldComponent_Dominion comp)
        {
            // A false rumour: run the correction now and stop colonists fretting over it.
            NewsSystem.PublishCorrection(n, comp, 0);
            ThoughtDef worry = NewsThoughts.ThoughtFor(n);
            if (worry != null && worry.stages[0].baseMoodEffect < 0f) RemoveAll(worry);
        }

        private static void CompleteSpread(NewsItem n, Pawn speaker)
        {
            GiveAll(DominionThoughtDefOf.Dominion_Advisory_GoodNews, speaker);
            Messages.Message("Dominion_Radio_SpreadDone".Translate(speaker.Named("PAWN"), n.headline), speaker, MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>Keeps a bad story off the air: its mood goes, but word may get out that it was hushed up.</summary>
        private static void CompleteSuppress(NewsItem n, Pawn speaker)
        {
            RemoveAll(NewsThoughts.ThoughtFor(n));
            if (Rand.Chance(0.25f))
            {
                GiveAll(DominionThoughtDefOf.Dominion_Advisory_Censored, speaker);
                Messages.Message("Dominion_Radio_SuppressLeaked".Translate(speaker.Named("PAWN"), n.headline), speaker, MessageTypeDefOf.NegativeEvent);
                return;
            }
            Messages.Message("Dominion_Radio_SuppressDone".Translate(speaker.Named("PAWN"), n.headline), speaker, MessageTypeDefOf.NeutralEvent);
        }

        // ------------------------------------------------------------------ menus

        /// <summary>The colony advisories, for the newspaper's ADVISORY stamp and the receiver.</summary>
        public static List<FloatMenuOption> AdvisoryMenu()
        {
            var options = new List<FloatMenuOption>();
            string cannot = CannotOrderReason();
            RadioOrders orders = WorldComponent_Dominion.Instance.RadioOrders;
            foreach (RadioOrderKind kind in RadioOrderSpec.Advisories)
            {
                RadioOrderSpec spec = RadioOrderSpec.Of(kind);
                if (orders.Queued(kind))
                {
                    options.Add(new FloatMenuOption("Dominion_Radio_CancelOrder".Translate(spec.Label), () => orders.Cancel(kind),
                        spec.icon, Color.white) { tooltip = spec.Desc });
                    continue;
                }
                string reason = cannot ?? CannotAdviseReason(kind);
                string label = spec.Label + (reason != null ? " (" + reason + ")" : "");
                options.Add(new FloatMenuOption(label, reason != null ? null : (System.Action)(() => Order(kind, null)), spec.icon, Color.white)
                    { tooltip = spec.Desc });
            }
            return options;
        }

        /// <summary>Responses to one story, for the article's RESPOND stamp. Only those that apply are listed.</summary>
        public static List<FloatMenuOption> StoryMenu(NewsItem n)
        {
            var options = new List<FloatMenuOption>();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RadioOrder queued = comp.RadioOrders.For(n);
            if (queued != null)
            {
                options.Add(new FloatMenuOption("Dominion_Radio_CancelOrder".Translate(queued.Spec.Label), () => comp.RadioOrders.Remove(queued),
                    queued.Spec.icon, Color.white));
                return options;
            }
            string cannot = CannotOrderReason();
            foreach (RadioOrderKind kind in RadioOrderSpec.StoryResponses)
            {
                if (CannotRespondReason(kind, n) != null) continue;
                RadioOrderSpec spec = RadioOrderSpec.Of(kind);
                string label = spec.Label + (cannot != null ? " (" + cannot + ")" : "");
                options.Add(new FloatMenuOption(label, cannot != null ? null : (System.Action)(() => Order(kind, n)), spec.icon, Color.white)
                    { tooltip = spec.Desc });
            }
            return options;
        }

        public static bool AnyResponse(NewsItem n) =>
            WorldComponent_Dominion.Instance.RadioOrders.For(n) != null || RadioOrderSpec.StoryResponses.Any(k => CannotRespondReason(k, n) == null);

        public static void Order(RadioOrderKind kind, NewsItem n)
        {
            string reason = CannotOrderReason();
            if (reason != null)
            {
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            WorldComponent_Dominion.Instance.RadioOrders.Queue(kind, n?.id ?? -1);
            Messages.Message("Dominion_Radio_Queued".Translate(RadioOrderSpec.Of(kind).Label), MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
