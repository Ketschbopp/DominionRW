using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// The statecraft desk: parchment, wax seals, quills and rubber stamps for treaties, ballots, letters and charters.
    /// Builds on <see cref="DominionTheme"/>'s ink colours. Everything drawn on parchment uses ink, never white text.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class DominionPaper
    {
        public static readonly Texture2D Parchment = Tex("Parchment");
        public static readonly Texture2D WaxSeal = Tex("WaxSeal");
        public static readonly Texture2D Quill = Tex("Quill");
        public static readonly Texture2D Star = Tex("Star");
        public static readonly Texture2D StarOutline = Tex("StarOutline");
        public static readonly Texture2D Scissors = Tex("Scissors");
        public static readonly Texture2D[] Signatures = { Tex("Signature_0"), Tex("Signature_1"), Tex("Signature_2") };

        public static readonly Texture2D StampSigned = Tex("Stamp_Signed");
        public static readonly Texture2D StampRejected = Tex("Stamp_Rejected");
        public static readonly Texture2D StampCounter = Tex("Stamp_Counter");
        public static readonly Texture2D StampRatified = Tex("Stamp_Ratified");
        public static readonly Texture2D StampVoid = Tex("Stamp_Void");
        public static readonly Texture2D StampAye = Tex("Stamp_Aye");
        public static readonly Texture2D StampNay = Tex("Stamp_Nay");
        public static readonly Texture2D StampAbstain = Tex("Stamp_Abstain");
        public static readonly Texture2D StampCarried = Tex("Stamp_Carried");
        public static readonly Texture2D StampDefeated = Tex("Stamp_Defeated");
        public static readonly Texture2D StampTopSecret = Tex("Stamp_TopSecret");
        public static readonly Texture2D StampSuccess = Tex("Stamp_Success");
        public static readonly Texture2D StampFailed = Tex("Stamp_Failed");
        public static readonly Texture2D StampCompromised = Tex("Stamp_Compromised");
        public static readonly Texture2D StampRecalled = Tex("Stamp_Recalled");
        public static readonly Texture2D StampRenewed = Tex("Stamp_Renewed");
        public static readonly Texture2D StampPaid = Tex("Stamp_Paid");
        public static readonly Texture2D StampClipped = Tex("Stamp_Clipped");
        public static readonly Texture2D StampDelivered = Tex("Stamp_Delivered");

        private static readonly Texture2D OddsLow = SolidColorMaterials.NewSolidColorTexture(new Color(0.6f, 0.2f, 0.15f));
        private static readonly Texture2D OddsMid = SolidColorMaterials.NewSolidColorTexture(new Color(0.65f, 0.5f, 0.15f));
        private static readonly Texture2D OddsHigh = SolidColorMaterials.NewSolidColorTexture(new Color(0.25f, 0.5f, 0.25f));

        /// <summary>A bar fill for a chance on the dark window: red, amber or green.</summary>
        public static Texture2D OddsFill(float chance) => chance < 0.35f ? OddsLow : chance < 0.65f ? OddsMid : OddsHigh;

        public static readonly Color Edge = new Color(0.32f, 0.23f, 0.13f, 0.85f);
        public static readonly Color EdgeFaint = new Color(0.32f, 0.23f, 0.13f, 0.35f);
        public static readonly Color WaxRed = new Color(0.62f, 0.1f, 0.09f);

        private static Texture2D Tex(string name) => ContentFinder<Texture2D>.Get("UI/Dominion/" + name);

        // ------------------------------------------------------------------ sheets

        /// <summary>A sheet of parchment with a double ruled border, like a charter.</summary>
        public static void DrawParchment(Rect rect, bool border = true)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Parchment, ScaleMode.StretchToFill);
            if (!border) return;
            GUI.color = Edge;
            Widgets.DrawBox(rect, 2);
            GUI.color = EdgeFaint;
            Widgets.DrawBox(rect.ContractedBy(6f));
            GUI.color = Color.white;
        }

        /// <summary>A small card of paper on a dark panel, for lists of documents.</summary>
        public static void DrawCard(Rect rect, bool highlight = false)
        {
            DominionTheme.DrawPaper(rect);
            if (highlight) Widgets.DrawBoxSolid(rect, DominionTheme.PaperHighlight);
            else if (Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect, DominionTheme.PaperShade);
        }

        /// <summary>A centred title in ink with an ornamental rule under it. Returns the y below it.</summary>
        public static float Title(Rect rect, string text, Color? color = null)
        {
            Text.Font = GameFont.Medium;
            float h = Text.CalcHeight(text, rect.width);
            Text.Font = GameFont.Small;
            DominionTheme.InkLabel(new Rect(rect.x, rect.y, rect.width, h), text, color ?? DominionTheme.Ink, GameFont.Medium, TextAnchor.UpperCenter);
            DominionTheme.DrawRule(new Rect(rect.center.x - Mathf.Min(rect.width, 320f) / 2f, rect.y + h + 2f, Mathf.Min(rect.width, 320f), 12f), color);
            return rect.y + h + 18f;
        }

        /// <summary>A small capitals heading in red ink, as on a form.</summary>
        public static void Caption(Rect rect, string text) =>
            DominionTheme.InkLabel(rect, text.ToUpper(), DominionTheme.InkRed, GameFont.Tiny);

        /// <summary>Ink text that wraps; returns its height.</summary>
        public static float Paragraph(float x, float y, float width, string text, Color? color = null, GameFont font = GameFont.Small)
        {
            Text.Font = font;
            float h = Text.CalcHeight(text, width);
            Text.Font = GameFont.Small;
            DominionTheme.InkLabel(new Rect(x, y, width, h), text, color, font);
            return h;
        }

        // ------------------------------------------------------------------ controls

        /// <summary>
        /// A button drawn on paper: an ink box that darkens on hover. Disabled buttons are faded and show
        /// <paramref name="disabledReason"/> in the tooltip.
        /// </summary>
        public static bool InkButton(Rect rect, string label, string tooltip = null, string disabledReason = null, bool selected = false, Color? ink = null)
        {
            bool disabled = disabledReason != null;
            bool over = !disabled && Mouse.IsOver(rect);
            Color c = disabled ? new Color(0.45f, 0.4f, 0.34f, 0.6f) : ink ?? DominionTheme.Ink;
            if (selected) Widgets.DrawBoxSolid(rect, new Color(c.r, c.g, c.b, 0.22f));
            else if (over) Widgets.DrawBoxSolid(rect, DominionTheme.PaperShade);
            GUI.color = new Color(c.r, c.g, c.b, disabled ? 0.4f : 0.85f);
            Widgets.DrawBox(rect, selected ? 2 : 1);
            GUI.color = Color.white;
            Rect r = over && Input.GetMouseButton(0) ? rect.ContractedBy(1f) : rect;
            DominionTheme.InkLabel(r, label.Truncate(rect.width - 6f), c, GameFont.Small, TextAnchor.MiddleCenter);
            string tip = tooltip;
            if (disabled) tip = (tip.NullOrEmpty() ? "" : tip + "\n\n") + disabledReason.Colorize(ColorLibrary.RedReadable);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tip);
            if (disabled) return false;
            if (Widgets.ButtonInvisible(rect))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        /// <summary>A row of ink options, one chosen. Returns the chosen index (changed if clicked).</summary>
        public static int Choice(Rect rect, string[] labels, int current, string[] tooltips = null, string[] disabled = null)
        {
            float w = rect.width / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect r = new Rect(rect.x + i * w + 1f, rect.y, w - 2f, rect.height);
                if (InkButton(r, labels[i], tooltips?[i], disabled?[i], i == current) && i != current) current = i;
            }
            return current;
        }

        /// <summary>A tick box in ink.</summary>
        public static bool InkCheckbox(Rect rect, string label, bool value, string tooltip = null, string disabledReason = null)
        {
            Rect box = new Rect(rect.x, rect.y + (rect.height - 18f) / 2f, 18f, 18f);
            Color c = disabledReason != null ? DominionTheme.InkFaded : DominionTheme.Ink;
            GUI.color = c;
            Widgets.DrawBox(box, 2);
            GUI.color = Color.white;
            if (value)
            {
                Widgets.DrawLine(new Vector2(box.x + 3f, box.center.y), new Vector2(box.x + 7f, box.yMax - 3f), DominionTheme.InkRed, 2.5f);
                Widgets.DrawLine(new Vector2(box.x + 7f, box.yMax - 3f), new Vector2(box.xMax - 2f, box.y + 2f), DominionTheme.InkRed, 2.5f);
            }
            DominionTheme.InkLabel(new Rect(box.xMax + 6f, rect.y, rect.width - 24f, rect.height), label, c, GameFont.Small, TextAnchor.MiddleLeft);
            if (Mouse.IsOver(rect) && disabledReason == null) Widgets.DrawBoxSolid(rect, DominionTheme.PaperShade);
            string tip = tooltip;
            if (disabledReason != null) tip = (tip.NullOrEmpty() ? "" : tip + "\n\n") + disabledReason.Colorize(ColorLibrary.RedReadable);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tip);
            if (disabledReason == null && Widgets.ButtonInvisible(rect))
            {
                (value ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
                return !value;
            }
            return value;
        }

        /// <summary>
        /// The odds that the other side agrees, as an inked gauge: red when unlikely, amber when it could go either way,
        /// green when likely. <paramref name="breakdown"/> explains the number on hover.
        /// </summary>
        public static void OddsMeter(Rect rect, float chance, string breakdown, string label = null)
        {
            chance = Mathf.Clamp01(chance);
            Color c = chance < 0.35f ? DominionTheme.InkRed : chance < 0.65f ? DominionTheme.InkAmber : DominionTheme.InkGreen;
            Widgets.DrawBoxSolid(rect, new Color(0.3f, 0.24f, 0.17f, 0.15f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * chance, rect.height), new Color(c.r, c.g, c.b, 0.6f));
            GUI.color = Edge;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            string text = (label ?? "Dominion_Paper_Odds".Translate().ToString()) + ": " + chance.ToStringPercent();
            DominionTheme.InkLabel(rect, text, DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleCenter);
            if (!breakdown.NullOrEmpty()) TooltipHandler.TipRegion(rect, breakdown);
        }

        /// <summary>Adds a "+5%: reason" line to an odds breakdown when the term is not zero.</summary>
        public static void Term(System.Text.StringBuilder sb, float value, string reason)
        {
            if (Mathf.Abs(value) < 0.005f) return;
            string v = (value > 0f ? "+" : "") + value.ToStringPercent();
            sb.AppendLine("  " + v.Colorize(value > 0f ? ColorLibrary.Green : ColorLibrary.RedReadable) + "  " + reason);
        }

        // ------------------------------------------------------------------ seals and signatures

        /// <summary>A wax seal in the faction's colour with its emblem pressed in. Null faction: Dominion red.</summary>
        public static void DrawSeal(Rect rect, Faction f, float alpha = 1f)
        {
            Color wax = f != null ? Darken(f.Color, 0.75f) : WaxRed;
            wax.a = alpha;
            DominionTheme.DrawInk(rect, WaxSeal, wax);
            Texture2D icon = f?.def?.FactionIcon;
            if (icon == null) icon = DominionTheme.Seal;
            Color pressed = Darken(wax, 0.55f);
            pressed.a = 0.85f * alpha;
            DominionTheme.DrawInk(rect.ContractedBy(rect.width * 0.27f), icon, pressed);
        }

        public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>A signature revealed left to right as if being written (<paramref name="progress"/> 0..1).</summary>
        public static void DrawSignature(Rect rect, int which, float progress, Color? ink = null)
        {
            progress = Mathf.Clamp01(progress);
            if (progress <= 0f) return;
            Texture2D tex = Signatures[Mathf.Abs(which) % Signatures.Length];
            Color old = GUI.color;
            GUI.color = ink ?? new Color(0.12f, 0.12f, 0.3f, 0.9f);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y, rect.width * progress, rect.height), tex, new Rect(0f, 0f, progress, 1f));
            GUI.color = old;
        }

        /// <summary>
        /// A dotted signature line with the signatory's name and title written under it.
        /// </summary>
        public static void SignatureLine(Rect rect, string name, string title)
        {
            float lineY = rect.yMax - 30f;
            for (float x = rect.x; x < rect.xMax; x += 6f)
                Widgets.DrawLineHorizontal(x, lineY, 3f, DominionTheme.InkFaded);
            DominionTheme.InkLabel(new Rect(rect.x, lineY + 1f, rect.width, 16f), name, DominionTheme.Ink, GameFont.Tiny, TextAnchor.UpperCenter);
            DominionTheme.InkLabel(new Rect(rect.x, lineY + 14f, rect.width, 16f), title, DominionTheme.InkFaded, GameFont.Tiny, TextAnchor.UpperCenter);
        }

        /// <summary>A stamp that slams down: big and faint at first, settling at full size in a fifth of a second.</summary>
        public static void SlamStamp(Rect rect, Texture2D stamp, Color color, float angle, float t)
        {
            float k = Mathf.Clamp01(t / 0.18f);
            float scale = Mathf.Lerp(1.7f, 1f, k);
            Rect r = new Rect(rect.center.x - rect.width * scale / 2f, rect.center.y - rect.height * scale / 2f, rect.width * scale, rect.height * scale);
            Matrix4x4 m = GUI.matrix;
            UI.RotateAroundPivot(angle, r.center);
            DominionTheme.DrawInk(r, stamp, new Color(color.r, color.g, color.b, 0.25f + 0.65f * k));
            GUI.matrix = m;
        }

        /// <summary>A wax seal dropping onto the page.</summary>
        public static void DropSeal(Rect rect, Faction f, float t)
        {
            float k = Mathf.Clamp01(t / 0.22f);
            float scale = Mathf.Lerp(1.5f, 1f, k);
            Rect r = new Rect(rect.center.x - rect.width * scale / 2f, rect.center.y - rect.height * scale / 2f, rect.width * scale, rect.height * scale);
            DrawSeal(r, f, 0.3f + 0.7f * k);
        }

        /// <summary>The player's signatory: the head of state, else the best diplomat, else the colony's name.</summary>
        public static string PlayerSignatory(out string title)
        {
            Pawn hos = LeadershipUtility.HeadOfState();
            if (hos != null)
            {
                title = "Dominion_Paper_HeadOfStateOf".Translate(Faction.OfPlayer.Name);
                return hos.Name?.ToStringFull ?? hos.LabelShortCap;
            }
            title = "Dominion_Paper_ForThePeopleOf".Translate(Faction.OfPlayer.Name);
            return Faction.OfPlayer.Name;
        }

        /// <summary>The other side's signatory: its leader if known, else "the government of".</summary>
        public static string Signatory(Faction f, out string title)
        {
            title = f.leader != null && f.def.leaderTitle != null
                ? f.def.leaderTitle.CapitalizeFirst() + ", " + f.Name
                : "Dominion_Paper_ForTheGovernmentOf".Translate(f.Name).ToString();
            return f.leader?.Name?.ToStringFull ?? f.Name;
        }

        /// <summary>A short date like "5th of Aprimay, 5502" for papers.</summary>
        public static string Date(int tick)
        {
            Map map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Vector2 longLat = map != null ? Find.WorldGrid.LongLatOf(map.Tile) : Vector2.zero;
            return GenDate.DateFullStringAt(GenDate.TickGameToAbs(tick), longLat);
        }
    }

    /// <summary>
    /// The signing ceremony on a document: you sign with the quill, the other side reads it over, then presses its
    /// seal (accepted), stamps REJECTED, or writes in a counter-offer. The caller decides the outcome in
    /// <see cref="Decide"/> once the reading is done.
    /// </summary>
    public class SealCeremony
    {
        public enum Stage { Draft, Signing, Reading, Accepted, Refused, Countered }

        private const float SignSeconds = 0.9f;
        private const float ReadSeconds = 0.9f;

        public Stage stage = Stage.Draft;
        private float since;
        public readonly int signature = Rand.RangeInclusive(0, 2);

        public float T => Time.realtimeSinceStartup - since;

        public bool Busy => stage == Stage.Signing || stage == Stage.Reading;
        public bool Done => stage == Stage.Accepted || stage == Stage.Refused;

        public void Sign()
        {
            Set(Stage.Signing);
            SoundDefOf.Designate_DragStandard_Changed.PlayOneShotOnCamera();
        }

        public void Set(Stage s)
        {
            stage = s;
            since = Time.realtimeSinceStartup;
            if (s == Stage.Accepted) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            else if (s == Stage.Refused || s == Stage.Countered) SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>Advances the animation. Calls <paramref name="decide"/> once, when the other side has read it.</summary>
        public void Update(Func<Stage> decide)
        {
            if (stage == Stage.Signing && T > SignSeconds) Set(Stage.Reading);
            else if (stage == Stage.Reading && T > ReadSeconds) Set(decide());
        }

        /// <summary>The player's signature over the line: written while signing, complete afterwards.</summary>
        public void DrawOurSignature(Rect line)
        {
            if (stage == Stage.Draft) return;
            float p = stage == Stage.Signing ? T / SignSeconds : 1f;
            DominionPaper.DrawSignature(new Rect(line.x + 4f, line.yMax - 30f - line.height * 0.55f, line.width - 8f, line.height * 0.55f), signature, p);
        }

        /// <summary>The other side's answer on their half of the page: their signature and seal, or a stamp.</summary>
        public void DrawTheirAnswer(Rect line, Faction f)
        {
            switch (stage)
            {
                case Stage.Reading:
                    int dots = 1 + (int)(T * 3f) % 3;
                    DominionTheme.InkLabel(new Rect(line.x, line.yMax - 56f, line.width, 20f), "Dominion_Paper_Reading".Translate() + new string('.', dots),
                        DominionTheme.InkFaded, GameFont.Small, TextAnchor.MiddleCenter);
                    break;
                case Stage.Accepted:
                    DominionPaper.DrawSignature(new Rect(line.x + 4f, line.yMax - 30f - line.height * 0.55f, line.width - 8f, line.height * 0.55f),
                        signature + 1, T / 0.6f, new Color(0.25f, 0.1f, 0.08f, 0.9f));
                    float s = Mathf.Min(64f, line.height);
                    DominionPaper.DropSeal(new Rect(line.xMax - s * 0.7f, line.y - s * 0.35f, s, s), f, T - 0.4f);
                    break;
                case Stage.Refused:
                    DominionPaper.SlamStamp(new Rect(line.x + 4f, line.yMax - 70f, line.width - 8f, 46f), DominionPaper.StampRejected, DominionTheme.InkRed, -7f, T);
                    break;
                case Stage.Countered:
                    DominionPaper.SlamStamp(new Rect(line.x + 4f, line.yMax - 70f, line.width - 8f, 46f), DominionPaper.StampCounter, DominionTheme.InkAmber, 5f, T);
                    break;
            }
        }
    }

    /// <summary>A pop-up drawn as a sheet of parchment instead of a grey window.</summary>
    public abstract class PaperWindow : Window
    {
        protected PaperWindow()
        {
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doWindowBackground = false;
            drawShadow = true;
        }

        protected override float Margin => 0f;

        public override void DoWindowContents(Rect inRect)
        {
            DominionPaper.DrawParchment(inRect);
            DoPaper(inRect.ContractedBy(26f, 22f));
        }

        protected abstract void DoPaper(Rect rect);
    }
}
