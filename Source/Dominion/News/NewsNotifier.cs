using RimWorld;
using Verse;

namespace Dominion
{
    public enum NewsNotifyMode { Silent, Message, Letter }

    /// <summary>Tells the player about delivered news, per category, as set in Dominion's News settings.</summary>
    public static class NewsNotifier
    {
        public const float MajorNewsworthiness = 0.85f;

        public static void Notify(NewsItem n)
        {
            DominionSettings settings = DominionMod.Settings;
            NewsNotifyMode mode = settings.NotifyModeFor(n.Category);
            if (mode == NewsNotifyMode.Silent) return;
            if (mode == NewsNotifyMode.Message && settings.letterForMajorNews && n.def.newsworthiness >= MajorNewsworthiness)
                mode = NewsNotifyMode.Letter;

            if (mode == NewsNotifyMode.Letter)
            {
                string text = n.story + "\n\n" + "Dominion_News_LetterFooter".Translate(
                    NewsText.SourceLabel(n.source), ConfidenceLabel(n.confidence));
                Find.LetterStack.ReceiveLetter(n.headline.Truncate(260f), text, LetterFor(n), null, n.primary, null, null, null, 0, true);
            }
            else
            {
                Messages.Message("Dominion_News_Message".Translate(n.headline), MessageTypeDefOf.NeutralEvent, historical: true);
            }
        }

        public static string ConfidenceLabel(NewsConfidence c) => ("Dominion_NewsConfidence_" + c).Translate();

        private static LetterDef LetterFor(NewsItem n)
        {
            Faction player = Faction.OfPlayerSilentFail;
            bool allyHit = player != null && n.secondary != null && n.secondary.RelationKindWith(player) == FactionRelationKind.Ally
                           && (n.def == DominionEventDefOf.Dominion_WarDeclared || n.def == DominionEventDefOf.Dominion_CapitalLost);
            bool playerHit = n.secondary != null && n.secondary.IsPlayer && n.def == DominionEventDefOf.Dominion_WarDeclared;
            return allyHit || playerHit ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent;
        }
    }
}
