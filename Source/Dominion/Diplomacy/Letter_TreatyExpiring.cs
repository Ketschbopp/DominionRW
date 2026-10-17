using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>A treaty with the player lapses in a few days. The letter takes you to the signing desk to renew it.</summary>
    public class Letter_TreatyExpiring : ChoiceLetter
    {
        public int treatyId = -1;

        private Treaty Treaty => WorldComponent_Dominion.Instance?.Treaties.FindById(treatyId);

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                Treaty t = Treaty;
                var renew = new DiaOption("Dominion_Treaty_Renew".Translate());
                if (t == null) renew.Disable("Dominion_Treaty_NoLongerInForce".Translate());
                else
                {
                    renew.action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_TreatyDraft(t.def, t.Other(Faction.OfPlayer), t));
                        Find.LetterStack.RemoveLetter(this);
                    };
                    renew.resolveTree = true;
                }
                yield return renew;
                if (t != null)
                {
                    yield return new DiaOption("Dominion_Treaties_ClickToRead".Translate())
                    {
                        action = () => Find.WindowStack.Add(new Dialog_TreatyDocument(t)),
                        resolveTree = true,
                    };
                }
                yield return Option_Close;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref treatyId, "treatyId", -1);
        }

        /// <summary>Sends the reminder for a treaty with the player that is about to lapse.</summary>
        public static void Send(Treaty t)
        {
            LetterDef def = DefDatabase<LetterDef>.GetNamedSilentFail("Dominion_TreatyExpiring");
            if (def == null) return;
            Faction other = t.Other(Faction.OfPlayer);
            var letter = (Letter_TreatyExpiring)LetterMaker.MakeLetter("Dominion_TreatyExpiring_Label".Translate(t.Label),
                "Dominion_TreatyExpiring_Text".Translate(t.Label, other.Name, t.DaysLeft), def, other, null);
            letter.treatyId = t.id;
            DominionLetters.Send(letter, important: true);
        }
    }
}
