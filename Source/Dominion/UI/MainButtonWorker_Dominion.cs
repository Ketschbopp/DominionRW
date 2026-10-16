using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The Dominion tab button, with a small badge showing unread news.</summary>
    public class MainButtonWorker_Dominion : MainButtonWorker_ToggleTab
    {
        public override void DoButton(Rect rect)
        {
            base.DoButton(rect);
            int unread = WorldComponent_Dominion.Instance?.News.UnreadCount ?? 0;
            if (unread <= 0) return;

            string label = unread > 99 ? "99+" : unread.ToString();
            Text.Font = GameFont.Tiny;
            float w = Mathf.Max(18f, Text.CalcSize(label).x + 8f);
            Rect badge = new Rect(rect.xMax - w - 2f, rect.y + 2f, w, 16f);
            Widgets.DrawBoxSolid(badge, new Color(0.65f, 0.12f, 0.12f, 0.95f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badge, label);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
