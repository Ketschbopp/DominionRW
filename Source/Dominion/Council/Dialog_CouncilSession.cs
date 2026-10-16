using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A council session the player attends: vote on every motion, spend influence lobbying members, then close
    /// the session to count the votes. In dev mode, a veto button exists for testing; in play, no one has a veto.
    /// </summary>
    public class Dialog_CouncilSession : Window
    {
        private const float RowHeight = 76f;
        private const float LobbyCost = 10f;
        private const float LobbyShift = 0.35f;

        private readonly Pawn speaker;
        private readonly Dictionary<Motion, int> votes = new Dictionary<Motion, int>();
        private readonly HashSet<string> lobbied = new HashSet<string>();
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(900f, 680f);

        public Dialog_CouncilSession(Pawn speaker)
        {
            this.speaker = speaker;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnCancel = false;
            closeOnClickedOutside = false;
            doCloseX = false;
        }

        private static CouncilState State => WorldComponent_Dominion.Instance.Council;

        public override void DoWindowContents(Rect inRect)
        {
            CouncilState c = State;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Dominion_Council_SessionTitle".Translate(c.host?.Name ?? "?"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width, 24f),
                "Dominion_Council_SessionInfo".Translate(speaker.LabelShortCap, c.attending.Count,
                    CouncilUtility.VoteWeight(Faction.OfPlayer, c.attending).ToString("F1"),
                    WorldComponent_Dominion.Instance.Player.influence.ToString("F0")));

            Rect outRect = new Rect(inRect.x, inRect.y + 66f, inRect.width, inRect.height - 66f - 50f);
            if (c.agenda.Count == 0)
                Widgets.Label(outRect, "Dominion_Council_NoMotions".Translate());
            Rect view = new Rect(0f, 0f, outRect.width - 16f, c.agenda.Count * (RowHeight + 6f));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < c.agenda.Count; i++)
                DrawMotion(new Rect(0f, i * (RowHeight + 6f), view.width, RowHeight), c.agenda[i]);
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(inRect.center.x - 120f, inRect.yMax - 40f, 240f, 36f), "Dominion_Council_CloseSession".Translate()))
            {
                CouncilSystem.Resolve(votes);
                Close();
            }
        }

        private void DrawMotion(Rect rect, Motion m)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(6f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.62f, 24f), m.Summary.Truncate(inner.width * 0.62f));
            TooltipHandler.TipRegion(new Rect(inner.x, inner.y, inner.width * 0.62f, 24f), m.def.description);
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width * 0.62f, 22f), "Dominion_Council_ProposedBy".Translate(m.sponsor?.Name ?? "?"));
            GUI.color = Color.white;

            CouncilSystem.Tally(m, votes, out float yes, out float no);
            Widgets.Label(new Rect(inner.x, inner.y + 46f, inner.width * 0.62f, 22f),
                (m.vetoed ? "Dominion_Council_Vetoed".Translate().ToString() + " " : "") + "Dominion_Council_Projected".Translate(yes.ToString("F1"), no.ToString("F1")));

            // Player's vote
            int vote = votes.TryGetValue(m, out int v) ? v : 0;
            float x = inner.x + inner.width * 0.64f;
            float bw = (inner.width * 0.36f - 8f) / 3f;
            foreach (int option in new[] { 1, 0, -1 })
            {
                Rect b = new Rect(x, inner.y, bw, 28f);
                string label = (option == 1 ? "Dominion_Council_Yes" : option == 0 ? "Dominion_Council_Abstain" : "Dominion_Council_No").Translate();
                if (vote == option) Widgets.DrawHighlightSelected(b);
                if (Widgets.ButtonText(b, label, drawBackground: vote != option)) votes[m] = option;
                x += bw + 4f;
            }

            Rect lobby = new Rect(inner.x + inner.width * 0.64f, inner.y + 34f, inner.width * 0.36f * (Prefs.DevMode ? 0.6f : 1f) - 4f, 28f);
            if (vote == 0)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(lobby, "Dominion_Council_Lobby".Translate(LobbyCost.ToString("F0")), active: false);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(lobby, "Dominion_Council_LobbyNeedsVote".Translate());
            }
            else if (Widgets.ButtonText(lobby, "Dominion_Council_Lobby".Translate(LobbyCost.ToString("F0"))))
                LobbyMenu(m, vote);

            if (Prefs.DevMode && Widgets.ButtonText(new Rect(lobby.xMax + 4f, lobby.y, inner.xMax - lobby.xMax - 4f, 28f), "Dominion_Council_DevVeto".Translate()))
                CouncilSystem.DevVeto(m);
        }

        private void LobbyMenu(Motion m, int direction)
        {
            var options = new List<FloatMenuOption>();
            float chance = Mathf.Clamp(0.4f + DiplomatUtility.Social(speaker) * 0.03f, 0.1f, 0.95f);
            foreach (Faction f in State.attending)
            {
                if (f.IsPlayer) continue;
                string key = f.loadID + ":" + m.GetHashCode();
                int leaning = CouncilUtility.AIVote(f, m);
                string label = "Dominion_Council_LobbyOption".Translate(f.Name, LeaningLabel(leaning), chance.ToStringPercent());
                if (lobbied.Contains(key) || WorldComponent_Dominion.Instance.Player.influence < LobbyCost)
                {
                    options.Add(new FloatMenuOption(label, null));
                    continue;
                }
                Faction captured = f;
                options.Add(new FloatMenuOption(label, () =>
                {
                    lobbied.Add(key);
                    WorldComponent_Dominion.Instance.Player.AddInfluence(-LobbyCost);
                    if (Rand.Chance(chance))
                    {
                        m.lobbying[captured.loadID] = m.LobbyFor(captured) + LobbyShift * direction;
                        Messages.Message("Dominion_Council_LobbySuccess".Translate(captured.Name), MessageTypeDefOf.PositiveEvent, historical: false);
                    }
                    else
                    {
                        WorldComponent_Dominion.Instance.GetRelation(captured, Faction.OfPlayer).favour -= 1f;
                        Messages.Message("Dominion_Council_LobbyFail".Translate(captured.Name), MessageTypeDefOf.NegativeEvent, historical: false);
                    }
                }));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_Council_NoOneToLobby".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static string LeaningLabel(int vote) =>
            (vote > 0 ? "Dominion_Council_Yes" : vote < 0 ? "Dominion_Council_No" : "Dominion_Council_Abstain").Translate();
    }
}
