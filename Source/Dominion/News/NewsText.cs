using System;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace Dominion
{
    /// <summary>
    /// Writes headlines and stories with vanilla's grammar engine. Each event def can name a RulePackDef
    /// providing "headline" and "story"; the shared Dominion_NewsCommon pack adds reusable phrases.
    /// Symbols available to rule packs: FACTION_A, FACTION_B, LEADER_A, LEADER_B, GOV_A, GOV_B,
    /// CAPITAL_A, LEAD (an opening that reflects confidence), SOURCE, DETAIL and DETAIL_INDEF (DETAIL with "a"/"an").
    /// </summary>
    public static class NewsText
    {
        public static void Resolve(NewsItem n)
        {
            if (n.def == null) return;
            n.advice = NewsFlavor.Advice(n);
            if (n.def.newsRules == null)
            {
                n.headline = Fallback(n);
                n.story = n.def.description;
                return;
            }

            GrammarRequest req = default;
            req.Includes.Add(n.def.newsRules);
            if (DominionRulePackDefOf.Dominion_NewsCommon != null)
                req.Includes.Add(DominionRulePackDefOf.Dominion_NewsCommon);

            AddFaction(ref req, "A", n.primary);
            AddFaction(ref req, "B", n.secondary);
            req.Rules.Add(new Rule_String("LEAD", (n.confidence == NewsConfidence.Confirmed
                ? "Dominion_News_LeadConfirmed" : "Dominion_News_LeadRumor").Translate()));
            req.Rules.Add(new Rule_String("SOURCE", SourceLabel(n.source)));
            req.Rules.Add(new Rule_String("DETAIL", n.detail ?? ""));
            req.Rules.Add(new Rule_String("DETAIL_INDEF", n.detail.NullOrEmpty() ? "" : Find.ActiveLanguageWorker.WithIndefiniteArticle(n.detail)));

            try
            {
                n.headline = GrammarResolver.Resolve("headline", req, "Dominion news " + n.def.defName, false, null, null, null, true);
                n.story = GrammarResolver.Resolve("story", req, "Dominion news " + n.def.defName, false, null, null, null, true);
            }
            catch (Exception e)
            {
                DLog.WarningOnce($"News grammar for {n.def.defName} failed: {e}", n.def.shortHash ^ 0x3E5);
            }
            if (n.headline.NullOrEmpty()) n.headline = Fallback(n);
            if (n.story.NullOrEmpty()) n.story = n.def.description;
            AddQuote(n);
            NewsFlavor.Decorate(n, req);
        }

        /// <summary>
        /// Stories about a faction's politics, wars and diplomacy end with a word from its leader, in the faction's own
        /// voice (from its memes and government): a threat in war, a refusal or a welcome in diplomacy.
        /// </summary>
        private static void AddQuote(NewsItem n)
        {
            Faction f = n.primary;
            if (f == null || f.IsPlayer || f.leader == null || n.confidence == NewsConfidence.Rumor) return;
            VoiceLine line;
            switch (n.def.category)
            {
                case EventCategory.War: line = VoiceLine.Threat; break;
                case EventCategory.Diplomacy:
                case EventCategory.Council: line = n.secondary != null && n.secondary.HostileTo(f) ? VoiceLine.Refuse : VoiceLine.Accept; break;
                case EventCategory.Politics: line = VoiceLine.Greeting; break;
                default: return;
            }
            n.story += "\n\n" + "Dominion_News_Quote".Translate(f.leader.LabelShort, FactionVoice.Line(f, line, n.id));
        }

        private static string Fallback(NewsItem n)
        {
            string a = n.primary?.Name ?? "Dominion_News_UnknownParty".Translate();
            string b = n.secondary?.Name ?? "Dominion_News_UnknownParty".Translate();
            return n.def.headline.NullOrEmpty() ? n.def.LabelCap.ToString() : string.Format(n.def.headline, a, b, n.detail ?? "");
        }

        private static void AddFaction(ref GrammarRequest req, string suffix, Faction f)
        {
            string unknown = "Dominion_News_UnknownParty".Translate();
            req.Rules.Add(new Rule_String("FACTION_" + suffix, f?.Name ?? unknown));
            req.Rules.Add(new Rule_String("LEADER_" + suffix, f?.leader?.LabelShort ?? "Dominion_News_ItsLeadership".Translate()));
            req.Rules.Add(new Rule_String("LEADER_" + suffix + "_APPOS", f?.leader != null ? ", " + f.leader.LabelShort + "," : ""));

            string gov = unknown;
            string capital = "Dominion_News_UnknownCapital".Translate();
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (f != null && comp != null && comp.TryGetState(f, out FactionState s))
            {
                if (s.governmentInitialized) gov = DominionUI.GovernmentLabel(s.government).ToLower();
                if (s.capital != null) capital = s.capital.Label;
            }
            req.Rules.Add(new Rule_String("GOV_" + suffix, gov));
            req.Rules.Add(new Rule_String("GOV_" + suffix + "_INDEF", gov == unknown
                ? "Dominion_News_NewGovernment".Translate().ToString() : Find.ActiveLanguageWorker.WithIndefiniteArticle(gov)));
            req.Rules.Add(new Rule_String("CAPITAL_" + suffix, capital));
        }

        public static string SourceLabel(NewsSource s) => ("Dominion_NewsSource_" + s).Translate();
    }

    [DefOf]
    public static class DominionRulePackDefOf
    {
        public static RulePackDef Dominion_NewsCommon;

        static DominionRulePackDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DominionRulePackDefOf));
        }
    }
}
