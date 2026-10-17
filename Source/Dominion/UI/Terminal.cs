using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The intelligence service's terminal: green phosphor on black, scanlines, bracketed commands and a blinking cursor.
    /// Used by the Intelligence tab.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Terminal
    {
        public static readonly Color Screen = new Color(0.02f, 0.05f, 0.03f);
        public static readonly Color Green = new Color(0.4f, 1f, 0.5f);
        public static readonly Color Dim = new Color(0.22f, 0.6f, 0.3f);
        public static readonly Color Faint = new Color(0.15f, 0.38f, 0.2f);
        public static readonly Color Amber = new Color(1f, 0.78f, 0.3f);
        public static readonly Color Alert = new Color(1f, 0.38f, 0.32f);

        private static readonly Texture2D Scanlines = MakeScanlines();

        private static Texture2D MakeScanlines()
        {
            var tex = new Texture2D(1, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.28f));
            tex.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            tex.SetPixel(0, 2, new Color(0f, 0f, 0f, 0f));
            tex.Apply();
            return tex;
        }

        /// <summary>The screen: black glass, a faint green bezel, scanlines over everything drawn before <see cref="EndScreen"/>.</summary>
        public static void BeginScreen(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Screen);
            GUI.color = new Color(Dim.r, Dim.g, Dim.b, 0.5f);
            Widgets.DrawBox(rect, 2);
            GUI.color = Color.white;
        }

        public static void EndScreen(Rect rect)
        {
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(rect, Scanlines, new Rect(0f, 0f, 1f, rect.height / 3f));
            // A slow bright band rolling down the glass.
            float t = Time.realtimeSinceStartup * 0.08f % 1f;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + rect.height * t, rect.width, 24f), new Color(Green.r, Green.g, Green.b, 0.025f));
        }

        public static void Label(Rect rect, string text, Color? color = null, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color ?? Green;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        public static string Cursor => Time.realtimeSinceStartup * 2f % 2f < 1f ? "_" : " ";

        /// <summary>A command in brackets. Hover inverts it; disabled commands are dim and say why.</summary>
        public static bool Button(Rect rect, string label, string tooltip = null, string disabledReason = null, bool selected = false, Color? color = null)
        {
            bool disabled = disabledReason != null;
            bool over = !disabled && Mouse.IsOver(rect);
            Color c = disabled ? Faint : color ?? Green;
            if (over || selected)
            {
                Widgets.DrawBoxSolid(rect, new Color(c.r, c.g, c.b, selected && !over ? 0.25f : 0.85f));
                Label(rect, "[ " + label.ToUpper() + " ]", over ? Screen : c, GameFont.Small, TextAnchor.MiddleCenter);
            }
            else Label(rect, "[ " + label.ToUpper() + " ]", c, GameFont.Small, TextAnchor.MiddleCenter);
            string tip = tooltip;
            if (disabled) tip = (tip.NullOrEmpty() ? "" : tip + "\n\n") + disabledReason.Colorize(ColorLibrary.RedReadable);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tip);
            if (disabled) return false;
            if (Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        /// <summary>A text progress bar: [#######-----] 58%.</summary>
        public static string Bar(float fraction, int cells = 16)
        {
            fraction = Mathf.Clamp01(fraction);
            int filled = Mathf.RoundToInt(fraction * cells);
            var sb = new StringBuilder("[");
            sb.Append('#', filled);
            sb.Append('-', cells - filled);
            sb.Append("] ");
            sb.Append(fraction.ToStringPercent());
            return sb.ToString();
        }

        /// <summary>A blacked-out line for what the service does not know yet.</summary>
        public static string Redacted(int length, float needed) =>
            new string('█', length) + "  " + "Dominion_Term_Clearance".Translate(needed.ToString("F0"));

        /// <summary>A thin rule of dashes across the screen.</summary>
        public static void Rule(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.center.y, rect.width, 1f), new Color(Dim.r, Dim.g, Dim.b, 0.6f));
        }
    }
}
