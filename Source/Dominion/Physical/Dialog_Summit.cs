using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum SummitMove { CommonGround, Flattery, Concession, Threat, Apology, Leverage, SharedEnemies, IdeologyAppeal, Candour }

    /// <summary>
    /// A summit in three rounds. Each round the head of state picks an approach; how it lands depends on their Social
    /// skill and the other leader's personality (the honourable despise threats, the greedy like concessions, the
    /// paranoid distrust flattery). Things happen around the table. Rapport then sets the odds of the final proposal.
    /// </summary>
    public class Dialog_Summit : Window
    {
        private const int Rounds = 3;
        private const float IncidentChance = 0.15f;
        private const int ConcessionSilver = 300;
        private const float ConcessionInfluence = 10f;

        private readonly Summit summit;
        private readonly Pawn ours;
        private readonly Pawn theirs;
        private readonly Faction faction;

        private int round;
        private float rapport;
        private bool concluded;
        private readonly List<string> log = new List<string>();
        private readonly List<SummitMove> offered = new List<SummitMove>();
        private Vector2 logScroll;

        public override Vector2 InitialSize => new Vector2(860f, 640f);

        public Dialog_Summit(Summit summit, Pawn ours, Pawn theirs)
        {
            this.summit = summit;
            this.ours = ours;
            this.theirs = theirs;
            faction = summit.faction;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnCancel = false;
            doCloseX = false;
            log.Add("Dominion_Summit_Opening".Translate(TheirName, faction.Name, summit.AgendaLabel));
            PrepareRound();
        }

        private string TheirName => theirs?.LabelShortCap ?? "Dominion_Summit_TheirRepresentative".Translate().ToString();

        private AIPersonality Personality => WorldComponent_Dominion.Instance.GetState(faction).personality;

        private RelationState TheirView => WorldComponent_Dominion.Instance.GetRelation(faction, Faction.OfPlayer);

        private int Social => DiplomatUtility.Social(ours);

        // Chances and the transcript only change when a move is played; the window draws every frame.
        private readonly Dictionary<SummitMove, float> chanceCache = new Dictionary<SummitMove, float>();
        private float finalChanceCache = -1f;
        private string logText;
        private int logTextCount = -1;
        private float logHeight;
        private float logWidth = -1f;

        private float CachedChance(SummitMove m)
        {
            if (!chanceCache.TryGetValue(m, out float c))
            {
                Evaluate(m, out c, out _, out _);
                chanceCache[m] = c;
            }
            return c;
        }

        private float CachedFinalChance()
        {
            if (finalChanceCache < 0f) finalChanceCache = FinalChance();
            return finalChanceCache;
        }

        private void PrepareRound()
        {
            chanceCache.Clear();
            finalChanceCache = -1f;
            offered.Clear();
            var eligible = new List<SummitMove>();
            foreach (SummitMove m in System.Enum.GetValues(typeof(SummitMove)))
                if (Eligible(m)) eligible.Add(m);
            eligible.Shuffle();
            for (int i = 0; i < eligible.Count && offered.Count < 4; i++)
                offered.Add(eligible[i]);
        }

        private bool Eligible(SummitMove m)
        {
            switch (m)
            {
                case SummitMove.Concession:
                    return DiplomacyHelpers.AvailableSilver() >= ConcessionSilver || WorldComponent_Dominion.Instance.Player.influence >= ConcessionInfluence;
                case SummitMove.Apology: return TheirView.grievance > 10f;
                case SummitMove.Leverage: return HostageUtility.HeldByUs(faction).Count > 0;
                case SummitMove.SharedEnemies: return TheirView.sharedEnemies > 0;
                case SummitMove.IdeologyAppeal: return RelationsSystem.IdeoCompatibility(faction, Faction.OfPlayer) > 0.2f;
                default: return true;
            }
        }

        /// <summary>Chance the move lands, and rapport on success and on failure.</summary>
        private void Evaluate(SummitMove m, out float chance, out float onSuccess, out float onFail)
        {
            AIPersonality p = Personality;
            float compat = RelationsSystem.IdeoCompatibility(faction, Faction.OfPlayer);
            switch (m)
            {
                case SummitMove.CommonGround: chance = 0.45f + Social * 0.025f + compat * 0.15f; onSuccess = 14f; onFail = -4f; break;
                case SummitMove.Flattery: chance = 0.5f + Social * 0.02f - p.paranoia * 0.25f; onSuccess = 10f; onFail = -8f; break;
                case SummitMove.Concession: chance = 0.6f + p.greed * 0.35f; onSuccess = 12f + p.greed * 15f; onFail = 3f; break;
                case SummitMove.Threat:
                    float ratio = DiplomacyHelpers.PlayerStrength() / Mathf.Max(1f, WorldComponent_Dominion.Instance.GetState(faction).militaryStrength);
                    chance = Mathf.Clamp01(ratio - 0.6f) * 0.8f * (1f - p.honour * 0.5f); onSuccess = 18f; onFail = -22f; break;
                case SummitMove.Apology: chance = 0.65f + p.honour * 0.2f; onSuccess = 12f; onFail = -3f; break;
                case SummitMove.Leverage: chance = 0.75f; onSuccess = Mathf.Min(30f, 8f * HostageUtility.HeldByUs(faction).Count); onFail = -10f; break;
                case SummitMove.SharedEnemies: chance = 0.7f; onSuccess = 12f; onFail = -2f; break;
                case SummitMove.IdeologyAppeal: chance = 0.5f + compat * 0.4f; onSuccess = 12f; onFail = -6f; break;
                default: chance = 0.4f + p.honour * 0.5f; onSuccess = 12f; onFail = -6f; break;
            }
            chance = Mathf.Clamp(chance, 0.05f, 0.95f);
        }

        private void Play(SummitMove m)
        {
            chanceCache.Clear();
            finalChanceCache = -1f;
            Evaluate(m, out float chance, out float onSuccess, out float onFail);
            if (m == SummitMove.Concession)
            {
                // Recheck: silver or influence may have gone since the round began.
                if (DiplomacyHelpers.AvailableSilverFresh() >= ConcessionSilver) EconomyEffects.Transfer(Faction.OfPlayer, summit.faction, ConcessionSilver);
                else if (WorldComponent_Dominion.Instance.Player.influence >= ConcessionInfluence)
                    WorldComponent_Dominion.Instance.Player.AddInfluence(-ConcessionInfluence);
                else
                {
                    Messages.Message("Dominion_Summit_ConcessionUnaffordable".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
            }
            bool success = Rand.Chance(chance);
            rapport = Mathf.Clamp(rapport + (success ? onSuccess : onFail), -100f, 100f);
            log.Add(("Dominion_SummitMove_" + m).Translate(ours.LabelShortCap).Colorize(new Color(0.75f, 0.85f, 1f)));
            log.Add(("Dominion_SummitMove_" + m + (success ? "_Success" : "_Fail")).Translate(TheirName));
            if (m == SummitMove.Threat && !success) TheirView.grievance += 5f;
            if (m == SummitMove.Apology && success) TheirView.grievance = Mathf.Max(0f, TheirView.grievance - 10f);
            if (m == SummitMove.Leverage && success && Personality.honour > 0.6f) TheirView.grievance += 5f;
            ours.skills?.Learn(SkillDefOf.Social, 400f);

            if (Rand.Chance(IncidentChance)) Incident();
            round++;
            if (round < Rounds) PrepareRound();
            else offered.Clear();
        }

        private void Incident()
        {
            int which = Rand.Range(0, 5);
            float[] effects = { -5f, Rand.Bool ? 5f : -5f, 8f, -4f, 6f };
            rapport = Mathf.Clamp(rapport + effects[which], -100f, 100f);
            log.Add(("Dominion_SummitIncident_" + which).Translate(TheirName).Colorize(new Color(1f, 0.85f, 0.5f)));
        }

        private float FinalChance()
        {
            float baseChance;
            switch (summit.agenda)
            {
                case SummitAgenda.Peace:
                    baseChance = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace")?.Worker.AcceptanceChance(faction) ?? 0.2f;
                    break;
                case SummitAgenda.Treaty:
                    baseChance = summit.treaty != null ? DiplomacyHelpers.TreatyAcceptance(summit.treaty, faction) : 0f;
                    break;
                case SummitAgenda.Hostages:
                    baseChance = HostageUtility.ExchangeAcceptance(faction);
                    break;
                default:
                    return 1f;
            }
            return Mathf.Clamp(baseChance + rapport / 150f + DiplomatUtility.InPersonBonus(ours), 0.02f, 0.97f);
        }

        private void Conclude(bool walkOut)
        {
            concluded = true;
            RelationState view = TheirView;
            if (walkOut)
            {
                view.favour -= 10f;
                view.grievance += 5f;
                log.Add("Dominion_Summit_WalkOut".Translate(ours.LabelShortCap));
                SummitSystem.Finish(summit, false, null);
                return;
            }

            view.favour += rapport / 5f;
            bool success = Rand.Chance(FinalChance());
            if (success)
            {
                switch (summit.agenda)
                {
                    case SummitAgenda.Relations:
                        view.favour += 15f + rapport / 4f;
                        faction.TryAffectGoodwillWith(Faction.OfPlayer, 8, canSendMessage: false, canSendHostilityLetter: false);
                        break;
                    case SummitAgenda.Peace:
                        if (faction.HostileTo(Faction.OfPlayer))
                            DiplomacyUtility.SetRelationKind(Faction.OfPlayer, faction, FactionRelationKind.Neutral, Faction.OfPlayer, "Dominion_Reason_Peace".Translate());
                        break;
                    case SummitAgenda.Treaty:
                        if (summit.treaty != null && WorldComponent_Dominion.Instance.Treaties.CannotSignReason(summit.treaty, Faction.OfPlayer, faction) == null)
                            WorldComponent_Dominion.Instance.Treaties.Sign(summit.treaty, Faction.OfPlayer, faction, Faction.OfPlayer);
                        break;
                    case SummitAgenda.Hostages:
                        HostageUtility.Exchange(faction, out _, out _);
                        break;
                }
                log.Add(("Dominion_SummitResult_Success_" + summit.agenda).Translate(TheirName, summit.AgendaLabel).Colorize(ColorLibrary.Green));
            }
            else
            {
                view.favour -= 5f;
                log.Add("Dominion_SummitResult_Fail".Translate(TheirName).Colorize(ColorLibrary.RedReadable));
            }
            SummitSystem.Finish(summit, success, null);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Dominion_Summit_Title".Translate(faction.Name));
            Text.Font = GameFont.Small;

            // Portraits and names
            float y = inRect.y + 40f;
            DrawParty(new Rect(inRect.x, y, 200f, 120f), ours, Faction.OfPlayer.Name);
            DrawParty(new Rect(inRect.xMax - 200f, y, 200f, 120f), theirs, faction.Name);

            Rect mid = new Rect(inRect.x + 210f, y, inRect.width - 420f, 120f);
            Widgets.Label(new Rect(mid.x, mid.y, mid.width, 24f), "Dominion_Summit_Agenda".Translate(summit.AgendaLabel));
            Widgets.Label(new Rect(mid.x, mid.y + 26f, mid.width, 24f), "Dominion_Summit_Round".Translate(Mathf.Min(round + 1, Rounds), Rounds));
            Rect bar = new Rect(mid.x, mid.y + 56f, mid.width, 24f);
            Widgets.FillableBar(bar, (rapport + 100f) / 200f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, "Dominion_Summit_Rapport".Translate(rapport.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;
            if (!concluded && round >= Rounds)
                Widgets.Label(new Rect(mid.x, mid.y + 86f, mid.width, 24f), "Dominion_Summit_FinalChance".Translate(CachedFinalChance().ToStringPercent()));

            // Transcript
            y += 128f;
            Rect logRect = new Rect(inRect.x, y, inRect.width, inRect.height - (y - inRect.y) - 120f);
            Widgets.DrawMenuSection(logRect);
            Rect inner = logRect.ContractedBy(8f);
            if (logTextCount != log.Count || logWidth != inner.width)
            {
                logText = string.Join("\n\n", log);
                logHeight = Text.CalcHeight(logText, inner.width - 16f);
                logTextCount = log.Count;
                logWidth = inner.width;
            }
            string text = logText;
            float h = logHeight;
            Rect view = new Rect(0f, 0f, inner.width - 16f, h);
            Widgets.BeginScrollView(inner, ref logScroll, view);
            Widgets.Label(view, text);
            Widgets.EndScrollView();

            // Choices
            Rect choices = new Rect(inRect.x, logRect.yMax + 8f, inRect.width, 110f);
            if (concluded)
            {
                if (Widgets.ButtonText(new Rect(choices.center.x - 100f, choices.y + 30f, 200f, 36f), "Dominion_Summit_Leave".Translate()))
                    Close();
                return;
            }
            if (round >= Rounds)
            {
                if (Widgets.ButtonText(new Rect(choices.x, choices.y, choices.width / 2f - 4f, 36f), "Dominion_Summit_Propose".Translate(summit.AgendaLabel)))
                    Conclude(false);
            }
            else
            {
                float w = (choices.width - 12f) / 2f;
                for (int i = 0; i < offered.Count; i++)
                {
                    SummitMove m = offered[i];
                    float chance = CachedChance(m);
                    Rect b = new Rect(choices.x + (i % 2) * (w + 12f), choices.y + (i / 2) * 40f, w, 36f);
                    if (Widgets.ButtonText(b, ("Dominion_SummitMoveLabel_" + m).Translate() + "  (" + chance.ToStringPercent() + ")"))
                    {
                        Play(m);
                        logScroll.y = float.MaxValue;
                        break;
                    }
                    TooltipHandler.TipRegion(b, ("Dominion_SummitMoveLabel_" + m + "_Desc").Translate());
                }
            }
            if (Widgets.ButtonText(new Rect(choices.xMax - 160f, choices.yMax - 30f, 160f, 30f), "Dominion_Summit_WalkOutButton".Translate()))
                Conclude(true);
        }

        private static void DrawParty(Rect rect, Pawn pawn, string factionName)
        {
            Rect icon = new Rect(rect.center.x - 40f, rect.y, 80f, 80f);
            Widgets.DrawMenuSection(icon);
            if (pawn != null) Widgets.ThingIcon(icon.ContractedBy(4f), pawn);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, icon.yMax + 2f, rect.width, 22f), pawn?.LabelShortCap ?? "?");
            GUI.color = Color.gray;
            Widgets.Label(new Rect(rect.x, icon.yMax + 22f, rect.width, 22f), factionName.Truncate(rect.width));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
