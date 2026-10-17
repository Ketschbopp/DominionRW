using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A mission file: choose the operation, the target, the agents who go and a handler who stays home, and how much
    /// risk to take. The odds and the chance of being caught update as you choose; seal it TOP SECRET to send them.
    /// </summary>
    public class Dialog_MissionFile : PaperWindow
    {
        private static readonly Color Manila = new Color(0.87f, 0.72f, 0.42f, 0.32f);

        private OperationDef op;
        private Faction target;
        private Faction thirdParty;
        private OpRisk risk = OpRisk.Normal;
        private Pawn handler;
        private readonly HashSet<Pawn> agents = new HashSet<Pawn>();
        private Vector2 scroll;
        private float sealedAt = -1f;
        private float viewHeight = 900f;

        public override Vector2 InitialSize => new Vector2(720f, 720f);

        public Dialog_MissionFile(Faction target = null)
        {
            candidateCache.Invalidate(); // pawns just sent away must not be offered again
            op = DefDatabase<OperationDef>.AllDefsListForReading.FirstOrDefault(d => d.allowAbstract) ?? DefDatabase<OperationDef>.AllDefsListForReading.FirstOrDefault();
            if (target != null && !target.IsPlayer && !target.defeated) this.target = target;
        }

        protected override void DoPaper(Rect rect)
        {
            Widgets.DrawBoxSolid(rect.ExpandedBy(20f), Manila);
            List<Pawn> candidates = candidateCache.Value;
            List<Pawn> team = agents.Where(p => p.Spawned && candidates.Contains(p)).ToList();
            if (handler != null && (!candidates.Contains(handler) || agents.Contains(handler))) handler = null;

            // Sealing: the TOP SECRET stamp comes down, then the agents leave.
            if (sealedAt >= 0f)
            {
                float t = Time.realtimeSinceStartup - sealedAt;
                DominionPaper.SlamStamp(new Rect(rect.center.x - 170f, rect.center.y - 60f, 340f, 120f), DominionPaper.StampTopSecret, DominionTheme.InkRed, -8f, t);
                if (t > 0.7f)
                {
                    if (IntelOpsSystem.CannotLaunchReason(op, target, team, thirdParty) == null)
                        IntelOpsSystem.Launch(op, target, team, thirdParty, risk, handler);
                    Close();
                }
                return;
            }

            float y = DominionPaper.Title(new Rect(rect.x, rect.y, rect.width, 40f), "Dominion_Mission_Title".Translate());
            DominionTheme.DrawStamp(new Rect(rect.xMax - 150f, rect.y - 6f, 150f, 52f), DominionPaper.StampTopSecret, DominionTheme.InkRed, 7f);

            Rect view = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y - 44f);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float vy = 0f, w = view.width;

            // Operation and target
            DominionPaper.Caption(new Rect(0f, vy, w / 2f, 16f), "Dominion_Mission_Operation".Translate());
            DominionPaper.Caption(new Rect(w / 2f + 4f, vy, w / 2f, 16f), "Dominion_Mission_Target".Translate());
            vy += 18f;
            if (DominionPaper.InkButton(new Rect(0f, vy, w / 2f - 4f, 28f), op?.LabelCap ?? "-", op != null ? op.LabelCap + "\n\n" + op.description : null))
                Find.WindowStack.Add(new FloatMenu(DefDatabase<OperationDef>.AllDefsListForReading.Select(d => new FloatMenuOption(
                    d.LabelCap.ToString() + (d.allowAbstract ? "" : " (" + "Dominion_Ops_PhysicalOnly".Translate().ToString() + ")"), () => op = d,
                    mouseoverGuiAction: r => TooltipHandler.TipRegion(r, d.description))).ToList()));
            if (DominionPaper.InkButton(new Rect(w / 2f + 4f, vy, w / 2f - 4f, 28f), target?.Name ?? "Dominion_Ops_ChooseTarget".Translate().ToString(), "Dominion_Tip_ChooseTarget".Translate()))
                Find.WindowStack.Add(new FloatMenu(WorldComponent_Dominion.Instance.Cache.Simulated
                    .Select(f => new FloatMenuOption(f.Name + "  (" + "Dominion_Mission_Network".Translate(WorldComponent_Dominion.Instance.Intel.Network(f).ToString("F0")) + ")",
                        () => target = f, f.def.FactionIcon, f.Color)).ToList()));
            vy += 34f;
            if (op != null && op.needsThirdParty)
            {
                if (DominionPaper.InkButton(new Rect(w / 2f + 4f, vy, w / 2f - 4f, 28f),
                        thirdParty != null ? "Dominion_Ops_Blame".Translate(thirdParty.Name).ToString() : "Dominion_Ops_ChooseBlame".Translate().ToString(), "Dominion_Tip_ChooseBlame".Translate()))
                    Find.WindowStack.Add(new FloatMenu(WorldComponent_Dominion.Instance.Cache.Simulated
                        .Where(f => f != target).Select(f => new FloatMenuOption(f.Name, () => thirdParty = f, f.def.FactionIcon, f.Color)).ToList()));
                vy += 34f;
            }

            if (op != null)
            {
                string cost = op.silverCost > 0
                    ? "Dominion_Ops_CostWithSilver".Translate(op.influenceCost.ToString("F0"), op.silverCost).ToString()
                    : "Dominion_Ops_CostInfluence".Translate(op.influenceCost.ToString("F0")).ToString();
                string modes = op.allowAbstract && op.allowPhysical ? "Dominion_Ops_ModeBoth".Translate()
                    : op.allowPhysical ? "Dominion_Ops_ModePhysical".Translate() : "Dominion_Ops_ModeAbstract".Translate();
                int days = Mathf.RoundToInt(op.durationDays * IntelOpsSystem.RiskDuration(risk));
                vy += DominionPaper.Paragraph(0f, vy, w, op.description, DominionTheme.InkFaded) + 4f;
                vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Ops_Terms".Translate(days, cost, op.skill?.label ?? "-", op.maxAgents) + "\n" + modes, DominionTheme.Ink, GameFont.Tiny) + 8f;
            }

            // Risk
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Mission_Risk".Translate());
            vy += 18f;
            string[] risks = { "Dominion_Risk_Cautious".Translate(), "Dominion_Risk_Normal".Translate(), "Dominion_Risk_Bold".Translate() };
            string[] riskTips = { "Dominion_Risk_Cautious_Desc".Translate(), "Dominion_Risk_Normal_Desc".Translate(), "Dominion_Risk_Bold_Desc".Translate() };
            risk = (OpRisk)DominionPaper.Choice(new Rect(0f, vy, Mathf.Min(w, 420f), 28f), risks, (int)risk, riskTips);
            vy += 34f;

            // Agents
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Ops_ChooseAgents".Translate());
            vy += 18f;
            foreach (Pawn p in candidates)
            {
                int skill = op?.skill != null ? p.skills?.GetSkill(op.skill)?.Level ?? 0 : 0;
                string label = p.LabelShortCap + (op?.skill != null ? "  (" + op.skill.LabelCap + " " + skill + ")" : "");
                bool on = agents.Contains(p);
                bool now = DominionPaper.InkCheckbox(new Rect(0f, vy, w / 2f - 4f, 24f), label, on, null, p == handler ? "Dominion_Mission_IsHandler".Translate().ToString() : null);
                if (now != on)
                {
                    if (now) agents.Add(p);
                    else agents.Remove(p);
                }
                vy += 24f;
            }
            if (candidates.Count == 0) vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Ops_NoAgents".Translate(), DominionTheme.InkFaded) + 4f;
            vy += 6f;

            // Handler
            DominionPaper.Caption(new Rect(0f, vy, w, 16f), "Dominion_Mission_Handler".Translate());
            vy += 18f;
            string handlerLabel = handler != null && op != null ? handler.LabelShortCap + "  (+" + IntelOpsSystem.HandlerBonus(op, handler).ToStringPercent() + ")" : "Dominion_None".Translate().ToString();
            if (op != null && DominionPaper.InkButton(new Rect(0f, vy, w / 2f - 4f, 28f), handlerLabel, "Dominion_Mission_Handler_Desc".Translate()))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_None".Translate(), () => handler = null) };
                foreach (Pawn p in candidates)
                {
                    if (agents.Contains(p)) continue;
                    Pawn captured = p;
                    options.Add(new FloatMenuOption(p.LabelShortCap + "  (+" + IntelOpsSystem.HandlerBonus(op, p).ToStringPercent() + ")", () => handler = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            vy += 36f;

            // Odds
            if (op != null && op.allowAbstract && target != null && team.Count > 0)
            {
                float chance = IntelOpsSystem.SuccessChance(op, target, team, risk, handler);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Dominion_Mission_OddsTitle".Translate());
                float plain = IntelOpsSystem.SuccessChance(op, target, team, OpRisk.Normal, null);
                DominionPaper.Term(sb, plain, "Dominion_Mission_OddsBase".Translate());
                DominionPaper.Term(sb, IntelOpsSystem.RiskChance(risk), "Dominion_Mission_OddsRisk".Translate());
                DominionPaper.Term(sb, IntelOpsSystem.HandlerBonus(op, handler), "Dominion_Mission_OddsHandler".Translate());
                DominionPaper.OddsMeter(new Rect(0f, vy, Mathf.Min(w, 420f), 26f), chance, sb.ToString(), "Dominion_Mission_Success".Translate());
                vy += 30f;
                vy += DominionPaper.Paragraph(0f, vy, w, "Dominion_Mission_Exposure".Translate(
                    (op.exposureOnFail * IntelOpsSystem.RiskExposure(risk)).ToStringPercent(),
                    (op.exposureOnSuccess * IntelOpsSystem.RiskExposure(risk)).ToStringPercent()), DominionTheme.InkRed, GameFont.Tiny) + 4f;
            }
            viewHeight = vy + 10f;
            Widgets.EndScrollView();

            // Dispatch
            string reason = op == null ? "-" : IntelOpsSystem.CannotLaunchReason(op, target, team, thirdParty);
            Rect send = new Rect(rect.xMax - 260f, rect.yMax - 36f, 260f, 34f);
            if (DominionPaper.InkButton(send, "Dominion_Mission_Seal".Translate(), "Dominion_Tip_Launch".Translate(), reason, false, DominionTheme.InkRed))
                sealedAt = Time.realtimeSinceStartup;

            // Visit Settlements: colonists already visiting one of the target's settlements can do it in person, now.
            Integration_VisitSettlements vs = IntegrationRegistry.Get<Integration_VisitSettlements>();
            if (op != null && op.allowPhysical && target != null && vs != null && vs.IsActive)
            {
                foreach ((Settlement s, Map map) in vs.ActiveVisits())
                {
                    if (s.Faction != target) continue;
                    FloatMenuAcceptanceReport ok = PhysicalOps.CanStartOnVisit(op, s, map);
                    Rect visitRect = new Rect(rect.x, rect.yMax - 36f, rect.width - 280f, 34f);
                    string label = "Dominion_Ops_RunOnVisit".Translate(s.Label, PhysicalOps.VisitAgents(map).Count);
                    if (DominionPaper.InkButton(visitRect, label, "Dominion_Ops_RunOnVisit_Desc".Translate(), ok.Accepted ? null : ok.FailReason ?? "-"))
                    {
                        PhysicalOps.BeginOnVisit(op, s, map);
                        Close();
                    }
                    break;
                }
            }
        }

        // The candidate scan walks every colonist on every home map; the dialog draws every frame.
        private static readonly UICache<List<Pawn>> candidateCache = new UICache<List<Pawn>>(Candidates, 1f);

        private static List<Pawn> Candidates()
        {
            var list = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                    if (!p.Downed && !p.InMentalState) list.Add(p);
            }
            return list;
        }
    }
}
