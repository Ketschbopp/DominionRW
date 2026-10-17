using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Dominion
{
    /// <summary>
    /// An audience with a faction's leader over the comms: their portrait in a brass frame, the conversation typed out
    /// like a telegram, and the topics you can raise: small talk, grievances (apologise, pay amends, demand an
    /// apology), rumours, threats, asking for help in your wars, trade and treaties. Every answer comes in the
    /// faction's own voice and has consequences. Leaders have only so much patience: each exchange uses some, and when
    /// it runs out they end the audience. One audience per faction every few days.
    /// </summary>
    public class Dialog_Audience : Window
    {
        public const int CooldownDays = 3;
        public const float ApologyCost = 5f;
        private const string CooldownKey = "Audience";

        private enum Node { Root, Grievances, Rumours, Threats, Help }

        private readonly Faction faction;
        private readonly List<(bool ours, string text, int outcome)> transcript = new List<(bool, string, int)>();
        private readonly HashSet<string> used = new HashSet<string>();
        private Node node = Node.Root;
        private int patience;
        private bool over;
        private Vector2 scroll;
        private float transcriptHeight;
        private bool scrollToEnd;

        public override Vector2 InitialSize => new Vector2(900f, 660f);

        protected override float Margin => 0f;

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        private RelationState TheirView => Comp.GetRelation(faction, Faction.OfPlayer);

        private RelationState OurView => Comp.GetRelation(Faction.OfPlayer, faction);

        private AIPersonality Temper => Comp.GetState(faction).personality;

        public Dialog_Audience(Faction faction)
        {
            this.faction = faction;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doWindowBackground = false;
            drawShadow = true;
            AIPersonality p = Temper;
            patience = Mathf.Clamp(3 + Mathf.RoundToInt(p.honour * 2f + TheirView.opinion / 40f) - (faction.HostileTo(Faction.OfPlayer) ? 1 : 0), 1, 7);
            Comp.Player.SetCooldown(CooldownKey, faction.loadID, Find.TickManager.TicksGame + CooldownDays * GenDate.TicksPerDay);
            Say(false, FactionVoice.Line(faction, faction.HostileTo(Faction.OfPlayer) ? VoiceLine.Threat : VoiceLine.Greeting, Find.TickManager.TicksGame), 0);
            Comp.Player.Log(faction, "Dominion_Audience_Log".Translate(faction.leader?.LabelShortCap ?? faction.Name), 0);
        }

        /// <summary>Why they will not receive you now, or null.</summary>
        public static string CannotHoldReason(Faction f)
        {
            WorldComponent_Dominion comp = Comp;
            if (comp == null || f == null || f.IsPlayer || f.defeated || !comp.TryGetState(f, out FactionState s)) return "Dominion_Action_Invalid".Translate();
            if (LeadershipUtility.BestDesk() == null && !LeadershipUtility.HasComms()) return "Dominion_Action_NeedsComms".Translate();
            int until = comp.Player.CooldownUntil(CooldownKey, f.loadID);
            int now = Find.TickManager.TicksGame;
            if (until > now) return "Dominion_Action_Cooldown".Translate((until - now).ToStringTicksToPeriod(allowSeconds: false));
            if (DiplomacyTalk.Shuns(f)) return "Dominion_Talk_Shuns".Translate(f.Name);
            if (f.HostileTo(Faction.OfPlayer) && s.personality.aggression >= 0.6f && s.warWeariness < 50f) return "Dominion_Audience_Refused".Translate(f.Name);
            return null;
        }

        public static void Request(Faction f)
        {
            string why = CannotHoldReason(f);
            if (why != null) Messages.Message(why, MessageTypeDefOf.RejectInput, historical: false);
            else Find.WindowStack.Add(new Dialog_Audience(f));
        }

        private void Say(bool ours, string text, int outcome)
        {
            if (text.NullOrEmpty()) return;
            transcript.Add((ours, text, outcome));
            scrollToEnd = true;
        }

        /// <summary>An exchange uses up some of their patience; when it is gone, they end the audience.</summary>
        private void Spend()
        {
            patience--;
            if (patience > 0) return;
            over = true;
            Say(false, FactionVoice.Line(faction, VoiceLine.Farewell, transcript.Count) + "  " + "Dominion_Audience_Ended".Translate(), 0);
        }

        // ------------------------------------------------------------------ drawing

        public override void DoWindowContents(Rect inRect)
        {
            // The comms room: panelling, a brass frame, a lamp on the screen.
            DominionRooms.DrawTiled(inRect, DominionRooms.Wood, 0.7f);
            DominionRooms.DrawGlow(new Vector2(inRect.x + 140f, inRect.y + 160f), 260f, new Color(1f, 0.85f, 0.55f, 0.18f));
            DominionRooms.DrawVignette(inRect, 0.6f);
            DominionRooms.DrawMoulding(inRect);
            Rect inner = inRect.ContractedBy(16f);
            Rect left = new Rect(inner.x, inner.y, 230f, inner.height);
            Rect right = new Rect(left.xMax + 14f, inner.y, inner.width - left.width - 14f, inner.height);
            DrawLeader(left);
            // Five rows of two: the root menu has nine choices.
            Rect choices = new Rect(right.x, right.yMax - 180f, right.width, 180f);
            DrawTranscript(new Rect(right.x, right.y, right.width, choices.y - right.y - 10f));
            DrawChoices(choices);
        }

        private void DrawLeader(Rect rect)
        {
            Rect frame = new Rect(rect.x, rect.y, rect.width, rect.width);
            Widgets.DrawBoxSolid(frame, new Color(0.06f, 0.05f, 0.04f));
            Rect pic = frame.ContractedBy(8f);
            bool drawn = false;
            if (faction.leader != null)
            {
                try
                {
                    GUI.DrawTexture(pic, PortraitsCache.Get(faction.leader, pic.size, Rot4.South, default, 1.15f));
                    drawn = true;
                }
                catch (System.Exception e)
                {
                    DLog.WarningOnce("Drawing a leader's portrait failed: " + e.Message, faction.loadID ^ 0xA0D);
                }
            }
            if (!drawn)
            {
                GUI.color = faction.Color;
                GUI.DrawTexture(pic.ContractedBy(30f), faction.def.FactionIcon);
                GUI.color = Color.white;
            }
            GUI.DrawTexture(new Rect(frame.x, frame.y, frame.width, 6f), DominionRooms.Brass, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(frame.x, frame.yMax - 6f, frame.width, 6f), DominionRooms.Brass, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(frame.x, frame.y, 6f, frame.height), DominionRooms.Brass, ScaleMode.StretchToFill);
            GUI.DrawTexture(new Rect(frame.xMax - 6f, frame.y, 6f, frame.height), DominionRooms.Brass, ScaleMode.StretchToFill);
            if (faction.leader != null) TooltipHandler.TipRegion(frame, faction.leader.LabelCap);

            float y = frame.yMax + 8f;
            DominionRooms.BrassPlate(new Rect(rect.x, y, rect.width, 28f), faction.leader?.LabelShortCap ?? faction.Name);
            y += 32f;
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 20f), faction.Name.Truncate(rect.width), DominionRooms.BrassLight, GameFont.Small, TextAnchor.MiddleCenter);
            y += 22f;
            Voice v = FactionVoice.Of(faction);
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 18f), "Dominion_Audience_Manner".Translate(FactionVoice.Label(v)), new Color(0.85f, 0.78f, 0.65f), GameFont.Tiny, TextAnchor.MiddleCenter);
            y += 22f;
            FactionRelationKind kind = faction.RelationKindWith(Faction.OfPlayer);
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 18f), kind.GetLabelCap() + "  (" + faction.PlayerGoodwill.ToStringWithSign() + ")", kind.GetColor(), GameFont.Tiny, TextAnchor.MiddleCenter);
            y += 26f;

            // Patience as a row of lamps.
            DominionTheme.InkLabel(new Rect(rect.x, y, rect.width, 18f), "Dominion_Audience_Patience".Translate().ToString().ToUpper(), DominionRooms.BrassLight, GameFont.Tiny, TextAnchor.MiddleCenter);
            y += 20f;
            const float Lamp = 14f;
            int max = 7;
            float x0 = rect.center.x - (max * (Lamp + 4f)) / 2f;
            for (int i = 0; i < max; i++)
            {
                Rect lamp = new Rect(x0 + i * (Lamp + 4f), y, Lamp, Lamp);
                bool lit = i < patience;
                Widgets.DrawBoxSolid(lamp, lit ? new Color(1f, 0.75f, 0.3f) : new Color(0.2f, 0.15f, 0.1f));
                if (lit) DominionRooms.DrawGlow(lamp.center, 12f, new Color(1f, 0.7f, 0.3f, 0.35f));
            }
            TooltipHandler.TipRegion(new Rect(rect.x, y - 20f, rect.width, 40f), "Dominion_Audience_Patience_Desc".Translate(faction.Name));
        }

        private void DrawTranscript(Rect rect)
        {
            Rect write = DominionRooms.DrawTelegram(rect, "Dominion_Audience_Head".Translate(faction.Name.ToUpper()));
            Rect view = new Rect(0f, 0f, write.width - 16f, Mathf.Max(write.height, transcriptHeight));
            if (scrollToEnd && Event.current.type == EventType.Layout)
            {
                scroll.y = Mathf.Max(0f, transcriptHeight - write.height);
                scrollToEnd = false;
            }
            Widgets.BeginScrollView(write, ref scroll, view);
            float y = 0f;
            string them = (faction.leader?.LabelShort ?? faction.Name).ToUpper();
            foreach ((bool ours, string text, int outcome) in transcript)
            {
                string who = ours ? "Dominion_Audience_You".Translate().ToString() : them;
                string line = who + ":  " + text;
                Text.Font = GameFont.Small;
                float h = Text.CalcHeight(line, view.width - 20f);
                Color ink = ours ? DominionTheme.InkFaded : outcome > 0 ? DominionTheme.InkGreen : outcome < 0 ? DominionTheme.InkRed : DominionTheme.Ink;
                DominionTheme.InkLabel(new Rect(ours ? 20f : 0f, y, view.width - 20f, h), line, ink);
                y += h + 6f;
            }
            transcriptHeight = y + 6f;
            Widgets.EndScrollView();
        }

        private struct Choice
        {
            public string label;
            public string tip;
            public string why;
            public System.Action act;
        }

        private void DrawChoices(Rect rect)
        {
            var choices = new List<Choice>();
            if (over)
                choices.Add(new Choice { label = "Dominion_Audience_Close".Translate(), act = () => Close() });
            else
                switch (node)
                {
                    case Node.Root: RootChoices(choices); break;
                    case Node.Grievances: GrievanceChoices(choices); break;
                    case Node.Rumours: RumourChoices(choices); break;
                    case Node.Threats: ThreatChoices(choices); break;
                    case Node.Help: HelpChoices(choices); break;
                }
            int cols = 2;
            float w = (rect.width - 6f) / cols;
            const float H = 32f;
            for (int i = 0; i < choices.Count; i++)
            {
                Choice c = choices[i];
                Rect b = new Rect(rect.x + i % cols * (w + 6f), rect.y + i / cols * (H + 4f), w, H);
                if (DominionRooms.BrassButton(b, c.label, c.tip, c.why)) c.act?.Invoke();
            }
        }

        private static Choice Make(string label, string tip, string why, System.Action act) => new Choice { label = label, tip = tip, why = why, act = act };

        private string UsedReason(string key) => used.Contains(key) ? "Dominion_Audience_AlreadyAsked".Translate().ToString() : null;

        // ------------------------------------------------------------------ the topics

        private void RootChoices(List<Choice> c)
        {
            c.Add(Make("Dominion_Audience_SmallTalk".Translate(), "Dominion_Audience_SmallTalk_Desc".Translate(), UsedReason("small"), SmallTalk));
            c.Add(Make("Dominion_Audience_Grievances".Translate() + " ▸", "Dominion_Audience_Grievances_Desc".Translate(), null, () => node = Node.Grievances));
            c.Add(Make("Dominion_Audience_Rumours".Translate() + " ▸", "Dominion_Audience_Rumours_Desc".Translate(), null, () => node = Node.Rumours));
            c.Add(Make("Dominion_Audience_Threats".Translate() + " ▸", "Dominion_Audience_Threats_Desc".Translate(), null, () => node = Node.Threats));
            c.Add(Make("Dominion_Talk_AskWants".Translate(), DiplomacyTalk.Desc(TalkTopic.AskWants), TalkReason(TalkTopic.AskWants), () => Topic(TalkTopic.AskWants)));
            bool atWar = Comp.Wars.AnyWar(Faction.OfPlayer);
            c.Add(Make("Dominion_Audience_Help".Translate() + " ▸", "Dominion_Audience_Help_Desc".Translate(),
                !atWar ? "Dominion_Audience_NoWar".Translate().ToString() : faction.HostileTo(Faction.OfPlayer) ? "Dominion_Action_NotWhileHostile".Translate().ToString() : null,
                () => node = Node.Help));
            c.Add(Make("Dominion_Audience_Deal".Translate(), "Dominion_Deal_Trade_Desc".Translate(faction.Name, TradeDeals.OfficeDays), TradeDeals.CannotDealReason(faction), () =>
            {
                Say(true, "Dominion_Audience_Said_Deal".Translate(), 0);
                Say(false, FactionVoice.Line(faction, VoiceLine.Bargain, transcript.Count), 0);
                TradeDeals.OpenTrade(faction);
            }));
            DiplomacyActionDef propose = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposeTreaty");
            if (propose != null)
                c.Add(Make("Dominion_Audience_Treaty".Translate(), propose.description, DiplomacyActions.CannotUseReason(propose, faction), () =>
                {
                    Say(true, "Dominion_Audience_Said_Treaty".Translate(), 0);
                    Dialog_TreatyDraft.ProposeMenu(faction);
                }));
            c.Add(Make("Dominion_Audience_End".Translate(), null, null, () =>
            {
                Say(true, "Dominion_Audience_Said_End".Translate(), 0);
                Say(false, FactionVoice.Line(faction, VoiceLine.Farewell, transcript.Count), 0);
                over = true;
            }));
        }

        private void SmallTalk()
        {
            used.Add("small");
            Say(true, "Dominion_Audience_Said_SmallTalk".Translate(), 0);
            float op = TheirView.opinion;
            Voice v = FactionVoice.Of(faction);
            int outcome = 0;
            if (op > -20f)
            {
                float favour = v == Voice.Festive || v == Voice.Gentle || v == Voice.Courtly ? 2f : v == Voice.Aloof || v == Voice.Stern ? 0.5f : 1f;
                TheirView.favour += favour;
                outcome = favour >= 1f ? 1 : 0;
                Say(false, FactionVoice.Line(faction, VoiceLine.SmallTalk, transcript.Count), outcome);
            }
            else Say(false, FactionVoice.Line(faction, VoiceLine.Rebuff, transcript.Count), -1);
            Spend();
        }

        private string TalkReason(TalkTopic t) => DiplomacyTalk.CannotTalkReason(faction, t);

        private void Topic(TalkTopic t, Faction about = null)
        {
            Say(true, ("Dominion_Audience_Said_" + t).Translate(about?.Name ?? ""), 0);
            TalkResult r = DiplomacyTalk.Talk(faction, t, about);
            Say(false, r.reply + (r.fact.NullOrEmpty() ? "" : "  (" + r.fact + ")"), r.outcome);
            Spend();
        }

        private void GrievanceChoices(List<Choice> c)
        {
            float theirs = TheirView.grievance;
            c.Add(Make("Dominion_Audience_Apologise".Translate(ApologyCost.ToString("F0")), "Dominion_Audience_Apologise_Desc".Translate(faction.Name),
                UsedReason("apology") ?? (theirs < 5f ? "Dominion_Audience_NoTheirGrievance".Translate(faction.Name).ToString()
                    : Comp.Player.influence < ApologyCost ? "Dominion_Action_NeedsInfluence".Translate(ApologyCost.ToString("F0")).ToString() : null), Apologise));
            int amends = AmendsCost();
            c.Add(Make("Dominion_Audience_Amends".Translate(amends), "Dominion_Audience_Amends_Desc".Translate(faction.Name),
                UsedReason("amends") ?? (theirs < 5f ? "Dominion_Audience_NoTheirGrievance".Translate(faction.Name).ToString()
                    : DiplomacyHelpers.AvailableSilver() < amends ? "Dominion_Action_NeedsSilver".Translate(amends).ToString() : null), PayAmends));
            c.Add(Make("Dominion_Audience_DemandApology".Translate(), DiplomacyTalk.Desc(TalkTopic.Complain), TalkReason(TalkTopic.Complain), () => Topic(TalkTopic.Complain)));
            c.Add(Make("Dominion_Audience_Back".Translate(), null, null, () => node = Node.Root));
        }

        private int AmendsCost() => 100 + Mathf.RoundToInt(TheirView.grievance * 10f / 50f) * 50;

        private void Apologise()
        {
            used.Add("apology");
            Comp.Player.AddInfluence(-ApologyCost);
            Say(true, "Dominion_Audience_Said_Apology".Translate(), 0);
            AIPersonality p = Temper;
            Voice v = FactionVoice.Of(faction);
            float chance = Mathf.Clamp(0.4f + p.honour * 0.3f + TheirView.opinion / 300f + (v == Voice.Gentle || v == Voice.Honourable ? 0.15f : 0f) - (p.aggression - 0.5f) * 0.3f, 0.05f, 0.95f);
            if (Rand.Chance(chance))
            {
                float eased = Mathf.Min(TheirView.grievance, 20f);
                TheirView.grievance -= eased;
                TheirView.favour += 3f;
                Say(false, FactionVoice.Line(faction, VoiceLine.Accept, transcript.Count) + "  (" + "Dominion_Audience_Forgiven".Translate(eased.ToString("F0")) + ")", 1);
                Comp.Player.Log(faction, "Dominion_Audience_LogApology".Translate(faction.Name), 1);
            }
            else
            {
                TheirView.favour -= 1f;
                Say(false, FactionVoice.Line(faction, VoiceLine.Refuse, transcript.Count) + "  (" + "Dominion_Audience_NotEnough".Translate() + ")", -1);
                Comp.Player.Log(faction, "Dominion_Audience_LogApologyRejected".Translate(faction.Name), -1);
            }
            Spend();
        }

        private void PayAmends()
        {
            used.Add("amends");
            int cost = AmendsCost();
            int paid = EconomyEffects.Transfer(Faction.OfPlayer, faction, cost);
            if (paid <= 0) return;
            Say(true, "Dominion_Audience_Said_Amends".Translate(paid), 0);
            float eased = Mathf.Min(TheirView.grievance, 25f * paid / (float)cost);
            TheirView.grievance -= eased;
            TheirView.favour += 4f;
            Say(false, FactionVoice.Line(faction, VoiceLine.Thanks, transcript.Count) + "  (" + "Dominion_Audience_Forgiven".Translate(eased.ToString("F0")) + ")", 1);
            Comp.Player.Log(faction, "Dominion_Audience_LogAmends".Translate(paid, faction.Name), 1);
            Spend();
        }

        private void RumourChoices(List<Choice> c)
        {
            c.Add(Make("Dominion_Talk_AskRivals".Translate(), DiplomacyTalk.Desc(TalkTopic.AskRivals), TalkReason(TalkTopic.AskRivals), () => Topic(TalkTopic.AskRivals)));
            c.Add(Make("Dominion_Talk_ShareRumour".Translate() + " ▸", DiplomacyTalk.Desc(TalkTopic.ShareRumour), TalkReason(TalkTopic.ShareRumour),
                () => Find.WindowStack.Add(new FloatMenu(DiplomacyTalk.RumourMenu(faction, about => Topic(TalkTopic.ShareRumour, about)), "Dominion_Talk_RumourTitle".Translate()))));
            c.Add(Make("Dominion_Talk_Warn".Translate(), DiplomacyTalk.Desc(TalkTopic.Warn), TalkReason(TalkTopic.Warn), () => Topic(TalkTopic.Warn)));
            c.Add(Make("Dominion_Audience_Back".Translate(), null, null, () => node = Node.Root));
        }

        private void ThreatChoices(List<Choice> c)
        {
            float ratio = DiplomacyTalk.StrengthRatio(faction);
            c.Add(Make("Dominion_Audience_Intimidate".Translate(), "Dominion_Audience_Intimidate_Desc".Translate(faction.Name, ratio.ToStringPercent()),
                UsedReason("threat"), Intimidate));
            DiplomacyActionDef tribute = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_DemandTribute");
            if (tribute != null && tribute.Worker.Visible(faction))
                c.Add(Make("Dominion_Audience_Tribute".Translate(), tribute.description, UsedReason("threat") ?? DiplomacyActions.CannotUseReason(tribute, faction), () =>
                {
                    used.Add("threat");
                    Say(true, "Dominion_Audience_Said_Tribute".Translate(), 0);
                    Find.WindowStack.Add(new Dialog_EnvoyLetter(tribute, faction));
                    Spend();
                }));
            c.Add(Make("Dominion_Audience_Back".Translate(), null, null, () => node = Node.Root));
        }

        /// <summary>
        /// A reminder of your strength. The weak and the timid grow careful and drop their grudges; the proud and the
        /// aggressive take it as a provocation.
        /// </summary>
        private void Intimidate()
        {
            used.Add("threat");
            Say(true, "Dominion_Audience_Said_Intimidate".Translate(), 0);
            AIPersonality p = Temper;
            float ratio = DiplomacyTalk.StrengthRatio(faction);
            float chance = Mathf.Clamp(0.25f + (ratio - 1f) * 0.4f - (p.aggression - 0.5f) * 0.6f - (p.honour - 0.5f) * 0.2f, 0.03f, 0.9f);
            if (Rand.Chance(chance))
            {
                TheirView.grievance = Mathf.Max(0f, TheirView.grievance - 8f);
                TheirView.favour += 1f;
                Say(false, FactionVoice.Line(faction, VoiceLine.Apology, transcript.Count) + "  (" + "Dominion_Audience_Cowed".Translate(faction.Name) + ")", 1);
                Comp.Player.Log(faction, "Dominion_Audience_LogCowed".Translate(faction.Name), 1);
            }
            else
            {
                TheirView.grievance += 8f;
                TheirView.favour -= 5f;
                Say(false, FactionVoice.Line(faction, VoiceLine.Threat, transcript.Count) + "  (" + "Dominion_Audience_Provoked".Translate(faction.Name) + ")", -1);
                Comp.Player.Log(faction, "Dominion_Audience_LogProvoked".Translate(faction.Name), -1);
            }
            Spend();
        }

        private void HelpChoices(List<Choice> c)
        {
            DiplomacyActionDef call = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_CallToArms");
            if (!(call?.Worker is DiplomacyAction_CallToArms)) call = null;
            var wars = new List<War>();
            Comp.Wars.WarsOf(Faction.OfPlayer, wars);
            foreach (War w in wars)
            {
                Faction enemy = w.Other(Faction.OfPlayer);
                if (enemy == null || enemy == faction) continue;
                string why = call == null ? "Dominion_Action_Invalid".Translate().ToString() : UsedReason("help") ?? DiplomacyActions.CannotUseReason(call, faction);
                if (why == null && faction.HostileTo(enemy)) why = "Dominion_Audience_AlreadyFighting".Translate(faction.Name, enemy.Name);
                float chance = DiplomacyAction_CallToArms.Acceptance(faction, enemy);
                c.Add(Make("Dominion_Audience_HelpAgainst".Translate(enemy.Name, chance.ToStringPercent()), call?.description, why, () =>
                {
                    used.Add("help");
                    Say(true, "Dominion_Audience_Said_Help".Translate(enemy.Name), 0);
                    List<DiplomacyLogEntry> log = Comp.Player.diplomacyLog;
                    DiplomacyLogEntry before = log.Count > 0 ? log[log.Count - 1] : null;
                    ((DiplomacyAction_CallToArms)call.Worker).Ask(faction, enemy);
                    // Their answer went into the record; read it back in their voice. (The log is capped, so compare entries, not counts.)
                    DiplomacyLogEntry e = log.Count > 0 && log[log.Count - 1] != before ? log[log.Count - 1] : null;
                    if (e != null) Say(false, FactionVoice.Answer(faction, e.outcome, false, transcript.Count) + "  (" + e.text + ")", e.outcome);
                    Spend();
                }));
            }
            if (c.Count == 0) c.Add(Make("Dominion_Audience_NoWar".Translate(), null, "Dominion_Audience_NoWar".Translate(), null));
            c.Add(Make("Dominion_Audience_Back".Translate(), null, null, () => node = Node.Root));
        }

        public override void PostClose()
        {
            base.PostClose();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }
    }
}
