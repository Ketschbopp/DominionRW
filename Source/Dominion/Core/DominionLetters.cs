using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>Which of Dominion's notices arrive as letters (the stack on the right); the rest become messages.</summary>
    public enum LetterMode { All, Important, None }

    /// <summary>
    /// Sends Dominion's letters, as allowed by the Letters setting. A notice that may not be a letter is shown as a
    /// message instead (top left, kept in the message history), so nothing is lost.
    /// </summary>
    public static class DominionLetters
    {
        /// <summary>
        /// Whether a letter may be sent. <paramref name="important"/> marks a threat to the colony, or something the
        /// player has to act on soon (a summons, a duty with a deadline, a visitor waiting).
        /// </summary>
        public static bool Allowed(bool important)
        {
            LetterMode mode = DominionMod.Settings.letterMode;
            return mode == LetterMode.All || mode == LetterMode.Important && important;
        }

        public static void Send(TaggedString label, TaggedString text, LetterDef def, LookTargets look = null, Faction related = null, bool important = false)
        {
            if (Allowed(important))
            {
                Find.LetterStack.ReceiveLetter(label, text, def, look, related);
                return;
            }
            AsMessage(label, def, look);
        }

        /// <summary>Sends a ready-made letter (such as a choice letter), or its label as a message.</summary>
        public static void Send(Letter letter, bool important = false)
        {
            if (Allowed(important))
            {
                Find.LetterStack.ReceiveLetter(letter);
                return;
            }
            AsMessage(letter.Label, letter.def, letter.lookTargets);
        }

        public static void AsMessage(TaggedString label, LetterDef def, LookTargets look)
        {
            MessageTypeDef type = def == LetterDefOf.ThreatBig || def == LetterDefOf.ThreatSmall || def == LetterDefOf.NegativeEvent
                ? MessageTypeDefOf.NegativeEvent
                : def == LetterDefOf.PositiveEvent ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent;
            Messages.Message("Dominion_Letter_AsMessage".Translate(label), look ?? LookTargets.Invalid, type, historical: true);
        }
    }
}
