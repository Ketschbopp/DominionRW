using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The letterhead across the top of the Dominion window: seal and title, the state of your realm at a glance
    /// (head of state, influence, capital, wars), and a ticker with the latest headline that opens the newspaper.
    /// </summary>
    public static class DominionHeader
    {
        public const float Height = 70f;

        // Counting wars and unread news is cheap, but there is no need to do it every frame.
        private static readonly UICache<string> statsCache = new UICache<string>(Stats, 1f);

        public static void Draw(Rect rect, WorldComponent_Dominion comp, Action openNews)
        {
            DominionTheme.DrawPaper(rect);
            Rect inner = rect.ContractedBy(6f, 5f);

            // Seal and title, like a letterhead.
            Rect seal = new Rect(inner.x, inner.y, inner.height, inner.height);
            GUI.DrawTexture(seal, DominionTheme.Seal);
            float titleH = inner.height * 0.62f;
            Rect title = new Rect(seal.xMax + 6f, inner.y, titleH * (640f / 120f), titleH);
            DominionTheme.DrawInk(title, DominionTheme.Title, DominionTheme.InkRed);
            DominionTheme.InkLabel(new Rect(title.x + 6f, title.yMax - 2f, title.width, 16f),
                "Dominion_Header_Subtitle".Translate(Faction.OfPlayer.Name), DominionTheme.InkFaded, GameFont.Tiny);

            if (comp == null) return;
            float x = title.xMax + 14f;
            GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.45f);
            Widgets.DrawLineVertical(x - 7f, inner.y + 2f, inner.height - 4f);
            GUI.color = Color.white;

            // Middle: head of state, capital, date.
            float midW = Mathf.Max(160f, (inner.xMax - x) * 0.45f);
            Pawn hos = LeadershipUtility.HeadOfState();
            string head = "Dominion_Header_HeadOfState".Translate(hos != null ? hos.LabelShortCap : "Dominion_None".Translate().ToString());
            DominionTheme.InkLabel(new Rect(x, inner.y, midW, 20f), head.Truncate(midW));
            string capital = comp.Player.capital != null && !comp.Player.capital.Destroyed ? comp.Player.capital.Label : "Dominion_None".Translate().ToString();
            DominionTheme.InkLabel(new Rect(x, inner.y + 19f, midW, 18f), ("Dominion_Header_Capital".Translate(capital)).Truncate(midW), DominionTheme.InkFaded, GameFont.Tiny);
            DominionTheme.InkLabel(new Rect(x, inner.y + 34f, midW, 18f), DominionTheme.TodayLong().Truncate(midW), DominionTheme.InkFaded, GameFont.Tiny);

            // Right: influence as an ink-filled gauge, and the realm in numbers.
            float rx = x + midW + 10f;
            float rw = inner.xMax - rx;
            Rect gauge = new Rect(rx, inner.y + 2f, rw, 16f);
            float frac = comp.Player.influence / PlayerState.MaxInfluence;
            Widgets.DrawBoxSolid(gauge, new Color(0.3f, 0.24f, 0.17f, 0.18f));
            Widgets.DrawBoxSolid(new Rect(gauge.x, gauge.y, gauge.width * frac, gauge.height), new Color(DominionTheme.InkRed.r, DominionTheme.InkRed.g, DominionTheme.InkRed.b, 0.75f));
            GUI.color = new Color(0.2f, 0.16f, 0.12f, 0.7f);
            Widgets.DrawBox(gauge);
            GUI.color = Color.white;
            DominionTheme.InkLabel(gauge, "Dominion_Header_Influence".Translate(comp.Player.influence.ToString("F0"), PlayerState.MaxInfluence.ToString("F0")),
                frac > 0.55f ? new Color(0.98f, 0.94f, 0.85f) : DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(gauge, "Dominion_Dip_Influence_Desc".Translate());
            DominionTheme.InkLabel(new Rect(rx, inner.y + 20f, rw, 18f), statsCache.Value.Truncate(rw), DominionTheme.Ink, GameFont.Tiny);

            // Ticker: the latest headline that reached the colony. Click to read it.
            NewsItem latest = comp.News.Delivered.Count > 0 ? comp.News.Delivered[comp.News.Delivered.Count - 1] : null;
            Rect ticker = new Rect(rx, inner.y + 37f, rw, 20f);
            if (latest != null)
            {
                string label = "Dominion_Header_Latest".Translate() + " ";
                Text.Font = GameFont.Tiny;
                float lw = Text.CalcSize(label).x;
                Text.Font = GameFont.Small;
                DominionTheme.InkLabel(new Rect(ticker.x, ticker.y + 2f, lw, 18f), label, DominionTheme.InkRed, GameFont.Tiny);
                Rect head2 = new Rect(ticker.x + lw, ticker.y, ticker.width - lw, ticker.height);
                if (Mouse.IsOver(ticker)) Widgets.DrawBoxSolid(ticker, DominionTheme.PaperShade);
                DominionTheme.InkLabel(head2, latest.headline.Truncate(head2.width), latest.read ? DominionTheme.InkFaded : DominionTheme.Ink);
                TooltipHandler.TipRegion(ticker, latest.headline);
                if (Widgets.ButtonInvisible(ticker)) openNews?.Invoke();
            }
        }

        private static string Stats()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (comp == null) return "";
            int wars = 0, mine = 0;
            foreach (War w in comp.Wars.wars)
            {
                wars++;
                if (w.InvolvesPlayer) mine++;
            }
            int treaties = 0;
            foreach (Treaty t in comp.Treaties.All)
                if (t.Involves(Faction.OfPlayer)) treaties++;
            return "Dominion_Header_Stats".Translate(mine, wars, treaties, comp.Cache.Simulated.Count, comp.News.UnreadCount);
        }
    }
}
