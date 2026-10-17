using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// God-mode tools for Dominion: push any faction's numbers around, start and end wars, sign and break treaties,
    /// drive the council, fire events and play with IRBM's doomsday clock. Opened from the Dominion window's god-mode
    /// button, which only exists while god mode is on. Two factions are picked at the top: most tools act on the first,
    /// tools between two factions act on both.
    /// </summary>
    public class Dialog_DominionGodMode : Window
    {
        private enum Page { Faction, War, Council, Events }

        private static Page page = Page.Faction;
        private static Faction a;
        private static Faction b;
        private Vector2 scroll;
        private float height = 600f;

        public override Vector2 InitialSize => new Vector2(760f, 640f);

        public Dialog_DominionGodMode()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        public override void DoWindowContents(Rect inRect)
        {
            if (!DebugSettings.godMode)
            {
                Close();
                return;
            }
            List<Faction> factions = Comp.Cache.Simulated;
            if (a == null || a.defeated) a = DominionUIState.selected ?? factions.FirstOrDefault();
            if (b != null && b.defeated) b = null;

            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.45f, 0.35f);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Dominion_God_Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // The two factions the tools act on.
            float y = inRect.y + 36f;
            PickButton(new Rect(inRect.x, y, inRect.width / 2f - 4f, 28f), "A", a, f => a = f, factions);
            PickButton(new Rect(inRect.x + inRect.width / 2f + 4f, y, inRect.width / 2f - 4f, 28f), "B", b, f => b = f, factions);
            y += 34f;

            float tw = inRect.width / 4f;
            int i = 0;
            foreach (Page p in System.Enum.GetValues(typeof(Page)))
            {
                Rect t = new Rect(inRect.x + i++ * tw, y, tw - 4f, 28f);
                if (page == p) Widgets.DrawHighlightSelected(t);
                if (Widgets.ButtonText(t, ("Dominion_God_Page_" + p).Translate())) page = p;
            }
            y += 34f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, height);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            var l = new Listing_Standard { ColumnWidth = (view.width - 20f) / 2f };
            l.Begin(view);
            switch (page)
            {
                case Page.Faction: FactionTools(l); break;
                case Page.War: WarTools(l); break;
                case Page.Council: CouncilTools(l); break;
                case Page.Events: EventTools(l); break;
            }
            height = Mathf.Max(l.MaxColumnHeightSeen + 20f, outRect.height);
            l.End();
            Widgets.EndScrollView();
        }

        private static void PickButton(Rect r, string slot, Faction current, System.Action<Faction> set, List<Faction> factions)
        {
            string label = slot + ": " + (current?.Name ?? "Dominion_None".Translate().ToString());
            if (!Widgets.ButtonText(r, label.Truncate(r.width - 8f))) return;
            var options = new List<FloatMenuOption> { new FloatMenuOption("Dominion_None".Translate(), () => set(null)) };
            if (Faction.OfPlayerSilentFail != null) options.Add(new FloatMenuOption(Faction.OfPlayer.Name, () => set(Faction.OfPlayer)));
            foreach (Faction f in factions)
            {
                Faction captured = f;
                options.Add(new FloatMenuOption(f.Name, () => set(captured), f.def.FactionIcon, f.Color));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static bool Need(Faction f, string slot)
        {
            if (f != null) return true;
            Messages.Message("Dominion_God_PickFirst".Translate(slot), MessageTypeDefOf.RejectInput, historical: false);
            return false;
        }

        private static void Done(string what) => Messages.Message("Dominion_God_Done".Translate(what), MessageTypeDefOf.SilentInput, historical: false);

        private static void Heading(Listing_Standard l, string key)
        {
            l.Gap(4f);
            GUI.color = new Color(1f, 0.6f, 0.45f);
            l.Label(key.Translate());
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ faction

        private static void FactionTools(Listing_Standard l)
        {
            FactionState s = a != null && !a.IsPlayer && Comp.TryGetState(a, out FactionState st) ? st : null;
            Heading(l, "Dominion_God_H_Relations");
            if (l.ButtonText("Dominion_God_Goodwill".Translate("+20")) && Need(a, "A") && !a.IsPlayer) a.TryAffectGoodwillWith(Faction.OfPlayer, 20, false, false);
            if (l.ButtonText("Dominion_God_Goodwill".Translate("-20")) && Need(a, "A") && !a.IsPlayer) a.TryAffectGoodwillWith(Faction.OfPlayer, -20, false, false);
            if (l.ButtonText("Dominion_God_Opinion".Translate("+25")) && Need(a, "A")) Opinion(25f);
            if (l.ButtonText("Dominion_God_Opinion".Translate("-25")) && Need(a, "A")) Opinion(-25f);
            if (l.ButtonText("Dominion_God_ClearGrievance".Translate()) && Need(a, "A"))
            {
                Comp.GetRelation(a, b ?? Faction.OfPlayer).grievance = 0f;
                Done("grievance");
            }

            Heading(l, "Dominion_God_H_State");
            if (s != null)
            {
                l.Label("Dominion_God_Numbers".Translate(s.stability.ToString("F0"), s.economyIndex.ToString("F0"), s.militaryStrength.ToString("F0"), s.legitimacy.ToString("F0")));
                if (l.ButtonText("Dominion_God_Stability".Translate("+10"))) s.stability = Mathf.Clamp(s.stability + 10f, 0f, 100f);
                if (l.ButtonText("Dominion_God_Stability".Translate("-10"))) s.stability = Mathf.Clamp(s.stability - 10f, 0f, 100f);
                if (l.ButtonText("Dominion_God_Economy".Translate("+10%"))) EconomyEffects.Shock(a, 0.1f, destroyGoods: false);
                if (l.ButtonText("Dominion_God_Economy".Translate("-10%"))) EconomyEffects.Shock(a, -0.1f);
                if (l.ButtonText("Dominion_God_Military".Translate("+25%"))) s.militaryStrength *= 1.25f;
                if (l.ButtonText("Dominion_God_Military".Translate("-25%"))) s.militaryStrength *= 0.75f;
                if (l.ButtonText("Dominion_God_Legitimacy".Translate("+20"))) s.legitimacy = Mathf.Clamp(s.legitimacy + 20f, 0f, 100f);
                if (l.ButtonText("Dominion_God_Government".Translate(DominionUI.GovernmentLabel(s.government))))
                    Find.WindowStack.Add(new FloatMenu(System.Enum.GetValues(typeof(GovernmentType)).Cast<GovernmentType>()
                        .Select(g => new FloatMenuOption(DominionUI.GovernmentLabel(g), () =>
                        {
                            s.government = g;
                            s.governmentInitialized = true;
                            Done(DominionUI.GovernmentLabel(g));
                        })).ToList()));
            }
            else l.Label("Dominion_God_PickAI".Translate());

            l.NewColumn();
            Heading(l, "Dominion_God_H_Leader");
            if (l.ButtonText("Dominion_God_KillLeader".Translate()) && Need(a, "A") && !a.IsPlayer)
            {
                Pawn leader = a.leader;
                if (leader != null && !leader.Dead) leader.Kill(null);
                if (a.leader == null || a.leader.Dead) a.TryGenerateNewLeader();
                Done("leader");
            }
            if (l.ButtonText("Dominion_God_NewLeader".Translate()) && Need(a, "A") && !a.IsPlayer)
            {
                a.TryGenerateNewLeader();
                Done(a.leader?.LabelShortCap ?? "leader");
            }

            Heading(l, "Dominion_God_H_Intel");
            if (l.ButtonText("Dominion_God_Intel100".Translate()))
            {
                foreach (Faction f in a != null && !a.IsPlayer ? new List<Faction> { a } : Comp.Cache.Simulated)
                    if (Comp.TryGetState(f, out FactionState fs)) fs.playerIntel = 100f;
                Done("intel");
            }
            if (l.ButtonText((IntelUtility.RevealAll ? "Dominion_God_RevealOff" : "Dominion_God_RevealOn").Translate()))
                IntelUtility.RevealAll = !IntelUtility.RevealAll;
        }

        private static void Opinion(float amount)
        {
            Faction other = b ?? Faction.OfPlayer;
            if (other == a) return;
            Comp.GetRelation(a, other).opinion = Mathf.Clamp(Comp.GetRelation(a, other).opinion + amount, -100f, 100f);
            Done("opinion");
        }

        // ------------------------------------------------------------------ war and treaties

        private static void WarTools(Listing_Standard l)
        {
            Heading(l, "Dominion_God_H_War");
            if (l.ButtonText("Dominion_God_DeclareWar".Translate()) && Need(a, "A") && Need(b, "B") && a != b)
            {
                DiplomacyUtility.SetRelationKind(a, b, FactionRelationKind.Hostile, a, "Dominion_God_Reason".Translate());
                Done("war");
            }
            if (l.ButtonText("Dominion_God_MakePeace".Translate()) && Need(a, "A") && Need(b, "B") && a != b)
            {
                DiplomacyUtility.SetRelationKind(a, b, FactionRelationKind.Neutral, a, "Dominion_God_Reason".Translate());
                Done("peace");
            }
            War w = a != null && b != null ? Comp.Wars.wars.FirstOrDefault(x => x.Involves(a) && x.Involves(b)) : null;
            if (w != null)
            {
                l.Label("Dominion_God_Score".Translate(w.Label, w.score.ToString("F0")));
                if (l.ButtonText("Dominion_God_ScoreFor".Translate(a.Name, 25))) WarUtility.AddScore(a, b, 25f);
                if (l.ButtonText("Dominion_God_ScoreFor".Translate(b.Name, 25))) WarUtility.AddScore(b, a, 25f);
                if (l.ButtonText("Dominion_God_Capitulate".Translate(b.Name))) WarUtility.Capitulate(w, b);
            }

            Heading(l, "Dominion_God_H_Warbands");
            if (l.ButtonText("Dominion_God_SpawnWarband".Translate()) && Need(a, "A") && Need(b, "B") && a != b && !a.IsPlayer)
            {
                WarbandMission mission = b.IsPlayer ? WarbandMission.RaidPlayer : WarbandMission.Siege;
                Done(WarbandUtility.Launch(a, b, mission) != null ? "warband" : "no warband (no route or settlement)");
            }
            if (l.ButtonText("Dominion_God_RecallWarbands".Translate()) && Need(a, "A"))
            {
                foreach (Warband band in WarbandUtility.All().Where(x => x.Faction == a).ToList()) band.Destroy();
                Done("recalled");
            }

            l.NewColumn();
            Heading(l, "Dominion_God_H_Treaties");
            if (l.ButtonText("Dominion_God_Sign".Translate()) && Need(a, "A") && Need(b, "B") && a != b)
                Find.WindowStack.Add(new FloatMenu(DefDatabase<TreatyDef>.AllDefsListForReading.Select(def => new FloatMenuOption(def.LabelCap, () =>
                {
                    string why = Comp.Treaties.CannotSignReason(def, a, b);
                    if (why != null) Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                    else
                    {
                        Comp.Treaties.Sign(def, a, b, a);
                        Done(def.label);
                    }
                })).ToList()));
            if (l.ButtonText("Dominion_God_Break".Translate()) && Need(a, "A"))
            {
                List<FloatMenuOption> options = Comp.Treaties.All.Where(t => t.Involves(a) && (b == null || t.Involves(b))).ToList()
                    .Select(t => new FloatMenuOption(t.Label + " (" + t.a.Name + " / " + t.b.Name + ")", () =>
                    {
                        Comp.Treaties.Break(t, a);
                        Done(t.Label);
                    })).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            foreach (System.Action<Listing_Standard> extra in ExtraWarTools) extra(l);
        }

        /// <summary>Other parts of Dominion (trade deals) add their own god-mode buttons to the war and treaties page.</summary>
        public static readonly List<System.Action<Listing_Standard>> ExtraWarTools = new List<System.Action<Listing_Standard>>();

        /// <summary>Other parts of Dominion (council motions) add their own god-mode buttons to the council page.</summary>
        public static readonly List<System.Action<Listing_Standard>> ExtraCouncilTools = new List<System.Action<Listing_Standard>>();

        public static Faction PickedA => a;
        public static Faction PickedB => b;

        // ------------------------------------------------------------------ council

        private static void CouncilTools(Listing_Standard l)
        {
            CouncilState c = Comp.Council;
            Heading(l, "Dominion_God_H_Council");
            l.Label("Dominion_God_CouncilState".Translate(c.phase.ToString(), c.agenda.Count, c.decrees.Count, c.disbanded ? "Dominion_Yes".Translate() : "Dominion_No".Translate()));
            if (!c.disbanded && l.ButtonText("Dominion_God_Disband".Translate()))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_God_DisbandConfirm".Translate(),
                    () => CouncilSystem.DisbandPermanently("Dominion_God_Reason".Translate()), destructive: true));
            if (c.disbanded && l.ButtonText("Dominion_God_Restore".Translate()))
            {
                CouncilSystem.Restore();
                Done("council");
            }
            foreach (Motion m in c.agenda)
                if (l.ButtonText((m.vetoed ? "Dominion_Council_Vetoed" : "Dominion_Council_DevVeto").Translate() + ": " + m.Summary))
                    m.vetoed = !m.vetoed;
            foreach (System.Action<Listing_Standard> extra in ExtraCouncilTools) extra(l);
        }

        // ------------------------------------------------------------------ events and IRBM

        private static void EventTools(Listing_Standard l)
        {
            Heading(l, "Dominion_God_H_Events");
            if (l.ButtonText("Dominion_God_FireEvent".Translate()))
                Find.WindowStack.Add(new FloatMenu(DefDatabase<DominionEventDef>.AllDefsListForReading.OrderBy(d => d.defName)
                    .Select(d => new FloatMenuOption(d.defName, () =>
                    {
                        EventBus.Raise(d, a, b, detail: "god mode");
                        Done(d.defName);
                    })).ToList()));
            if (l.ButtonText("Dominion_God_DeliverNews".Translate()))
            {
                NewsLog log = Comp.News;
                foreach (NewsItem n in log.Pending.ToList()) log.MarkDelivered(n);
                Done("news");
            }

            l.NewColumn();
            Heading(l, "Dominion_God_H_IRBM");
            if (Integration_IRBM.Instance == null)
            {
                l.Label("Dominion_God_NoIRBM".Translate());
                return;
            }
            l.Label("Dominion_God_IRBMState".Translate(Integration_IRBM.Phase.ToString(), Integration_IRBM.ClockText()));
            if (l.ButtonText("Dominion_God_StrikeTactical".Translate()) && Need(a, "A") && Need(b, "B") && a != b) Strike(false);
            if (l.ButtonText("Dominion_God_StrikeStrategic".Translate()) && Need(a, "A") && Need(b, "B") && a != b) Strike(true);
            if (l.ButtonText("Dominion_God_Clock".Translate(60))) Integration_IRBM.DevAdvanceClock(60f);
            if (l.ButtonText("Dominion_God_ClockReset".Translate())) Integration_IRBM.DevResetClock();
            if (l.ButtonText("Dominion_God_NuclearWar".Translate()))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Dominion_God_NuclearWarConfirm".Translate(), Integration_IRBM.DevStartNuclearWar, destructive: true));
            if (l.ButtonText("Dominion_God_EndNuclearWar".Translate())) Integration_IRBM.DevEndNuclearWar();
            if (l.ButtonText("Dominion_God_EndWinter".Translate())) Integration_IRBM.DevEndWinter();
        }

        /// <summary>Dominion's side of a missile strike from A on B's capital (no missile flies; IRBM's own debug tools do that).</summary>
        private static void Strike(bool strategic)
        {
            Settlement target = WorldMapView.Seat(b);
            Integration_IRBM.MissileStrike(a, b, target, strategic);
            Done(strategic ? "strategic strike" : "tactical strike");
        }
    }
}
