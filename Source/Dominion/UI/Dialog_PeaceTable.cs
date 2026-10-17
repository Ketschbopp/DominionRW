using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The peace table: a draft peace on parchment. Take the victor's terms when you are clearly winning, or negotiate:
    /// which occupied settlements stay where they are, silver either way, prisoners released. The odds move as you
    /// change the terms; sign it, and they seal it or stamp it REJECTED.
    /// </summary>
    public class Dialog_PeaceTable : PaperWindow
    {
        private readonly War war;
        private readonly PeaceDeal deal;
        private readonly SealCeremony ceremony = new SealCeremony();
        private bool victorsTerms;
        private Vector2 scroll;
        private float viewHeight = 500f;
        private string defaultTerms;

        public override Vector2 InitialSize => new Vector2(780f, 720f);

        public Dialog_PeaceTable(War war)
        {
            this.war = war;
            deal = new PeaceDeal(war);
            victorsTerms = PlayerLeads;
        }

        private Faction Enemy => war.Other(Faction.OfPlayer);

        private bool PlayerLeads => war.Leader() == Faction.OfPlayer;

        private static DiplomacyActionDef PeaceDef => DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace");

        private float Chance(System.Text.StringBuilder sb = null)
        {
            if (!victorsTerms) return deal.Chance(sb);
            float c = deal.Chance(null);
            var worker = PeaceDef?.Worker;
            if (sb != null && worker != null) sb.Append(worker.ChanceBreakdown(Enemy));
            // Victor's terms cost them their war; they take it a little harder than a deal.
            float baseChance = (worker?.AcceptanceChance(Enemy) ?? c) - 0.05f;
            if (sb != null) DominionPaper.Term(sb, -0.05f, "Dominion_Odds_VictorsTerms".Translate());
            return Mathf.Clamp(baseChance, 0.02f, 0.95f);
        }

        private string Breakdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Dominion_Odds_Title".Translate(Enemy.Name));
            float c = Chance(sb);
            sb.AppendLine();
            sb.Append("Dominion_Odds_Total".Translate(c.ToStringPercent()));
            return sb.ToString();
        }

        private string CannotSendReason()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (!comp.Wars.wars.Contains(war)) return "Dominion_Peace_WarOver".Translate();
            DiplomacyActionDef def = PeaceDef;
            if (def != null)
            {
                string why = DiplomacyActions.CannotUseReason(def, Enemy);
                if (why != null) return why;
            }
            return victorsTerms ? null : deal.CannotOfferReason();
        }

        public override void PreClose()
        {
            base.PreClose();
            if (ceremony.Busy) Decide();
        }

        private SealCeremony.Stage Decide()
        {
            if (ceremony.Done) return ceremony.stage;
            DiplomacyActionDef def = PeaceDef;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction enemy = Enemy;
            float chance = Chance();
            if (def != null)
            {
                comp.Player.AddInfluence(-def.influenceCost);
                if (def.cooldownDays > 0)
                    comp.Player.SetCooldown(def.defName, enemy.loadID, Find.TickManager.TicksGame + def.cooldownDays * GenDate.TicksPerDay);
            }
            if (!Rand.Chance(chance))
            {
                string refused = "Dominion_Peace_Refused".Translate(enemy.Name);
                Messages.Message(refused, MessageTypeDefOf.NegativeEvent, historical: true);
                comp.Player.Log(enemy, refused, -1);
                war.Chronicle("Dominion_Peace_ChronicleRefused".Translate(enemy.Name));
                return SealCeremony.Stage.Refused;
            }
            war.deal = victorsTerms ? null : deal;
            try
            {
                DiplomacyUtility.SetRelationKind(Faction.OfPlayer, enemy, FactionRelationKind.Neutral, Faction.OfPlayer, def?.label ?? "peace");
            }
            finally
            {
                war.deal = null;
            }
            string accepted = "Dominion_Peace_Accepted".Translate(enemy.Name);
            Messages.Message(accepted, MessageTypeDefOf.PositiveEvent, historical: true);
            comp.Player.Log(enemy, accepted, 1);
            return SealCeremony.Stage.Accepted;
        }

        protected override void DoPaper(Rect rect)
        {
            ceremony.Update(Decide);
            Faction enemy = Enemy;
            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Peace_Title".Translate(enemy.Name));
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), war.Label, DominionTheme.InkFaded, GameFont.Small, TextAnchor.UpperCenter);
            y += 24f;

            const float SignHeight = 150f;
            Rect bodyOut = new Rect(rect.x, y, rect.width, rect.yMax - y - SignHeight);
            Rect view = new Rect(0f, 0f, bodyOut.width - 16f, viewHeight);
            Widgets.BeginScrollView(bodyOut, ref scroll, view);
            float vy = 0f, w = view.width;
            bool locked = ceremony.stage != SealCeremony.Stage.Draft;
            string sent = "Dominion_Paper_Locked".Translate();

            // Where the war stands.
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Peace_Standing".Translate());
            vy += 18f;
            WarPanel.DrawScoreBar(new Rect(0f, vy, Mathf.Min(w, 460f), 22f), war);
            vy += 26f;
            vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Peace_Facts".Translate(war.DurationTicks.ToStringTicksToPeriod(allowSeconds: false), war.battles,
                war.DeadOf(Faction.OfPlayer), war.DeadOf(enemy), enemy.Name), DominionTheme.InkFaded, GameFont.Tiny) + 8f;

            // Which terms.
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Peace_Kind".Translate());
            vy += 18f;
            string[] kinds = { "Dominion_Peace_Victors".Translate(), "Dominion_Peace_Negotiated".Translate() };
            string[] kindTips = { "Dominion_Peace_Victors_Desc".Translate(WarUtility.GoalLabel(war.goal)), "Dominion_Peace_Negotiated_Desc".Translate() };
            string[] kindOff =
            {
                locked ? sent : PlayerLeads ? null : "Dominion_Peace_NotWinning".Translate(PeaceTerms.DecisiveScore.ToString("F0")).ToString(),
                locked ? sent : null,
            };
            victorsTerms = DominionPaper.Choice(new Rect(0f, vy, Mathf.Min(w, 420f), 28f), kinds, victorsTerms ? 0 : 1, kindTips, kindOff) == 0;
            vy += 34f;

            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Paper_Article1".Translate());
            vy += 18f;
            if (victorsTerms)
            {
                defaultTerms ??= PeaceTerms.Describe(war);
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, defaultTerms) + 8f;
            }
            else vy = DrawNegotiation(vy, w, locked, sent);

            DominionPaper.OddsMeter(new Rect(0f, vy, Mathf.Min(w, 420f), 26f), Chance(), Breakdown());
            vy += 30f;
            DiplomacyActionDef def = PeaceDef;
            if (def != null)
                vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Peace_Bill".Translate(def.influenceCost.ToString("F0")), DominionTheme.InkFaded, GameFont.Tiny) + 4f;
            viewHeight = vy + 8f;
            Widgets.EndScrollView();

            // Signatures
            Rect sign = new Rect(rect.x, rect.yMax - SignHeight, rect.width, SignHeight);
            float half = (sign.width - 40f) / 2f;
            Rect ours = new Rect(sign.x, sign.y + 34f, half, 90f), theirs = new Rect(sign.x + half + 40f, sign.y + 34f, half, 90f);
            string ourName = DominionPaper.PlayerSignatory(out string ourTitle);
            string theirName = DominionPaper.Signatory(enemy, out string theirTitle);
            DominionPaper.SignatureLine(ours, ourName, ourTitle);
            DominionPaper.SignatureLine(theirs, theirName, theirTitle);
            ceremony.DrawOurSignature(ours);
            ceremony.DrawTheirAnswer(theirs, enemy);
            Rect button = new Rect(sign.x, sign.y, half, 28f);
            if (ceremony.stage == SealCeremony.Stage.Draft)
            {
                string why = CannotSendReason();
                if (DominionPaper.InkButton(button, "Dominion_Peace_Sign".Translate(), "Dominion_Peace_Sign_Desc".Translate(enemy.Name), why, false, DominionTheme.InkRed))
                    ceremony.Sign();
            }
            else if (ceremony.Done && DominionPaper.InkButton(new Rect(theirs.x, sign.y, half, 28f), "Dominion_Paper_FileAway".Translate()))
                Close();
            if (ceremony.stage == SealCeremony.Stage.Accepted && ceremony.T > 0.5f)
                DominionPaper.SlamStamp(new Rect(sign.center.x - 90f, sign.y - 8f, 180f, 70f), DominionPaper.StampSigned, DominionTheme.InkGreen, -6f, ceremony.T - 0.5f);
        }

        private float DrawNegotiation(float vy, float w, bool locked, string sent)
        {
            Faction enemy = Enemy;
            List<Settlement> occupied = deal.Occupied();
            if (occupied.Count > 0)
            {
                foreach (Settlement s in occupied)
                {
                    bool ours = s.Faction == Faction.OfPlayer;
                    HashSet<Settlement> set = ours ? deal.kept : deal.demanded;
                    string label = ours ? "Dominion_Deal_Keep".Translate(s.Label) : "Dominion_Deal_DemandBack".Translate(s.Label);
                    bool on = set.Contains(s);
                    bool now = DominionPaper.InkCheckbox(new Rect(10f, vy, w - 10f, 24f), label, on,
                        ours ? "Dominion_Deal_Keep_Desc".Translate() : "Dominion_Deal_DemandBack_Desc".Translate(), locked ? sent : null);
                    if (now != on)
                    {
                        if (now) set.Add(s);
                        else set.Remove(s);
                    }
                    vy += 26f;
                }
            }
            else vy += DominionPaper.Paragraph(10f, vy, w - 10f, "Dominion_Deal_NoOccupied".Translate(), DominionTheme.InkFaded) + 4f;

            int silver = DiplomacyHelpers.AvailableSilver();
            DominionTheme.InkLabel(new Rect(10f, vy, 150f, 28f), "Dominion_Deal_Offer".Translate(), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
            deal.silverOffered = SilverChoice(new Rect(160f, vy, Mathf.Min(w - 160f, 440f), 28f), deal.silverOffered, silver, locked, sent);
            vy += 32f;
            DominionTheme.InkLabel(new Rect(10f, vy, 150f, 28f), "Dominion_Deal_Demand".Translate(), DominionTheme.Ink, GameFont.Small, TextAnchor.MiddleLeft);
            deal.silverDemanded = SilverChoice(new Rect(160f, vy, Mathf.Min(w - 160f, 440f), 28f), deal.silverDemanded, int.MaxValue, locked, sent);
            vy += 32f;
            int prisoners = PeaceDeal.Prisoners(enemy).Count;
            deal.releasePrisoners = DominionPaper.InkCheckbox(new Rect(10f, vy, w - 10f, 24f), "Dominion_Deal_ReleasePrisoners".Translate(prisoners, enemy.Name), deal.releasePrisoners,
                "Dominion_Deal_ReleasePrisoners_Desc".Translate(), locked ? sent : prisoners == 0 ? "Dominion_Deal_NoPrisoners".Translate().ToString() : null);
            vy += 30f;
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Deal_Summary".Translate());
            vy += 18f;
            foreach (string line in deal.Describe())
                vy += DominionPaper.Paragraph(10f, vy, w - 10f, "· " + line) + 2f;
            return vy + 8f;
        }

        private static int SilverChoice(Rect r, int current, int available, bool locked, string sent)
        {
            int[] opts = PeaceDeal.SilverOptions;
            var labels = new string[opts.Length];
            var off = new string[opts.Length];
            for (int i = 0; i < opts.Length; i++)
            {
                labels[i] = opts[i] == 0 ? "Dominion_None".Translate().ToString() : opts[i].ToString();
                off[i] = locked ? sent : opts[i] > available ? "Dominion_Action_NeedsSilver".Translate(opts[i]).ToString() : null;
            }
            int index = Mathf.Max(0, System.Array.IndexOf(opts, current));
            return opts[DominionPaper.Choice(r, labels, index, null, off)];
        }
    }
}
