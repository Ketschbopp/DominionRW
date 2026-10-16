using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>Small shared UI helpers.</summary>
    public static class DominionUI
    {
        public static string GovernmentLabel(GovernmentType g) => ("Dominion_Government_" + g).Translate();

        public static string BlocLabel(Bloc b) => ("Dominion_Bloc_" + b).Translate();

        public static Color StabilityColor(float v)
        {
            if (v < 20f) return ColorLibrary.RedReadable;
            if (v < 40f) return new Color(1f, 0.6f, 0.2f);
            if (v < 60f) return Color.yellow;
            return ColorLibrary.Green;
        }

        public static Color OpinionColor(float v)
        {
            if (v <= -50f) return ColorLibrary.RedReadable;
            if (v < -10f) return new Color(1f, 0.6f, 0.2f);
            if (v < 10f) return Color.gray;
            if (v < 50f) return new Color(0.6f, 0.9f, 0.6f);
            return ColorLibrary.Green;
        }

        public static string Colored(string text, Color c) => text.Colorize(c);

        /// <summary>A labelled 0..1 bar on one line.</summary>
        public static void Bar(Rect rect, string label, float fraction, string valueText = null)
        {
            Rect labelRect = rect.LeftPart(0.45f);
            Rect barRect = rect.RightPart(0.55f).ContractedBy(0f, 4f);
            Widgets.Label(labelRect, label);
            Widgets.FillableBar(barRect, Mathf.Clamp01(fraction));
            if (valueText != null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(barRect, valueText);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }
    }
}
