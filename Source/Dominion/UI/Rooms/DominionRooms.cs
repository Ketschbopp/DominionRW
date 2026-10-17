using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The rooms behind the Dominion tabs: walnut panelling and brass for the telegraph office (Diplomacy), stone and
    /// shelves for the state archive (Treaties), concrete and lamp light for the war room (War), and the council chamber.
    /// Textures come from tools/textures/make_ui.py; the tileable ones repeat instead of stretching.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DominionRooms
    {
        public static readonly Texture2D Wood = Tiled("Wood");
        public static readonly Texture2D Stone = Tiled("Stone");
        public static readonly Texture2D Concrete = Tiled("Concrete");
        public static readonly Texture2D Cork = Tiled("Cork");
        public static readonly Texture2D Shelf = Tiled("Shelf");
        public static readonly Texture2D TickerTape = Tiled("TickerTape");
        public static readonly Texture2D Brass = Tex("Brass");
        public static readonly Texture2D Knob = Tex("Knob");
        public static readonly Texture2D Dial = Tex("Dial");
        public static readonly Texture2D Scroll = Tex("Scroll");
        public static readonly Texture2D Ribbon = Tex("Ribbon");
        public static readonly Texture2D RibbonFrayed = Tex("RibbonFrayed");
        public static readonly Texture2D Folder = Tex("Folder");
        public static readonly Texture2D Glow = Tex("Glow");
        public static readonly Texture2D Seat = Tex("Seat");
        public static readonly Texture2D Banner = Tex("Banner");

        public static readonly Color BrassInk = new Color(0.22f, 0.15f, 0.06f);
        public static readonly Color BrassLight = new Color(0.95f, 0.82f, 0.5f);
        public static readonly Color Lamp = new Color(1f, 0.82f, 0.55f);
        public static readonly Color Telegram = new Color(1f, 0.93f, 0.7f);

        private static Texture2D Tex(string name) => ContentFinder<Texture2D>.Get("UI/Dominion/" + name);

        private static Texture2D Tiled(string name)
        {
            Texture2D t = Tex(name);
            if (t != null) t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        /// <summary>A texture repeated over the rect at its natural size times <paramref name="scale"/>.</summary>
        public static void DrawTiled(Rect rect, Texture2D tex, float scale = 1f, Color? tint = null)
        {
            GUI.color = tint ?? Color.white;
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(0f, 0f, rect.width / (tex.width * scale), rect.height / (tex.height * scale)));
            GUI.color = Color.white;
        }

        /// <summary>A tileable strip repeated across the rect, stretched to its height.</summary>
        public static void DrawStrip(Rect rect, Texture2D tex, float offset = 0f, Color? tint = null)
        {
            GUI.color = tint ?? Color.white;
            float scale = rect.height / tex.height;
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(offset, 0f, rect.width / (tex.width * scale), 1f));
            GUI.color = Color.white;
        }

        /// <summary>Soft light pooled around a point, as from a lamp.</summary>
        public static void DrawGlow(Vector2 centre, float radius, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), Glow);
            GUI.color = Color.white;
        }

        /// <summary>Darkened corners, so the light falls on the middle of the room.</summary>
        public static void DrawVignette(Rect rect, float strength = 0.55f)
        {
            float edge = Mathf.Min(rect.width, rect.height) * 0.18f;
            for (int i = 0; i < 6; i++)
            {
                float a = strength * (1f - i / 6f) * 0.25f;
                float d = edge * i / 6f;
                Color c = new Color(0f, 0f, 0f, a);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + d, rect.width, edge / 6f), c);
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - d - edge / 6f, rect.width, edge / 6f), c);
                Widgets.DrawBoxSolid(new Rect(rect.x + d, rect.y, edge / 6f, rect.height), c);
                Widgets.DrawBoxSolid(new Rect(rect.xMax - d - edge / 6f, rect.y, edge / 6f, rect.height), c);
            }
        }

        /// <summary>A frame of moulding round a panel: a dark line, a lit line inside it.</summary>
        public static void DrawMoulding(Rect rect, Color? light = null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            Widgets.DrawBox(rect, 2);
            GUI.color = light ?? new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.35f);
            Widgets.DrawBox(rect.ContractedBy(2f));
            GUI.color = Color.white;
        }

        /// <summary>Four brass screw heads in the corners of a plate.</summary>
        public static void DrawScrews(Rect rect, float size = 6f)
        {
            foreach (Vector2 p in new[] { new Vector2(rect.x + 4f, rect.y + 4f), new Vector2(rect.xMax - 4f - size, rect.y + 4f),
                         new Vector2(rect.x + 4f, rect.yMax - 4f - size), new Vector2(rect.xMax - 4f - size, rect.yMax - 4f - size) })
                GUI.DrawTexture(new Rect(p.x, p.y, size, size), Knob);
        }

        /// <summary>An engraved brass nameplate.</summary>
        public static void BrassPlate(Rect rect, string label, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            GUI.DrawTexture(rect, Brass, ScaleMode.StretchToFill);
            if (rect.width > 60f && rect.height > 20f) DrawScrews(rect, 5f);
            DominionTheme.InkLabel(new Rect(rect.x + 10f, rect.y, rect.width - 20f, rect.height), label.Truncate(rect.width - 20f), BrassInk, font, anchor);
        }

        /// <summary>
        /// A brass switch: a plate that sinks when pressed and glows when <paramref name="lit"/>. Disabled switches are
        /// tarnished and say why in the tooltip.
        /// </summary>
        public static bool BrassButton(Rect rect, string label, string tooltip = null, string disabledReason = null, bool lit = false)
        {
            bool disabled = disabledReason != null;
            bool over = !disabled && Mouse.IsOver(rect);
            bool down = over && Input.GetMouseButton(0);
            Rect r = down ? new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 1f) : rect;
            GUI.color = disabled ? new Color(0.55f, 0.52f, 0.48f) : lit ? new Color(1f, 0.96f, 0.82f) : over ? new Color(1f, 0.97f, 0.9f) : new Color(0.88f, 0.85f, 0.8f);
            GUI.DrawTexture(r, Brass, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            if (lit) DrawGlow(r.center, Mathf.Max(r.width, r.height) * 0.6f, new Color(1f, 0.85f, 0.4f, 0.25f));
            GUI.color = new Color(0f, 0f, 0f, down ? 0.7f : 0.45f);
            Widgets.DrawBox(r);
            GUI.color = Color.white;
            DominionTheme.InkLabel(r, label.Truncate(rect.width - 8f), disabled ? new Color(0.3f, 0.27f, 0.22f, 0.6f) : BrassInk, GameFont.Small, TextAnchor.MiddleCenter);
            string tip = tooltip;
            if (disabled) tip = (tip.NullOrEmpty() ? "" : tip + "\n\n") + disabledReason.Colorize(ColorLibrary.RedReadable);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tip);
            if (disabled || !Widgets.ButtonInvisible(rect)) return false;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// A gauge with a needle: <paramref name="value"/> from -100 (far left, red) to +100 (far right, green), an
        /// optional second ghost needle (e.g. where the other side stands), and a caption under it.
        /// </summary>
        public static void DrawDial(Rect rect, float value, string caption, float? ghost = null, string tooltip = null)
        {
            float size = Mathf.Min(rect.width, rect.height * 1.25f);
            Rect face = new Rect(rect.center.x - size / 2f, rect.y, size, size);
            // Only the upper half of the face carries the scale.
            GUI.DrawTextureWithTexCoords(new Rect(face.x, face.y, face.width, face.height * 0.62f), Dial, new Rect(0f, 0.38f, 1f, 0.62f));
            Vector2 pivot = new Vector2(face.center.x, face.y + face.height * 0.5f);
            float len = size * 0.38f;
            if (ghost.HasValue) Needle(pivot, ghost.Value, len * 0.85f, new Color(0.2f, 0.3f, 0.6f, 0.6f), 2f);
            // The needle trembles a little, as needles do.
            float wobble = Mathf.Sin(Time.realtimeSinceStartup * 7f + value) * 0.6f;
            Needle(pivot, value + wobble, len, new Color(0.15f, 0.1f, 0.06f), 2.5f);
            if (!caption.NullOrEmpty())
                DominionTheme.InkLabel(new Rect(rect.x, face.y + face.height * 0.62f, rect.width, 18f), caption, BrassLight, GameFont.Tiny, TextAnchor.UpperCenter);
            if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tooltip);
        }

        private static void Needle(Vector2 pivot, float value, float len, Color c, float width)
        {
            float a = Mathf.Lerp(180f, 360f, (Mathf.Clamp(value, -100f, 100f) + 100f) / 200f) * Mathf.Deg2Rad;
            Widgets.DrawLine(pivot, pivot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len, c, width);
        }

        /// <summary>A telegram form: yellowed paper with a printed head and a perforated edge. Returns the writing area.</summary>
        public static Rect DrawTelegram(Rect rect, string head)
        {
            GUI.color = Telegram;
            GUI.DrawTexture(rect, DominionPaper.Parchment, ScaleMode.StretchToFill);
            GUI.color = new Color(0.4f, 0.3f, 0.15f, 0.7f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            Rect band = new Rect(rect.x, rect.y, rect.width, 26f);
            Widgets.DrawBoxSolid(band, new Color(0.55f, 0.18f, 0.12f, 0.85f));
            DominionTheme.InkLabel(band.ContractedBy(8f, 0f), head, new Color(1f, 0.95f, 0.85f), GameFont.Small, TextAnchor.MiddleLeft);
            for (float x = rect.x + 6f; x < rect.xMax - 4f; x += 10f)
                Widgets.DrawBoxSolid(new Rect(x, rect.yMax - 5f, 4f, 2f), new Color(0.4f, 0.3f, 0.15f, 0.35f));
            return new Rect(rect.x + 10f, band.yMax + 6f, rect.width - 20f, rect.height - band.height - 16f);
        }

        /// <summary>Monospaced-looking telegraph text: capitals, with STOP between sentences.</summary>
        public static string Telegraphese(string text)
        {
            if (text.NullOrEmpty()) return "";
            string t = text.StripTags().Trim().ToUpper();
            return t.Replace(". ", " STOP ").Replace(".\n", " STOP\n").TrimEnd('.') + " STOP";
        }
    }
}
