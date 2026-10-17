using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The council's buttons on the god-mode page: call a session now, put any motion on the agenda (targets are the
    /// factions picked as A and B on that page), force each motion on the agenda to carry or fail, and wipe every breach.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class CouncilGodTools
    {
        static CouncilGodTools()
        {
            Dialog_DominionGodMode.ExtraCouncilTools.Add(Draw);
        }

        private static void Draw(Listing_Standard l)
        {
            CouncilState c = WorldComponent_Dominion.Instance.Council;
            l.GapLine();
            l.Label("Dominion_God_Council_Header".Translate());
            if (l.ButtonText("Dominion_God_Council_CallSession".Translate()))
            {
                CouncilSystem.DevCallSessionNow();
                Done();
            }
            if (l.ButtonText("Dominion_God_Council_AddMotion".Translate()))
                Find.WindowStack.Add(new FloatMenu(DefDatabase<CouncilMotionDef>.AllDefsListForReading.OrderBy(d => d.pack ?? "").ThenBy(d => d.label)
                    .Select(d => new FloatMenuOption(d.LabelCap + " (" + d.targetCount + ")", () => AddMotion(d))).ToList()));
            foreach (Motion m in c.agenda.ToList())
            {
                Motion motion = m;
                string state = m.devForce > 0 ? "Dominion_God_Council_ForcePass".Translate().ToString() : m.devForce < 0 ? "Dominion_God_Council_ForceFail".Translate().ToString() : "-";
                if (l.ButtonText("Dominion_God_Council_Force".Translate(m.Summary, state)))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption("Dominion_God_Council_ForcePass".Translate(), () => CouncilSystem.DevForce(motion, true)),
                        new FloatMenuOption("Dominion_God_Council_ForceFail".Translate(), () => CouncilSystem.DevForce(motion, false)),
                    }));
            }
            if (l.ButtonText("Dominion_God_Council_ClearViolations".Translate()))
            {
                CouncilSystem.DevClearViolations();
                Done();
            }
        }

        private static void AddMotion(CouncilMotionDef def)
        {
            var targets = new List<Faction>();
            if (def.targetCount >= 1) targets.Add(Dialog_DominionGodMode.PickedA);
            if (def.targetCount >= 2) targets.Add(Dialog_DominionGodMode.PickedB);
            string error = targets.Any(t => t == null) ? (string)"Dominion_Council_WrongTargets".Translate() : CouncilSystem.DevAddMotion(def, targets);
            if (error != null) Messages.Message(error, MessageTypeDefOf.RejectInput, historical: false);
            else Done();
        }

        private static void Done() => Messages.Message("Dominion_God_Council_Done".Translate(), MessageTypeDefOf.TaskCompletion, historical: false);
    }
}
