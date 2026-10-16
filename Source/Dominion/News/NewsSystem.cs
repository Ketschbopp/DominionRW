using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Turns events into news for the player. An event becomes a report only if the player's intel on the
    /// factions involved meets its secrecy; then it travels to the colony with a delay that depends on how
    /// the player hears about it. Hidden events can surface later as intel grows, or leak. Low intel also
    /// lets false rumors through, which are corrected once intel improves.
    /// </summary>
    public class NewsSystem : SimSystem
    {
        public override int Order => 50;

        private const int HiddenLifetimeDays = 30;
        private const float CorrectionIntel = 50f;

        private static readonly List<NewsItem> due = new List<NewsItem>();

        private static ResearchProjectDef tubeTelevision;
        private static bool tubeTelevisionLooked;

        /// <summary>Vanilla's Tube television research is done (any mod that keeps its defName counts).</summary>
        public static bool TelevisionResearched
        {
            get
            {
                if (!tubeTelevisionLooked)
                {
                    tubeTelevision = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("TubeTelevision");
                    tubeTelevisionLooked = true;
                }
                return tubeTelevision != null && tubeTelevision.IsFinished;
            }
        }

        public override void OnEvent(DominionEvent ev)
        {
            if (ev.def == null || ev.def.debugHidden && ev.def != DominionEventDefOf.Dominion_DebugTest) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            NewsItem item = comp.News.Add(NewsItem.FromEvent(ev));

            if (IntelUtility.InvolvesPlayer(ev.primary, ev.secondary))
            {
                item.confidence = NewsConfidence.Confirmed;
                item.source = NewsSource.DiplomaticDispatch;
                item.arrivesTick = ev.tick;
                if (DominionMod.Settings.instantNews) Deliver(item, comp);
                return;
            }

            float intel = IntelUtility.IntelOn(ev.primary, ev.secondary);
            float required = IntelUtility.RequiredIntel(ev.secrecy);
            if (intel >= required)
            {
                Schedule(item, intel, required, comp, NewsSource.TravelerGossip);
                if (DominionMod.Settings.instantNews) Deliver(item, comp);
            }
            else
                item.expiresTick = ev.tick + HiddenLifetimeDays * GenDate.TicksPerDay;
        }

        /// <summary>Picks how the news arrives, how late, and how sure it is.</summary>
        private static void Schedule(NewsItem item, float intel, float required, WorldComponent_Dominion comp, NewsSource fallback)
        {
            int now = Find.TickManager.TicksGame;
            int hour = GenDate.TicksPerHour;
            Faction player = Faction.OfPlayerSilentFail;
            bool radio = DominionResearchDefOf.Dominion_RadioMonitoring.IsFinished && comp.radioCoverage > 10f;
            bool allyInvolved = player != null &&
                                (item.primary?.RelationKindWith(player) == FactionRelationKind.Ally ||
                                 item.secondary?.RelationKindWith(player) == FactionRelationKind.Ally);

            int delay;
            // A linked large antenna hears the whole world, including traffic that is only lightly guarded.
            if (radio && (item.secrecy == Secrecy.Public || item.secrecy == Secrecy.Restricted && RadioAntenna.GlobalCoverage))
            {
                item.source = NewsSource.RadioIntercept;
                delay = Rand.Range(1 * hour, 6 * hour);
            }
            else if (allyInvolved)
            {
                item.source = NewsSource.DiplomaticDispatch;
                delay = Rand.Range(6 * hour, 24 * hour);
            }
            else if (item.secrecy >= Secrecy.Restricted)
            {
                item.source = NewsSource.IntelReport;
                delay = Rand.Range(12 * hour, 48 * hour);
            }
            else
            {
                item.source = fallback;
                delay = Rand.Range(24 * hour, 96 * hour);
            }

            // Confirmed when intel clearly exceeds what was needed, or the source is direct.
            bool direct = item.source == NewsSource.RadioIntercept || item.source == NewsSource.DiplomaticDispatch;
            item.confidence = direct || intel >= required + 30f ? NewsConfidence.Confirmed : NewsConfidence.Rumor;
            // With television (vanilla Tube television research), the world's news reaches the colony within hours.
            if (TelevisionResearched) delay = Mathf.Min(delay, Rand.Range(1 * hour, 4 * hour));
            if (DominionMod.Settings.instantNews) delay = 0;
            item.arrivesTick = now + delay;
            item.expiresTick = -1;
        }

        public override void Hourly()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            List<NewsItem> pending = comp.News.Pending;
            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            // News already on the slow road when television arrives (or instant news is switched on) catches up too.
            if (TelevisionResearched || DominionMod.Settings.instantNews)
            {
                int latest = DominionMod.Settings.instantNews ? now : now + 4 * GenDate.TicksPerHour;
                for (int i = 0; i < pending.Count; i++)
                    if (!pending[i].Hidden && pending[i].arrivesTick > latest) pending[i].arrivesTick = latest;
            }
            due.Clear();
            for (int i = 0; i < pending.Count; i++)
                if (!pending[i].Hidden && pending[i].arrivesTick <= now)
                    due.Add(pending[i]);
            for (int i = 0; i < due.Count; i++)
                Deliver(due[i], comp);
            due.Clear();
        }

        public static void Deliver(NewsItem item, WorldComponent_Dominion comp)
        {
            NewsText.Resolve(item);
            comp.News.MarkDelivered(item);
            NewsNotifier.Notify(item);
            NewsThoughts.Apply(item);
        }

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            int now = Find.TickManager.TicksGame;
            DominionSettings settings = DominionMod.Settings;

            // Hidden events: surface them if intel has grown, let secrets leak, drop old ones.
            List<NewsItem> pending = comp.News.Pending;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                NewsItem item = pending[i];
                if (!item.Hidden) continue;
                float intel = IntelUtility.IntelOn(item.primary, item.secondary);
                float required = IntelUtility.RequiredIntel(item.secrecy);
                if (intel >= required)
                {
                    Schedule(item, intel, required, comp, NewsSource.IntelReport);
                    continue;
                }
                float leak = item.secrecy == Secrecy.BlackOps ? 0.005f : item.secrecy == Secrecy.Secret ? 0.02f : 0.04f;
                if (Rand.Chance(leak))
                {
                    Schedule(item, intel, required, comp, NewsSource.Leak);
                    item.source = NewsSource.Leak;
                    item.confidence = NewsConfidence.Rumor;
                    continue;
                }
                if (now > item.expiresTick)
                    pending.RemoveAt(i);
            }

            if (settings.falseRumors)
            {
                TryCreateFalseRumor(comp);
                PublishCorrections(comp);
            }
        }

        private static readonly List<Faction> rumorCandidates = new List<Faction>();

        /// <summary>Little-known factions attract baseless rumors. More of them the less the player knows.</summary>
        public static NewsItem TryCreateFalseRumor(WorldComponent_Dominion comp, bool force = false)
        {
            rumorCandidates.Clear();
            foreach (Faction f in comp.Cache.Simulated)
                if (IntelUtility.IntelOn(f) < 40f)
                    rumorCandidates.Add(f);
            if (rumorCandidates.Count == 0) return null;
            if (!force && !Rand.Chance(Mathf.Min(0.1f, 0.01f * rumorCandidates.Count))) return null;

            Faction a = rumorCandidates.RandomElement();
            Faction b = null;
            DominionEventDef def;
            float roll = Rand.Value;
            if (roll < 0.35f)
            {
                def = DominionEventDefOf.Dominion_WarDeclared;
                b = comp.Cache.Simulated.Where(f => f != a && !a.HostileTo(f)).RandomElementWithFallback();
                if (b == null) def = DominionEventDefOf.Dominion_Riots;
            }
            else if (roll < 0.6f) def = DominionEventDefOf.Dominion_Riots;
            else if (roll < 0.8f) def = DominionEventDefOf.Dominion_Coup;
            else def = DominionEventDefOf.Dominion_LeaderDied;

            int now = Find.TickManager.TicksGame;
            NewsItem item = comp.News.Add(new NewsItem
            {
                def = def,
                occurredTick = now,
                primary = a,
                secondary = b,
                secrecy = Secrecy.Public,
                truthful = false,
                confidence = NewsConfidence.Rumor,
                source = NewsSource.TravelerGossip,
                arrivesTick = now + Rand.Range(GenDate.TicksPerHour * 2, GenDate.TicksPerDay * 2),
            });
            return item;
        }

        /// <summary>Once the player knows a faction well, false rumors about it are publicly corrected.</summary>
        private static void PublishCorrections(WorldComponent_Dominion comp)
        {
            IReadOnlyList<NewsItem> delivered = comp.News.Delivered;
            for (int i = delivered.Count - 1; i >= 0; i--)
            {
                NewsItem n = delivered[i];
                if (n.truthful || n.corrected || n.correctsId >= 0) continue;
                if (IntelUtility.IntelOn(n.primary) < CorrectionIntel) continue;
                PublishCorrection(n, comp, Rand.Range(GenDate.TicksPerHour, GenDate.TicksPerHour * 12));
            }
        }

        /// <summary>Marks a false rumour as corrected and sends out the correction after <paramref name="delay"/> ticks.</summary>
        public static void PublishCorrection(NewsItem n, WorldComponent_Dominion comp, int delay)
        {
            n.corrected = true;
            int now = Find.TickManager.TicksGame;
            comp.News.Add(new NewsItem
            {
                def = DominionEventDefOf.Dominion_NewsCorrection,
                occurredTick = now,
                primary = n.primary,
                secondary = n.secondary,
                secrecy = Secrecy.Public,
                confidence = NewsConfidence.Confirmed,
                source = NewsSource.Correction,
                correctsId = n.id,
                arrivesTick = now + delay,
            });
        }
    }
}
