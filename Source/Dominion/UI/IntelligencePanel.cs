using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The Intelligence tab: networks, operations under way, counter-intelligence, and planning new operations.</summary>
    public class IntelligencePanel
    {
        private const float Line = 24f;
        private Vector2 scroll;
        private float height = 600f;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            IntelService svc = comp.Intel;
            Rect view = new Rect(0f, 0f, rect.width - 16f, height);
            Widgets.BeginScrollView(rect, ref scroll, view);
            var l = new Listing_Standard();
            l.Begin(view);

            Text.Font = GameFont.Medium;
            l.Label("Dominion_Ops_Title".Translate());
            Text.Font = GameFont.Small;

            ResearchProjectDef research = DominionResearchDefOf.Dominion_IntelligenceNetworks;
            if (!research.IsFinished)
                l.Label("Dominion_Ops_NeedsResearch".Translate(research.label));
            else
            {
                Rect buttons = l.GetRect(32f);
                if (Widgets.ButtonText(buttons.LeftPart(0.32f), "Dominion_Ops_Plan".Translate()))
                    Find.WindowStack.Add(new Dialog_PlanOperation());
                string ciLabel = svc.CounterIntelActive
                    ? "Dominion_Ops_CounterIntelUntil".Translate((svc.counterIntelUntil - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)).ToString()
                    : "Dominion_Ops_CounterIntelButton".Translate(IntelOpsSystem.CounterIntelCost.ToString("F0")).ToString();
                Rect ci = new Rect(buttons.x + buttons.width * 0.34f, buttons.y, buttons.width * 0.4f, buttons.height);
                if (Widgets.ButtonText(ci, ciLabel, active: !svc.CounterIntelActive))
                {
                    string error = IntelOpsSystem.StartCounterIntel();
                    if (error != null) Messages.Message(error, MessageTypeDefOf.RejectInput, historical: false);
                }
                TooltipHandler.TipRegion(ci, "Dominion_Ops_CounterIntel_Desc".Translate(IntelOpsSystem.CounterIntelDays));
            }

            l.GapLine();
            l.Label("Dominion_Ops_Running".Translate());
            if (svc.operations.Count == 0)
            {
                GUI.color = Color.gray;
                l.Label("  " + "Dominion_None".Translate());
                GUI.color = Color.white;
            }
            int now = Find.TickManager.TicksGame;
            foreach (Operation op in svc.operations)
                l.Label("  " + "Dominion_Ops_RunningLine".Translate(op.def.LabelCap, op.target.Name,
                    string.Join(", ", op.agents.Select(a => a.LabelShortCap)), Mathf.Max(0, op.endTick - now).ToStringTicksToPeriod(allowSeconds: false)));

            l.GapLine();
            l.Label("Dominion_Ops_Networks".Translate());
            foreach (Faction f in comp.Cache.Simulated)
            {
                Rect row = l.GetRect(Line);
                FactionUIUtility.DrawFactionIconWithTooltip(new Rect(row.x, row.y + 2f, 20f, 20f), f);
                Widgets.Label(new Rect(row.x + 24f, row.y, row.width * 0.4f, Line), f.Name.Truncate(row.width * 0.4f));
                Rect bar = new Rect(row.x + row.width * 0.45f, row.y + 3f, row.width * 0.25f, Line - 6f);
                float net = svc.Network(f);
                Widgets.FillableBar(bar, net / 100f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(bar, "Dominion_Ops_NetworkLevel".Translate(net.ToString("F0")));
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(row.x + row.width * 0.73f, row.y, row.width * 0.27f, Line),
                    "Dominion_Ops_YourIntel".Translate(IntelUtility.IntelOn(f).ToString("F0")));
            }

            height = Mathf.Max(l.CurHeight + 20f, rect.height);
            l.End();
            Widgets.EndScrollView();
        }
    }

    /// <summary>Choose an operation, a target and up to three agents, see the odds, and send them off.</summary>
    public class Dialog_PlanOperation : Window
    {
        private const float Line = 24f;
        private OperationDef op;
        private Faction target;
        private readonly HashSet<Pawn> agents = new HashSet<Pawn>();
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(640f, 620f);

        public Dialog_PlanOperation()
        {
            candidateCache.Invalidate(); // pawns just sent away must not be offered again
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            op = DefDatabase<OperationDef>.AllDefsListForReading.FirstOrDefault();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "Dominion_Ops_Plan".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 40f;

            if (Widgets.ButtonText(new Rect(inRect.x, y, inRect.width / 2f - 4f, 32f), op?.LabelCap ?? "-"))
                Find.WindowStack.Add(new FloatMenu(DefDatabase<OperationDef>.AllDefsListForReading
                    .Select(d => new FloatMenuOption(d.LabelCap, () => op = d)).ToList()));
            if (Widgets.ButtonText(new Rect(inRect.x + inRect.width / 2f + 4f, y, inRect.width / 2f - 4f, 32f), target?.Name ?? "Dominion_Ops_ChooseTarget".Translate().ToString()))
                Find.WindowStack.Add(new FloatMenu(WorldComponent_Dominion.Instance.Cache.Simulated
                    .Select(f => new FloatMenuOption(f.Name, () => target = f)).ToList()));
            y += 38f;

            if (op != null)
            {
                string cost = op.silverCost > 0
                    ? "Dominion_Ops_CostWithSilver".Translate(op.influenceCost.ToString("F0"), op.silverCost).ToString()
                    : "Dominion_Ops_CostInfluence".Translate(op.influenceCost.ToString("F0")).ToString();
                string info = op.description + "\n\n" + "Dominion_Ops_Terms".Translate(op.durationDays, cost, op.skill?.label ?? "-", op.maxAgents);
                float h = Text.CalcHeight(info, inRect.width);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, h), info);
                y += h + 8f;
            }

            Widgets.Label(new Rect(inRect.x, y, inRect.width, Line), "Dominion_Ops_ChooseAgents".Translate());
            y += Line;
            List<Pawn> candidates = candidateCache.Value;
            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - 90f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, candidates.Count * Line);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                bool on = agents.Contains(p);
                int skill = op?.skill != null ? p.skills?.GetSkill(op.skill)?.Level ?? 0 : 0;
                string label = p.LabelShortCap + (op?.skill != null ? "  (" + op.skill.LabelCap + " " + skill + ")" : "");
                bool before = on;
                Widgets.CheckboxLabeled(new Rect(0f, i * Line, view.width, Line), label, ref on);
                if (on != before)
                {
                    if (on) agents.Add(p);
                    else agents.Remove(p);
                }
            }
            Widgets.EndScrollView();

            List<Pawn> team = agents.Where(p => p.Spawned && candidates.Contains(p)).ToList();
            string reason = op == null ? "-" : IntelOpsSystem.CannotLaunchReason(op, target, team);
            float y2 = inRect.yMax - 84f;
            if (op != null && target != null && team.Count > 0)
                Widgets.Label(new Rect(inRect.x, y2, inRect.width, Line), "Dominion_Ops_Chance".Translate(IntelOpsSystem.SuccessChance(op, target, team).ToStringPercent(),
                    op.exposureOnFail.ToStringPercent()));
            if (reason != null)
            {
                GUI.color = new Color(1f, 0.6f, 0.4f);
                Rect reasonRect = new Rect(inRect.x, y2 + Line, inRect.width, Line);
                Widgets.Label(reasonRect, reason.Truncate(reasonRect.width));
                TooltipHandler.TipRegion(reasonRect, reason);
                GUI.color = Color.white;
            }
            if (Widgets.ButtonText(new Rect(inRect.center.x - 100f, inRect.yMax - 36f, 200f, 34f), "Dominion_Ops_Launch".Translate(), active: reason == null)
                && reason == null)
            {
                IntelOpsSystem.Launch(op, target, team);
                Close();
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
