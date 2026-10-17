using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The Intelligence tab, as the service's terminal. OPERATIONS: missions under way with progress, click one for its
    /// file (odds now, agents, handler, risk), recall it or find it on the map. NETWORK BOARD: what the service knows about
    /// each faction and what is still redacted. COUNTER-INTEL: suspected foreign operations, sweeps, and how much each
    /// service knows about you. ARCHIVE: finished missions and how they ended.
    /// </summary>
    public class IntelligencePanel
    {
        private enum Screen { Operations, Board, Counter, Archive }

        private const float Line = 22f;

        private static Screen screen = Screen.Operations;
        private int selectedOp = -1;
        private Vector2 leftScroll;
        private Vector2 rightScroll;

        public void Draw(Rect rect, WorldComponent_Dominion comp)
        {
            IntelService svc = comp.Intel;
            Terminal.BeginScreen(rect);
            Rect inner = rect.ContractedBy(12f);

            // Header
            Terminal.Label(new Rect(inner.x, inner.y, inner.width, 22f), "Dominion_Term_Header".Translate(Faction.OfPlayer.Name).ToString().ToUpper(), Terminal.Green);
            Terminal.Label(new Rect(inner.x, inner.y, inner.width, 22f), DominionPaper.Date(Find.TickManager.TicksGame).ToUpper(), Terminal.Dim, GameFont.Small, TextAnchor.UpperRight);
            Terminal.Label(new Rect(inner.x, inner.y + 20f, inner.width, 20f),
                "Dominion_Term_Status".Translate(svc.operations.Count, comp.Player.influence.ToString("F0"),
                    svc.CounterIntelActive ? "Dominion_Term_CIOn".Translate() : "Dominion_Term_CIOff".Translate()) + "  > " + Terminal.Cursor, Terminal.Dim, GameFont.Tiny);

            ResearchProjectDef research = DominionResearchDefOf.Dominion_IntelligenceNetworks;
            bool cleared = research.IsFinished;

            // Menu
            float x = inner.x;
            float y = inner.y + 44f;
            foreach (Screen s in System.Enum.GetValues(typeof(Screen)))
            {
                Rect b = new Rect(x, y, 170f, 26f);
                if (Terminal.Button(b, ("Dominion_Term_Screen_" + s).Translate(), ("Dominion_Term_Screen_" + s + "_Desc").Translate(), null, screen == s))
                {
                    screen = s;
                    leftScroll = rightScroll = Vector2.zero;
                }
                x += 174f;
            }
            Rect plan = new Rect(inner.xMax - 190f, y, 190f, 26f);
            if (Terminal.Button(plan, "Dominion_Term_NewMission".Translate(), "Dominion_Tip_PlanOp".Translate(), cleared ? null : "Dominion_Action_NeedsResearch".Translate(research.label).ToString(), false, Terminal.Amber))
                Find.WindowStack.Add(new Dialog_MissionFile(DominionUIState.selected));
            Terminal.Rule(new Rect(inner.x, y + 28f, inner.width, 6f));

            Rect body = new Rect(inner.x, y + 38f, inner.width, inner.yMax - y - 38f);
            if (!cleared && screen != Screen.Board)
            {
                Terminal.Label(new Rect(body.x, body.y + 20f, body.width, 60f), "Dominion_Term_AccessDenied".Translate(research.label).ToString().ToUpper(), Terminal.Alert, GameFont.Medium, TextAnchor.UpperCenter);
            }
            else
            {
                switch (screen)
                {
                    case Screen.Operations: DrawOperations(body, svc); break;
                    case Screen.Board: DrawBoard(body, comp, svc); break;
                    case Screen.Counter: DrawCounter(body, comp, svc); break;
                    case Screen.Archive: DrawArchive(body, svc); break;
                }
            }
            Terminal.EndScreen(rect);
        }

        // ------------------------------------------------------------------ operations

        private void DrawOperations(Rect rect, IntelService svc)
        {
            Rect left = rect.LeftPart(0.55f);
            Rect right = rect.RightPart(0.43f);
            int now = Find.TickManager.TicksGame;
            if (svc.operations.Count == 0)
            {
                Terminal.Label(left, "Dominion_Term_NoOps".Translate().ToString().ToUpper(), Terminal.Dim);
                return;
            }
            if (!svc.operations.Any(o => o.id == selectedOp)) selectedOp = svc.operations[0].id;
            Rect view = new Rect(0f, 0f, left.width - 16f, svc.operations.Count * (Line * 2f + 6f));
            Widgets.BeginScrollView(left, ref leftScroll, view);
            float y = 0f;
            foreach (Operation op in svc.operations)
            {
                Rect row = new Rect(0f, y, view.width, Line * 2f + 2f);
                bool sel = op.id == selectedOp;
                if (sel) Widgets.DrawBoxSolid(row, new Color(Terminal.Green.r, Terminal.Green.g, Terminal.Green.b, 0.12f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(Terminal.Green.r, Terminal.Green.g, Terminal.Green.b, 0.06f));
                string name = ("#" + op.id.ToString("D3") + "  " + op.def.label + "  →  " + (op.settlement?.Label ?? op.target.Name)).ToUpper();
                Terminal.Label(new Rect(row.x + 4f, row.y, row.width - 8f, Line), name.Truncate(row.width - 8f), sel ? Terminal.Green : Terminal.Dim);
                string progress;
                if (op.physical) progress = PhysicalOps.Status(op).ToUpper();
                else
                {
                    float total = Mathf.Max(1f, op.endTick - op.startTick);
                    progress = Terminal.Bar((now - op.startTick) / total, 20) + "   ETA " + Mathf.Max(0, op.endTick - now).ToStringTicksToPeriod(allowSeconds: false).ToUpper();
                }
                Terminal.Label(new Rect(row.x + 16f, row.y + Line, row.width - 20f, Line), progress.Truncate(row.width - 20f), Terminal.Dim, GameFont.Tiny);
                TooltipHandler.TipRegion(row, "Dominion_Term_ClickForFile".Translate());
                if (Widgets.ButtonInvisible(row)) selectedOp = op.id;
                y += Line * 2f + 6f;
            }
            Widgets.EndScrollView();

            Operation chosen = svc.operations.FirstOrDefault(o => o.id == selectedOp);
            if (chosen != null) DrawOpFile(right, chosen);
        }

        private void DrawOpFile(Rect rect, Operation op)
        {
            GUI.color = Terminal.Dim;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            Rect inner = rect.ContractedBy(8f);
            int now = Find.TickManager.TicksGame;
            var lines = new List<(string, string)>
            {
                ("Dominion_Term_F_Operation".Translate(), op.def.LabelCap),
                ("Dominion_Term_F_Target".Translate(), op.settlement != null ? op.settlement.Label + " (" + op.target.Name + ")" : op.target.Name),
                ("Dominion_Term_F_Mode".Translate(), (op.physical ? "Dominion_Term_Physical" : "Dominion_Term_Abstract").Translate()),
                ("Dominion_Term_F_Agents".Translate(), string.Join(", ", op.agents.Where(p => p != null).Select(p =>
                    p.LabelShortCap + (op.def.skill != null ? " (" + (p.skills?.GetSkill(op.def.skill)?.Level ?? 0) + ")" : "")))),
                ("Dominion_Term_F_Handler".Translate(), op.handler != null ? op.handler.LabelShortCap : "Dominion_None".Translate().ToString()),
                ("Dominion_Term_F_Risk".Translate(), ("Dominion_Risk_" + op.risk).Translate()),
                ("Dominion_Term_F_Started".Translate(), DominionPaper.Date(op.startTick)),
            };
            if (op.thirdParty != null) lines.Add(("Dominion_Term_F_Blame".Translate(), op.thirdParty.Name));
            if (!op.physical)
            {
                lines.Add(("Dominion_Term_F_Ends".Translate(), Mathf.Max(0, op.endTick - now).ToStringTicksToPeriod(allowSeconds: false)));
                List<Pawn> agents = op.agents.Where(p => p != null && !p.Dead).ToList();
                lines.Add(("Dominion_Term_F_Odds".Translate(), IntelOpsSystem.SuccessChance(op.def, op.target, agents, op.risk, op.handler).ToStringPercent()));
                lines.Add(("Dominion_Term_F_Exposure".Translate(), (op.def.exposureOnFail * IntelOpsSystem.RiskExposure(op.risk)).ToStringPercent()));
            }
            else lines.Add(("Dominion_Term_F_Status".Translate(), PhysicalOps.Status(op)));

            float y = inner.y;
            Terminal.Label(new Rect(inner.x, y, inner.width, 22f), "Dominion_Term_FileTitle".Translate(op.id.ToString("D3")).ToString().ToUpper(), Terminal.Amber);
            y += 26f;
            foreach ((string k, string v) in lines)
            {
                Text.Font = GameFont.Small;
                float h = Mathf.Max(20f, Text.CalcHeight(v, inner.width - 120f));
                Terminal.Label(new Rect(inner.x, y, 116f, h), k.ToUpper(), Terminal.Dim, GameFont.Tiny);
                Terminal.Label(new Rect(inner.x + 120f, y, inner.width - 120f, h), v, Terminal.Green);
                y += h + 2f;
            }
            y += 8f;
            Rect recall = new Rect(inner.x, y, inner.width / 2f - 4f, 26f);
            if (Terminal.Button(recall, "Dominion_Term_Recall".Translate(), "Dominion_Term_Recall_Desc".Translate(), IntelOpsSystem.CannotRecallReason(op), false, Terminal.Alert))
            {
                Operation captured = op;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_Term_RecallConfirm".Translate(op.def.label, op.target.Name), () => IntelOpsSystem.Recall(captured), destructive: true));
            }
            Thing look = op.objective != null && op.objective.Spawned ? op.objective : op.agents.FirstOrDefault(a => a != null && a.Spawned);
            Rect locate = new Rect(recall.xMax + 8f, y, inner.width / 2f - 4f, 26f);
            if (Terminal.Button(locate, "Dominion_Term_Locate".Translate(), "Dominion_Tip_ShowTarget".Translate(), look == null ? "Dominion_Term_NothingToLocate".Translate().ToString() : null))
                CameraJumper.TryJumpAndSelect(look);
        }

        // ------------------------------------------------------------------ network board

        private void DrawBoard(Rect rect, WorldComponent_Dominion comp, IntelService svc)
        {
            List<Faction> factions = comp.Cache.Simulated;
            Faction sel = DominionUIState.SelectedIn(factions);
            Rect left = rect.LeftPart(0.38f);
            Rect right = rect.RightPart(0.6f);
            Rect view = new Rect(0f, 0f, left.width - 16f, factions.Count * (Line + 18f));
            Widgets.BeginScrollView(left, ref leftScroll, view);
            float y = 0f;
            foreach (Faction f in factions)
            {
                Rect row = new Rect(0f, y, view.width, Line + 16f);
                if (f == sel) Widgets.DrawBoxSolid(row, new Color(Terminal.Green.r, Terminal.Green.g, Terminal.Green.b, 0.12f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(Terminal.Green.r, Terminal.Green.g, Terminal.Green.b, 0.06f));
                Terminal.Label(new Rect(row.x + 4f, row.y, row.width - 8f, Line), f.Name.ToUpper().Truncate(row.width - 8f), f == sel ? Terminal.Green : Terminal.Dim);
                Terminal.Label(new Rect(row.x + 12f, row.y + Line - 4f, row.width - 16f, 18f),
                    "NET " + Terminal.Bar(svc.Network(f) / 100f, 10) + "   INTEL " + IntelUtility.IntelOn(f).ToString("F0"), Terminal.Faint, GameFont.Tiny);
                if (Widgets.ButtonInvisible(row)) DominionUIState.selected = f;
                y += Line + 18f;
            }
            Widgets.EndScrollView();
            if (sel != null) DrawDossier(right, sel, comp, svc);
        }

        private void DrawDossier(Rect rect, Faction f, WorldComponent_Dominion comp, IntelService svc)
        {
            GUI.color = Terminal.Dim;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;
            Rect inner = rect.ContractedBy(8f);
            comp.TryGetState(f, out FactionState s);
            float intel = IntelUtility.IntelOn(f);
            var lines = new List<(string, string, float)>
            {
                ("Dominion_Term_D_Network".Translate(), Terminal.Bar(svc.Network(f) / 100f, 16), 0f),
                ("Dominion_Term_D_Intel".Translate(), intel.ToString("F0") + " / 100", 0f),
                ("Dominion_Term_D_Leader".Translate(), f.leader != null ? f.leader.Name?.ToStringFull ?? f.leader.LabelShortCap : "-", IntelUtility.RestrictedIntel),
                ("Dominion_Term_D_Government".Translate(), s != null && s.governmentInitialized ? DominionUI.GovernmentLabel(s.government) : "-", IntelUtility.RestrictedIntel),
                ("Dominion_Term_D_Stability".Translate(), s != null ? s.stability.ToString("F0") + "   LEGITIMACY " + s.legitimacy.ToString("F0") : "-", IntelUtility.RestrictedIntel),
                ("Dominion_Term_D_Economy".Translate(), s != null ? s.economyIndex.ToString("F0") : "-", IntelUtility.RestrictedIntel),
                ("Dominion_Term_D_Army".Translate(), s != null ? s.militaryStrength.ToString("F0") + "   " + "Dominion_Term_D_Warbands".Translate(WarbandUtility.CountActive(f)) : "-", IntelUtility.SecretIntel),
                ("Dominion_Term_D_Treaties".Translate(), Treaties(f, comp), IntelUtility.RestrictedIntel),
                ("Dominion_Term_D_Mood".Translate(), s != null ? DominionUI.BlocLabel(s.DominantBloc) + ", " + "Dominion_Term_D_Weariness".Translate(s.warWeariness.ToString("F0")) : "-", IntelUtility.SecretIntel),
                ("Dominion_Term_D_OnYou".Translate(), s != null ? "Dominion_Term_D_TheirFile".Translate(s.playerIntel.ToString("F0")).ToString() : "-", IntelUtility.SecretIntel),
                ("Dominion_Term_D_Opinion".Translate(), comp.GetRelation(f, Faction.OfPlayer).opinion.ToString("F0"), IntelUtility.BlackOpsIntel),
            };
            Rect view = new Rect(0f, 0f, inner.width - 16f, 40f + lines.Count * 40f + 60f);
            Widgets.BeginScrollView(inner, ref rightScroll, view);
            float y = 0f;
            Terminal.Label(new Rect(0f, y, view.width, 24f), ("Dominion_Term_DossierTitle".Translate(f.Name)).ToString().ToUpper(), Terminal.Amber);
            y += 28f;
            foreach ((string k, string v, float needed) in lines)
            {
                bool known = needed <= 0f || intel >= needed;
                string value = known ? v : Terminal.Redacted(Mathf.Clamp(v.Length, 6, 18), needed);
                Text.Font = GameFont.Small;
                float h = Mathf.Max(20f, Text.CalcHeight(value, view.width - 130f));
                Terminal.Label(new Rect(0f, y, 126f, h), k.ToUpper(), Terminal.Dim, GameFont.Tiny);
                Terminal.Label(new Rect(130f, y, view.width - 130f, h), value, known ? Terminal.Green : Terminal.Faint);
                y += h + 4f;
            }
            y += 10f;
            if (Terminal.Button(new Rect(0f, y, 220f, 26f), "Dominion_Term_MissionAgainst".Translate(), "Dominion_Tip_PlanOp".Translate(), null, false, Terminal.Amber))
                Find.WindowStack.Add(new Dialog_MissionFile(f));
            if (Terminal.Button(new Rect(228f, y, 200f, 26f), "Dominion_Term_OpenDossier".Translate(), "Dominion_UI_Jump_Dossier_Desc".Translate(f.Name)))
                DominionUIState.Show(DominionTab.Overview, f);
            Widgets.EndScrollView();
        }

        private static string Treaties(Faction f, WorldComponent_Dominion comp)
        {
            var list = comp.Treaties.All.Where(t => t.Involves(f)).Select(t => t.Label + " (" + t.Other(f).Name + ")").ToList();
            return list.Count == 0 ? "Dominion_None".Translate().ToString() : string.Join(", ", list);
        }

        // ------------------------------------------------------------------ counter-intelligence

        private void DrawCounter(Rect rect, WorldComponent_Dominion comp, IntelService svc)
        {
            float y = rect.y;
            string ci = svc.CounterIntelActive
                ? "Dominion_Ops_CounterIntelUntil".Translate((svc.counterIntelUntil - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)).ToString()
                : "Dominion_Ops_CounterIntelButton".Translate(IntelOpsSystem.CounterIntelCost.ToString("F0")).ToString();
            string ciWhy = svc.CounterIntelActive ? "Dominion_Ops_CounterIntelActive".Translate().ToString()
                : comp.Player.influence < IntelOpsSystem.CounterIntelCost ? "Dominion_Action_NeedsInfluence".Translate(IntelOpsSystem.CounterIntelCost.ToString("F0")).ToString() : null;
            if (Terminal.Button(new Rect(rect.x, y, 380f, 26f), ci, "Dominion_Ops_CounterIntel_Desc".Translate(IntelOpsSystem.CounterIntelDays), ciWhy, svc.CounterIntelActive))
            {
                string error = IntelOpsSystem.StartCounterIntel();
                if (error != null) Messages.Message(error, MessageTypeDefOf.RejectInput, historical: false);
            }
            y += 34f;

            Rect left = new Rect(rect.x, y, rect.width * 0.58f, rect.yMax - y);
            Rect right = new Rect(left.xMax + rect.width * 0.02f, y, rect.width * 0.4f, rect.yMax - y);

            Terminal.Label(new Rect(left.x, left.y, left.width, 22f), "Dominion_Term_Suspicions".Translate().ToString().ToUpper(), Terminal.Amber);
            List<Suspicion> sus = svc.suspicions.Where(s => s.faction != null && !s.faction.defeated).OrderByDescending(s => s.tick).ToList();
            Rect listRect = new Rect(left.x, left.y + 26f, left.width, left.height - 26f);
            if (sus.Count == 0)
                Terminal.Label(listRect, "Dominion_Term_NoSuspicions".Translate().ToString().ToUpper(), Terminal.Dim);
            else
            {
                Rect view = new Rect(0f, 0f, listRect.width - 16f, sus.Count * (Line * 2f + 8f));
                Widgets.BeginScrollView(listRect, ref leftScroll, view);
                float sy = 0f;
                int now = Find.TickManager.TicksGame;
                foreach (Suspicion s in sus)
                {
                    Rect row = new Rect(0f, sy, view.width, Line * 2f + 4f);
                    Terminal.Label(new Rect(row.x, row.y, row.width - 170f, Line), (s.faction.Name + " — " + s.kind).ToUpper().Truncate(row.width - 170f), Terminal.Green);
                    Terminal.Label(new Rect(row.x + 12f, row.y + Line, row.width - 180f, Line),
                        "CONFIDENCE " + Terminal.Bar(s.confidence, 10) + "   " + "Dominion_News_Ago".Translate((now - s.tick).ToStringTicksToPeriod(allowSeconds: false)).ToString().ToUpper(),
                        Terminal.Dim, GameFont.Tiny);
                    Suspicion captured = s;
                    if (Terminal.Button(new Rect(row.xMax - 164f, row.y + 8f, 164f, 26f), "Dominion_Term_Sweep".Translate(IntelOpsSystem.SweepChance(s).ToStringPercent()),
                            "Dominion_Term_Sweep_Desc".Translate(IntelOpsSystem.SweepCost.ToString("F0"), s.faction.Name), IntelOpsSystem.CannotSweepReason(), false, Terminal.Amber))
                        IntelOpsSystem.Sweep(captured);
                    sy += Line * 2f + 8f;
                }
                Widgets.EndScrollView();
            }

            Terminal.Label(new Rect(right.x, right.y, right.width, 22f), "Dominion_Term_TheirFiles".Translate().ToString().ToUpper(), Terminal.Amber);
            float ry = right.y + 26f;
            foreach (Faction f in comp.Cache.Simulated.OrderByDescending(f => comp.TryGetState(f, out FactionState s) ? s.playerIntel : 0f))
            {
                if (ry > right.yMax - Line) break;
                comp.TryGetState(f, out FactionState s);
                float theirs = s?.playerIntel ?? 0f;
                bool known = IntelUtility.Knows(f, IntelUtility.SecretIntel);
                Terminal.Label(new Rect(right.x, ry, right.width * 0.5f, Line), f.Name.ToUpper().Truncate(right.width * 0.5f), Terminal.Dim, GameFont.Tiny);
                Terminal.Label(new Rect(right.x + right.width * 0.5f, ry, right.width * 0.5f, Line),
                    known ? Terminal.Bar(theirs / 100f, 10) : new string('█', 10), known ? (theirs > 50f ? Terminal.Alert : Terminal.Green) : Terminal.Faint, GameFont.Tiny);
                ry += Line;
            }
            TooltipHandler.TipRegion(right, "Dominion_Term_TheirFiles_Desc".Translate(IntelUtility.SecretIntel.ToString("F0")));
        }

        // ------------------------------------------------------------------ archive

        private void DrawArchive(Rect rect, IntelService svc)
        {
            if (svc.history.Count == 0)
            {
                Terminal.Label(rect, "Dominion_Term_ArchiveEmpty".Translate().ToString().ToUpper(), Terminal.Dim);
                return;
            }
            Rect view = new Rect(0f, 0f, rect.width - 16f, svc.history.Count * (Line + 4f));
            Widgets.BeginScrollView(rect, ref leftScroll, view);
            float y = 0f;
            for (int i = svc.history.Count - 1; i >= 0; i--)
            {
                OpRecord r = svc.history[i];
                Rect row = new Rect(0f, y, view.width, Line);
                Color c = r.outcome == OpOutcome.Success ? Terminal.Green : r.outcome == OpOutcome.Recalled ? Terminal.Dim : r.outcome == OpOutcome.Failed ? Terminal.Amber : Terminal.Alert;
                Terminal.Label(new Rect(row.x, row.y, 150f, Line), "[" + ("Dominion_OpOutcome_" + r.outcome).Translate().ToString().ToUpper() + "]", c);
                Terminal.Label(new Rect(row.x + 150f, row.y, row.width - 330f, Line),
                    (r.def.label + "  →  " + (r.target?.Name ?? r.targetName ?? "?")).ToUpper().Truncate(row.width - 330f), Terminal.Green);
                Terminal.Label(new Rect(row.xMax - 176f, row.y, 176f, Line), DominionPaper.Date(r.tick).ToUpper(), Terminal.Dim, GameFont.Tiny, TextAnchor.MiddleRight);
                TooltipHandler.TipRegion(row, r.def.LabelCap + "\n" + "Dominion_Term_F_Agents".Translate() + ": " + (r.agents.NullOrEmpty() ? "-" : r.agents));
                y += Line + 4f;
            }
            Widgets.EndScrollView();
        }
    }
}
