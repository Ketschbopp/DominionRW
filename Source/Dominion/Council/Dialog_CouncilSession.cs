using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// A council session the player attends, drawn as a ballot paper. Each motion is a ballot: stamp it AYE, NAY or
    /// ABSTAIN. Pick a motion to see the floor: every member's leaning, and lobby the wavering with influence, silver or a
    /// promise of support. Strike the gavel to count the votes; each ballot is then stamped CARRIED or DEFEATED.
    /// In dev mode a veto button exists for testing; in play, no one has a veto.
    /// </summary>
    public class Dialog_CouncilSession : PaperWindow
    {
        private const float BallotHeight = 92f;
        private const float InfluenceLobbyCost = 10f;
        private const int SilverLobbyCost = 200;
        private const float LobbyShift = 0.35f;

        private readonly Pawn speaker;
        private readonly Dictionary<Motion, int> votes = new Dictionary<Motion, int>();
        private readonly HashSet<string> lobbied = new HashSet<string>();
        private Dictionary<Motion, bool> results;
        private readonly List<Motion> agenda;
        private readonly List<Faction> attendees;
        private readonly Dictionary<Motion, (float yes, float no, float quorum)> finalTally = new Dictionary<Motion, (float, float, float)>();
        private Motion selected;
        private float gavelTime = -1f;
        private Vector2 ballotScroll;
        private Vector2 floorScroll;

        public override Vector2 InitialSize => new Vector2(1000f, 720f);

        public Dialog_CouncilSession(Pawn speaker)
        {
            this.speaker = speaker;
            doCloseX = false;
            closeOnCancel = false;
            closeOnAccept = false;
            agenda = new List<Motion>(State.agenda);
            // The session state is cleared when the gavel falls; keep who was in the room for the results.
            attendees = new List<Faction>(State.attending);
            selected = agenda.Count > 0 ? agenda[0] : null;
        }

        private static CouncilState State => WorldComponent_Dominion.Instance.Council;

        private int VoteOn(Motion m) => votes.TryGetValue(m, out int v) ? v : 0;

        protected override void DoPaper(Rect rect)
        {
            CouncilState c = State;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Council_SessionTitle".Translate(c.host?.Name ?? "?"));
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f),
                "Dominion_Council_SessionInfo".Translate(speaker.LabelShortCap, attendees.Count,
                    CouncilUtility.VoteWeight(Faction.OfPlayer, attendees).ToString("F1"),
                    WorldComponent_Dominion.Instance.Player.influence.ToString("F0")), DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
            y += 26f;

            Rect body = new Rect(rect.x, y, rect.width, rect.yMax - y - 50f);
            Rect ballots = body.LeftPart(0.58f);
            Rect floor = body.RightPart(0.4f);
            DrawBallots(ballots);
            GUI.color = DominionPaper.EdgeFaint;
            Widgets.DrawLineVertical(floor.x - body.width * 0.01f, floor.y, floor.height);
            GUI.color = Color.white;
            DrawFloor(floor);

            Rect bottom = new Rect(rect.center.x - 150f, rect.yMax - 40f, 300f, 36f);
            if (results == null)
            {
                if (DominionPaper.InkButton(bottom, "Dominion_Council_Gavel".Translate(), "Dominion_Council_Gavel_Desc".Translate(), null, false, DominionTheme.InkRed))
                {
                    SoundDefOf.Crunch.PlayOneShotOnCamera();
                    gavelTime = Time.realtimeSinceStartup;
                    foreach (Motion m in agenda)
                    {
                        CouncilSystem.Tally(m, votes, out float yes, out float no);
                        finalTally[m] = (yes, no, CouncilSystem.Quorum);
                    }
                    results = CouncilSystem.Resolve(votes);
                }
                DominionTheme.DrawInk(new Rect(bottom.x + 8f, bottom.y + 4f, 28f, 28f), DominionTheme.CategoryIcon(EventCategory.Council), DominionTheme.InkRed);
            }
            else if (DominionPaper.InkButton(bottom, "Dominion_Council_Adjourn".Translate(), "Dominion_Council_Adjourn_Desc".Translate()))
                Close();
        }

        // ------------------------------------------------------------------ ballots

        private void DrawBallots(Rect rect)
        {
            if (agenda.Count == 0)
            {
                DominionPaper.Paragraph(rect.x, rect.y, rect.width, "Dominion_Council_NoMotions".Translate(), DominionTheme.InkFaded);
                return;
            }
            Rect view = new Rect(0f, 0f, rect.width - 16f, agenda.Count * (BallotHeight + 8f));
            Widgets.BeginScrollView(rect, ref ballotScroll, view);
            for (int i = 0; i < agenda.Count; i++)
                DrawBallot(new Rect(0f, i * (BallotHeight + 8f), view.width, BallotHeight), agenda[i]);
            Widgets.EndScrollView();
        }

        private void DrawBallot(Rect rect, Motion m)
        {
            bool isSelected = m == selected;
            Widgets.DrawBoxSolid(rect, isSelected ? DominionTheme.PaperHighlight : DominionTheme.PaperShade);
            GUI.color = isSelected ? DominionPaper.Edge : DominionPaper.EdgeFaint;
            Widgets.DrawBox(rect, isSelected ? 2 : 1);
            GUI.color = Color.white;
            Rect inner = rect.ContractedBy(6f);
            Rect text = new Rect(inner.x, inner.y, inner.width - 200f, inner.height);
            DominionTheme.InkLabel(new Rect(text.x, text.y, text.width, 22f), m.Summary.Truncate(text.width));
            DominionTheme.InkLabel(new Rect(text.x, text.y + 20f, text.width, 16f), "Dominion_Council_ProposedBy".Translate(m.sponsor?.Name ?? "?"), DominionTheme.InkFaded, GameFont.Tiny);
            if (m.sponsor != null && WorldComponent_Dominion.Instance.Player.councilPromises.Contains(m.sponsor.loadID))
                DominionTheme.InkLabel(new Rect(text.x, text.y + 34f, text.width, 16f), "Dominion_Council_YouPromised".Translate(m.sponsor.Name), DominionTheme.InkAmber, GameFont.Tiny);

            // The tally: yes against no, with the quorum marked.
            float yes, no, quorum;
            if (finalTally.TryGetValue(m, out var tally)) (yes, no, quorum) = tally;
            else
            {
                CouncilSystem.Tally(m, votes, out yes, out no);
                quorum = CouncilSystem.Quorum;
            }
            Rect bar = new Rect(text.x, inner.yMax - 18f, text.width, 14f);
            float total = Mathf.Max(0.01f, yes + no);
            float max = Mathf.Max(total, quorum * 1.5f);
            Widgets.DrawBoxSolid(bar, new Color(0.3f, 0.24f, 0.17f, 0.15f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * yes / max, bar.height), new Color(DominionTheme.InkGreen.r, DominionTheme.InkGreen.g, DominionTheme.InkGreen.b, 0.6f));
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * yes / max, bar.y, bar.width * no / max, bar.height), new Color(DominionTheme.InkRed.r, DominionTheme.InkRed.g, DominionTheme.InkRed.b, 0.55f));
            float q = bar.x + bar.width * quorum / max;
            Widgets.DrawLineVertical(q, bar.y - 3f, bar.height + 6f);
            DominionTheme.InkLabel(bar, "Dominion_Council_Projected".Translate(yes.ToString("F1"), no.ToString("F1")), DominionTheme.Ink, GameFont.Tiny, TextAnchor.MiddleCenter);
            TooltipHandler.TipRegion(bar, "Dominion_Council_Tally_Desc".Translate(quorum.ToString("F1")));
            TooltipHandler.TipRegion(text, m.def.description + "\n\n" + "Dominion_Council_ClickForFloor".Translate());
            if (Widgets.ButtonInvisible(text)) selected = m;

            // The stamps, or the result once the gavel has fallen.
            Rect stamps = new Rect(inner.xMax - 194f, inner.y, 194f, inner.height);
            if (results != null)
            {
                bool carried = results.TryGetValue(m, out bool r) && r;
                DominionPaper.SlamStamp(stamps.ContractedBy(4f, 14f), carried ? DominionPaper.StampCarried : DominionPaper.StampDefeated,
                    carried ? DominionTheme.InkGreen : DominionTheme.InkRed, carried ? -6f : 5f, Time.realtimeSinceStartup - gavelTime - agenda.IndexOf(m) * 0.25f);
                return;
            }
            int vote = VoteOn(m);
            float sw = 62f;
            var options = new[] { (1, DominionPaper.StampAye, DominionTheme.InkGreen), (0, DominionPaper.StampAbstain, DominionTheme.InkAmber), (-1, DominionPaper.StampNay, DominionTheme.InkRed) };
            for (int i = 0; i < options.Length; i++)
            {
                (int option, Texture2D tex, Color color) = options[i];
                Rect s = new Rect(stamps.x + i * (sw + 4f), stamps.y + 6f, sw, 30f);
                string tip = ("Dominion_Council_Stamp_" + option).Translate();
                if (DominionTheme.StampButton(s, tex, color, (i - 1) * 3f, tip, null, vote == option)) votes[m] = option;
            }
            if (vote != 0 || votes.ContainsKey(m))
                DominionTheme.DrawStamp(new Rect(stamps.x + 30f, stamps.y + 40f, 130f, 40f), vote > 0 ? DominionPaper.StampAye : vote < 0 ? DominionPaper.StampNay : DominionPaper.StampAbstain,
                    vote > 0 ? DominionTheme.InkGreen : vote < 0 ? DominionTheme.InkRed : DominionTheme.InkAmber, -4f);
            if (Prefs.DevMode && DominionPaper.InkButton(new Rect(stamps.xMax - 60f, stamps.yMax - 20f, 60f, 20f), m.vetoed ? "Dominion_Council_Vetoed".Translate().ToString() : "Dominion_Council_DevVeto".Translate().ToString()))
                CouncilSystem.DevVeto(m);
        }

        // ------------------------------------------------------------------ the floor

        private void DrawFloor(Rect rect)
        {
            DominionPaper.Caption(new Rect(rect.x, rect.y, rect.width, 16f), "Dominion_Council_Floor".Translate());
            if (selected == null) return;
            DominionTheme.InkLabel(new Rect(rect.x, rect.y + 16f, rect.width, 22f), selected.Summary.Truncate(rect.width));
            int vote = VoteOn(selected);
            string hint = vote == 0 ? "Dominion_Council_LobbyNeedsVote".Translate() : "Dominion_Council_LobbyHint".Translate();
            DominionTheme.InkLabel(new Rect(rect.x, rect.y + 36f, rect.width, 30f), hint, DominionTheme.InkFaded, GameFont.Tiny);

            Rect outRect = new Rect(rect.x, rect.y + 66f, rect.width, rect.height - 66f);
            List<Faction> members = attendees;
            Rect view = new Rect(0f, 0f, outRect.width - 16f, members.Count * 50f);
            Widgets.BeginScrollView(outRect, ref floorScroll, view);
            float y = 0f;
            foreach (Faction f in members)
            {
                Rect row = new Rect(0f, y, view.width, 46f);
                DrawMember(row, f, vote);
                y += 50f;
            }
            Widgets.EndScrollView();
        }

        private void DrawMember(Rect row, Faction f, int vote)
        {
            Widgets.DrawBoxSolid(row, DominionTheme.PaperShade);
            DominionPaper.DrawSeal(new Rect(row.x + 2f, row.y + 3f, 40f, 40f), f);
            float weight = CouncilUtility.VoteWeight(f, attendees);
            DominionTheme.InkLabel(new Rect(row.x + 46f, row.y + 2f, row.width - 160f, 20f), f.Name.Truncate(row.width - 160f));
            int leaning;
            bool wavering;
            if (f.IsPlayer)
            {
                leaning = vote;
                wavering = false;
            }
            else
            {
                leaning = CouncilUtility.AIVote(f, selected);
                wavering = CouncilUtility.Wavering(f, selected);
            }
            string lean = f.IsPlayer ? "Dominion_Council_Leaning_You".Translate().ToString() : Dialog_CouncilSessionText.Leaning(leaning, wavering);
            Color c = leaning > 0 ? DominionTheme.InkGreen : leaning < 0 ? DominionTheme.InkRed : DominionTheme.InkAmber;
            DominionTheme.InkLabel(new Rect(row.x + 46f, row.y + 22f, row.width - 160f, 18f),
                lean + "  ·  " + "Dominion_Council_Weight".Translate(weight.ToString("F1")), c, GameFont.Tiny);
            if (f.IsPlayer || results != null) return;

            Rect lobby = new Rect(row.xMax - 108f, row.y + 9f, 104f, 28f);
            string key = f.loadID + ":" + selected.GetHashCode();
            string why = vote == 0 ? "Dominion_Council_LobbyNeedsVote".Translate().ToString()
                : lobbied.Contains(key) ? "Dominion_Council_AlreadyLobbied".Translate().ToString()
                : leaning == vote && !wavering ? "Dominion_Council_AlreadyWithYou".Translate().ToString() : null;
            if (DominionPaper.InkButton(lobby, "Dominion_Council_LobbyShort".Translate(), "Dominion_Council_Lobby_Desc".Translate(f.Name), why))
                LobbyMenu(f, key, vote);
        }

        private void LobbyMenu(Faction f, string key, int direction)
        {
            PlayerState player = WorldComponent_Dominion.Instance.Player;
            RelationState view = WorldComponent_Dominion.Instance.GetRelation(f, Faction.OfPlayer);
            int social = DiplomatUtility.Social(speaker);
            float influenceChance = Mathf.Clamp(0.4f + social * 0.03f, 0.1f, 0.95f);
            float silverChance = Mathf.Clamp(0.55f + social * 0.02f + WorldComponent_Dominion.Instance.GetState(f).personality.greed * 0.2f, 0.1f, 0.95f);
            float promiseChance = Mathf.Clamp(0.3f + view.opinion / 200f + social * 0.02f, 0.05f, 0.9f);
            bool promised = player.councilPromises.Contains(f.loadID);
            var options = new List<FloatMenuOption>
            {
                Option("Dominion_Council_LobbyInfluence".Translate(InfluenceLobbyCost.ToString("F0"), influenceChance.ToStringPercent()),
                    player.influence < InfluenceLobbyCost ? "Dominion_Action_NeedsInfluence".Translate(InfluenceLobbyCost.ToString("F0")).ToString() : null,
                    () =>
                    {
                        player.AddInfluence(-InfluenceLobbyCost);
                        Lobby(f, key, direction, influenceChance, 1f);
                    }),
                Option("Dominion_Council_LobbySilver".Translate(SilverLobbyCost, silverChance.ToStringPercent()),
                    DiplomacyHelpers.AvailableSilver() < SilverLobbyCost ? "Dominion_Action_NeedsSilver".Translate(SilverLobbyCost).ToString() : null,
                    () =>
                    {
                        int paid = DiplomacyHelpers.TakeSilver(SilverLobbyCost);
                        EconomyEffects.Receive(f, paid);
                        Lobby(f, key, direction, silverChance, 1.3f);
                    }),
                Option("Dominion_Council_LobbyPromise".Translate(promiseChance.ToStringPercent()),
                    promised ? "Dominion_Council_AlreadyPromised".Translate().ToString() : null,
                    () =>
                    {
                        player.councilPromises.Add(f.loadID);
                        Lobby(f, key, direction, promiseChance, 1f);
                    }),
            };
            Find.WindowStack.Add(new FloatMenu(options, "Dominion_Council_LobbyTitle".Translate(f.Name)));
        }

        private static FloatMenuOption Option(string label, string disabled, System.Action act) =>
            disabled == null ? new FloatMenuOption(label, act) : new FloatMenuOption(label + " (" + disabled + ")", null);

        private void Lobby(Faction f, string key, int direction, float chance, float strength)
        {
            lobbied.Add(key);
            if (Rand.Chance(chance))
            {
                selected.lobbying[f.loadID] = selected.LobbyFor(f) + LobbyShift * strength * direction;
                Messages.Message("Dominion_Council_LobbySuccess".Translate(f.Name), MessageTypeDefOf.PositiveEvent, historical: false);
            }
            else
            {
                WorldComponent_Dominion.Instance.GetRelation(f, Faction.OfPlayer).favour -= 1f;
                Messages.Message("Dominion_Council_LobbyFail".Translate(f.Name), MessageTypeDefOf.NegativeEvent, historical: false);
            }
        }

        public static string LeaningLabel(int vote) => Dialog_CouncilSessionText.Leaning(vote, false);
    }

    internal static class Dialog_CouncilSessionText
    {
        public static string Leaning(int vote, bool wavering)
        {
            string s = (vote > 0 ? "Dominion_Council_Leaning_Yes" : vote < 0 ? "Dominion_Council_Leaning_No" : "Dominion_Council_Leaning_Abstain").Translate();
            return wavering ? s + " (" + "Dominion_Council_Wavering".Translate() + ")" : s;
        }
    }
}
