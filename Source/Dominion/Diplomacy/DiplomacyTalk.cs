using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>The quick things you can say to a faction over the wire, one click each.</summary>
    public enum TalkTopic { Praise, Complain, AskRivals, AskWants, ShareRumour, Warn }

    /// <summary>What a faction answered: a line in its own voice, what you learned, and how it went (1 well, -1 badly, 0 neither).</summary>
    public class TalkResult
    {
        public string reply;
        public string fact;
        public int outcome;
    }

    /// <summary>
    /// Talking with a faction: praise, airing a complaint, asking about their rivals or what they want, passing on a
    /// rumour about a third faction, warning them of a threat. Used by the telegraph keys and by audiences. Each topic
    /// has a cooldown per faction and a small influence cost; replies come in the faction's voice
    /// (<see cref="FactionVoice"/>), and the effects are real: favour, goodwill, grievances, intel, and other
    /// factions' standing.
    /// </summary>
    public static class DiplomacyTalk
    {
        public static readonly TalkTopic[] Topics = (TalkTopic[])System.Enum.GetValues(typeof(TalkTopic));

        public static int CooldownDays(TalkTopic t)
        {
            switch (t)
            {
                case TalkTopic.Praise: return 3;
                case TalkTopic.AskWants: return 3;
                case TalkTopic.ShareRumour: return 4;
                case TalkTopic.Warn: return 6;
                default: return 5;
            }
        }

        public static float InfluenceCost(TalkTopic t)
        {
            switch (t)
            {
                case TalkTopic.AskRivals:
                case TalkTopic.ShareRumour: return 3f;
                case TalkTopic.Complain:
                case TalkTopic.Warn: return 2f;
                default: return 0f;
            }
        }

        public static string Label(TalkTopic t) => ("Dominion_Talk_" + t).Translate();

        public static string Desc(TalkTopic t) => ("Dominion_Talk_" + t + "_Desc").Translate();

        private static string CooldownKey(TalkTopic t) => "Talk_" + t;

        private static WorldComponent_Dominion Comp => WorldComponent_Dominion.Instance;

        private static RelationState TheirView(Faction f) => Comp.GetRelation(f, Faction.OfPlayer);

        private static RelationState OurView(Faction f) => Comp.GetRelation(Faction.OfPlayer, f);

        /// <summary>A faction whose ideology shuns outsiders will not talk unless it already likes you.</summary>
        public static bool Shuns(Faction f) => GovernmentUtility.HasMeme(f, "VFEA_Isolationist") && TheirView(f).opinion < 30f;

        /// <summary>Why you cannot raise this topic with them now, or null.</summary>
        public static string CannotTalkReason(Faction f, TalkTopic t)
        {
            WorldComponent_Dominion comp = Comp;
            if (comp == null || f == null || f.IsPlayer || f.defeated || !comp.TryGetState(f, out _)) return "Dominion_Action_Invalid".Translate();
            if (LeadershipUtility.BestDesk() == null && !LeadershipUtility.HasComms()) return "Dominion_Action_NeedsComms".Translate();
            int until = comp.Player.CooldownUntil(CooldownKey(t), f.loadID);
            int now = Find.TickManager.TicksGame;
            if (until > now) return "Dominion_Action_Cooldown".Translate((until - now).ToStringTicksToPeriod(allowSeconds: false));
            if (comp.Player.influence < InfluenceCost(t)) return "Dominion_Action_NeedsInfluence".Translate(InfluenceCost(t).ToString("F0"));
            if (t != TalkTopic.Warn && Shuns(f)) return "Dominion_Talk_Shuns".Translate(f.Name);
            bool hostile = f.HostileTo(Faction.OfPlayer);
            switch (t)
            {
                case TalkTopic.Praise:
                case TalkTopic.AskWants:
                case TalkTopic.ShareRumour:
                    if (hostile) return "Dominion_Action_NotWhileHostile".Translate();
                    break;
                case TalkTopic.Complain:
                    if (OurView(f).grievance < 5f) return "Dominion_Talk_NoGrievance".Translate(f.Name);
                    break;
                case TalkTopic.AskRivals:
                    if (hostile) return "Dominion_Action_NotWhileHostile".Translate();
                    if (Rivals(f, out _, out _) == 0) return "Dominion_Talk_NoOneElse".Translate();
                    break;
            }
            return null;
        }

        // ------------------------------------------------------------------ odds

        /// <summary>The chance the topic goes well, or null if it simply happens.</summary>
        public static float? Chance(Faction f, TalkTopic t, Faction about = null, StringBuilder explain = null)
        {
            if (Comp == null || f == null) return null;
            AIPersonality p = Comp.GetState(f).personality;
            float op = TheirView(f).opinion;
            Voice v = FactionVoice.Of(f);
            float c;
            switch (t)
            {
                case TalkTopic.Praise:
                    c = Term(explain, 0.55f, "Dominion_Odds_Base");
                    c += Term(explain, (p.honour - 0.5f) * 0.4f, "Dominion_Talk_Odds_Honour");
                    c += Term(explain, -(p.paranoia - 0.5f) * 0.4f, "Dominion_Talk_Odds_Paranoia");
                    c += Term(explain, op / 400f, "Dominion_Talk_Odds_Opinion");
                    c += Term(explain, v == Voice.Gentle || v == Voice.Courtly || v == Voice.Festive || v == Voice.Divine ? 0.1f
                        : v == Voice.Martial || v == Voice.Aloof || v == Voice.Stern ? -0.15f : 0f, "Dominion_Talk_Odds_Manner");
                    break;
                case TalkTopic.Complain:
                    c = Term(explain, 0.35f, "Dominion_Odds_Base");
                    c += Term(explain, (p.honour - 0.5f) * 0.6f, "Dominion_Talk_Odds_Honour");
                    c += Term(explain, -(p.aggression - 0.5f) * 0.5f, "Dominion_Talk_Odds_Aggression");
                    c += Term(explain, op / 300f, "Dominion_Talk_Odds_Opinion");
                    c += Term(explain, Mathf.Clamp((StrengthRatio(f) - 1f) * 0.15f, -0.15f, 0.2f), "Dominion_Talk_Odds_Strength");
                    break;
                case TalkTopic.ShareRumour:
                    if (about == null) return null;
                    RelationState toward = Comp.GetRelation(f, about);
                    c = Term(explain, 0.45f, "Dominion_Odds_Base");
                    c += Term(explain, toward.opinion < 0f ? 0.2f : -0.1f, "Dominion_Talk_Odds_Distrust");
                    c += Term(explain, IntelUtility.IntelOn(about) / 300f, "Dominion_Talk_Odds_Intel");
                    c += Term(explain, -(p.paranoia - 0.5f) * 0.3f, "Dominion_Talk_Odds_Paranoia");
                    c += Term(explain, op / 300f, "Dominion_Talk_Odds_Opinion");
                    break;
                default:
                    return null;
            }
            c += Term(explain, LeadershipUtility.AcceptanceBonus(), "Dominion_Odds_Leader");
            return Mathf.Clamp(c, 0.05f, 0.95f);
        }

        private static float Term(StringBuilder explain, float value, string key)
        {
            if (explain != null && Mathf.Abs(value) >= 0.005f) DominionPaper.Term(explain, value, key.Translate());
            return value;
        }

        /// <summary>Your strength against theirs: above 1 you are stronger.</summary>
        public static float StrengthRatio(Faction f)
        {
            float theirs = Comp.TryGetState(f, out FactionState s) ? Mathf.Max(1f, s.militaryStrength) : 1f;
            return DiplomacyHelpers.PlayerStrength() / theirs;
        }

        // ------------------------------------------------------------------ talking

        /// <summary>
        /// Raises the topic: pays its influence, starts its cooldown, applies the effect and logs it.
        /// <paramref name="about"/> is the faction a rumour is about. Returns their answer.
        /// </summary>
        public static TalkResult Talk(Faction f, TalkTopic t, Faction about = null)
        {
            WorldComponent_Dominion comp = Comp;
            PlayerState player = comp.Player;
            player.AddInfluence(-InfluenceCost(t));
            int now = Find.TickManager.TicksGame;
            player.SetCooldown(CooldownKey(t), f.loadID, now + CooldownDays(t) * GenDate.TicksPerDay);
            var r = new TalkResult();
            int seed = now / 2500;
            switch (t)
            {
                case TalkTopic.Praise: Praise(f, r); break;
                case TalkTopic.Complain: Complain(f, r); break;
                case TalkTopic.AskRivals: AskRivals(f, r); break;
                case TalkTopic.AskWants: AskWants(f, r); break;
                case TalkTopic.ShareRumour: ShareRumour(f, about, r); break;
                case TalkTopic.Warn: Warn(f, r); break;
            }
            r.reply = FactionVoice.Line(f, ReplyLine(t, r.outcome), seed);
            string log = "Dominion_Talk_Log".Translate(Label(t), r.reply) + (r.fact.NullOrEmpty() ? "" : " " + r.fact);
            player.Log(f, log, r.outcome);
            return r;
        }

        private static VoiceLine ReplyLine(TalkTopic t, int outcome)
        {
            switch (t)
            {
                case TalkTopic.Praise: return outcome > 0 ? VoiceLine.Praised : VoiceLine.Rebuff;
                case TalkTopic.Complain: return outcome > 0 ? VoiceLine.Apology : VoiceLine.Insulted;
                case TalkTopic.AskRivals:
                case TalkTopic.AskWants: return outcome >= 0 ? VoiceLine.Confide : VoiceLine.Rebuff;
                case TalkTopic.ShareRumour: return outcome > 0 ? VoiceLine.Confide : VoiceLine.Rebuff;
                case TalkTopic.Warn: return outcome > 0 ? VoiceLine.Warned : VoiceLine.Rebuff;
            }
            return VoiceLine.Greeting;
        }

        private static void Praise(Faction f, TalkResult r)
        {
            if (Rand.Chance(Chance(f, TalkTopic.Praise).Value))
            {
                TheirView(f).favour += 4f;
                f.TryAffectGoodwillWith(Faction.OfPlayer, 1, canSendMessage: false, canSendHostilityLetter: false);
                r.outcome = 1;
                r.fact = "Dominion_Talk_Praise_Good".Translate(f.Name);
            }
            else
            {
                TheirView(f).favour -= 1f;
                r.outcome = -1;
                r.fact = "Dominion_Talk_Praise_Bad".Translate(f.Name);
            }
        }

        private static void Complain(Faction f, TalkResult r)
        {
            RelationState ours = OurView(f);
            if (Rand.Chance(Chance(f, TalkTopic.Complain).Value))
            {
                float eased = Mathf.Min(ours.grievance, 15f);
                ours.grievance -= eased;
                TheirView(f).favour += 2f;
                f.TryAffectGoodwillWith(Faction.OfPlayer, 1, canSendMessage: false, canSendHostilityLetter: false);
                r.outcome = 1;
                r.fact = "Dominion_Talk_Complain_Good".Translate(f.Name, eased.ToString("F0"));
            }
            else
            {
                TheirView(f).favour -= 3f;
                TheirView(f).grievance += 3f;
                r.outcome = -1;
                r.fact = "Dominion_Talk_Complain_Bad".Translate(f.Name);
            }
        }

        /// <summary>The faction they think least and most of, besides you. Returns how many others they know.</summary>
        public static int Rivals(Faction f, out Faction worst, out Faction best)
        {
            worst = best = null;
            float lo = float.MaxValue, hi = float.MinValue;
            int n = 0;
            foreach (Faction o in Comp.Cache.Simulated)
            {
                if (o == f || o.IsPlayer || o.defeated) continue;
                float op = Comp.GetRelation(f, o).opinion;
                n++;
                if (op < lo)
                {
                    lo = op;
                    worst = o;
                }
                if (op > hi)
                {
                    hi = op;
                    best = o;
                }
            }
            return n;
        }

        private static void AskRivals(Faction f, TalkResult r)
        {
            AIPersonality p = Comp.GetState(f).personality;
            if (p.paranoia > 0.75f && TheirView(f).opinion < 20f)
            {
                r.outcome = -1;
                r.fact = "Dominion_Talk_Rivals_Refused".Translate(f.Name);
                return;
            }
            Rivals(f, out Faction worst, out Faction best);
            var sb = new StringBuilder();
            if (worst != null) sb.Append("Dominion_Talk_Rivals_Worst".Translate(worst.Name, Comp.GetRelation(f, worst).opinion.ToString("F0")));
            if (best != null && best != worst) sb.Append(" " + "Dominion_Talk_Rivals_Best".Translate(best.Name, Comp.GetRelation(f, best).opinion.ToString("F0")));
            r.fact = sb.ToString();
            if (Comp.TryGetState(f, out FactionState s)) s.playerIntel = Mathf.Min(100f, s.playerIntel + 2f);
            r.outcome = 0;
        }

        /// <summary>What a faction wants from you or from the world, worked out from its situation and temper.</summary>
        public static List<string> Wants(Faction f)
        {
            var wants = new List<string>();
            Faction player = Faction.OfPlayer;
            AIPersonality p = Comp.GetState(f).personality;
            var wars = new List<War>();
            Comp.Wars.WarsOf(f, wars);
            foreach (War w in wars)
            {
                Faction enemy = w.Other(f);
                if (enemy != null && !enemy.IsPlayer) wants.Add("Dominion_Talk_Want_Help".Translate(enemy.Name));
            }
            if (TheirView(f).grievance > 20f) wants.Add("Dominion_Talk_Want_Amends".Translate());
            if (OurView(f).embargo) wants.Add("Dominion_Talk_Want_LiftEmbargo".Translate());
            if (OurView(f).border != BorderStatus.Open) wants.Add("Dominion_Talk_Want_OpenBorders".Translate());
            bool partners = Comp.Treaties.All.Any(t => t.Between(player, f));
            if (!partners && TheirView(f).opinion > 10f)
                wants.Add((p.aggression < 0.45f ? "Dominion_Talk_Want_NonAggression" : "Dominion_Talk_Want_Trade").Translate());
            if (Comp.TryGetState(f, out FactionState s) && s.economyShock < -0.15f) wants.Add("Dominion_Talk_Want_Aid".Translate());
            if (p.greed > 0.65f) wants.Add("Dominion_Talk_Want_Silver".Translate());
            return wants;
        }

        private static void AskWants(Faction f, TalkResult r)
        {
            List<string> wants = Wants(f);
            r.fact = wants.Count == 0 ? "Dominion_Talk_Want_Nothing".Translate(f.Name).ToString()
                : "Dominion_Talk_Want_List".Translate(f.Name, string.Join("; ", wants)).ToString();
            TheirView(f).favour += 1f;
            r.outcome = 0;
        }

        private static void ShareRumour(Faction f, Faction about, TalkResult r)
        {
            if (about == null)
            {
                r.outcome = -1;
                return;
            }
            if (Rand.Chance(Chance(f, TalkTopic.ShareRumour, about).Value))
            {
                Comp.GetRelation(f, about).favour -= 5f;
                TheirView(f).favour += 3f;
                r.outcome = 1;
                r.fact = "Dominion_Talk_Rumour_Good".Translate(f.Name, about.Name);
            }
            else
            {
                TheirView(f).favour -= 3f;
                r.outcome = -1;
                r.fact = "Dominion_Talk_Rumour_Bad".Translate(f.Name, about.Name);
            }
        }

        /// <summary>A real danger to the faction you could warn it about, or null: a warband on its way, an open war, a rival spoiling for one.</summary>
        public static string Threat(Faction f)
        {
            foreach (Warband w in WarbandUtility.All())
                if (w.VisibleToPlayer && w.target == f && w.Faction != null) return "Dominion_Talk_Threat_Warband".Translate(w.Faction.Name);
            var wars = new List<War>();
            Comp.Wars.WarsOf(f, wars);
            foreach (War w in wars)
            {
                Faction enemy = w.Other(f);
                if (enemy != null && !enemy.IsPlayer) return "Dominion_Talk_Threat_War".Translate(enemy.Name);
            }
            foreach (Faction o in Comp.Cache.Simulated)
            {
                if (o == f || o.IsPlayer || !Comp.TryGetState(o, out FactionState s)) continue;
                if (Comp.GetRelation(o, f).opinion < -50f && s.personality.aggression > 0.6f && IntelUtility.Knows(o, IntelUtility.RestrictedIntel))
                    return "Dominion_Talk_Threat_Rival".Translate(o.Name);
            }
            return null;
        }

        private static void Warn(Faction f, TalkResult r)
        {
            string threat = Threat(f);
            if (threat != null)
            {
                bool hostile = f.HostileTo(Faction.OfPlayer);
                TheirView(f).favour += hostile ? 6f : 5f;
                if (hostile) TheirView(f).grievance = Mathf.Max(0f, TheirView(f).grievance - 5f);
                else f.TryAffectGoodwillWith(Faction.OfPlayer, 2, canSendMessage: false, canSendHostilityLetter: false);
                r.outcome = 1;
                r.fact = "Dominion_Talk_Warn_Good".Translate(f.Name, threat);
            }
            else
            {
                TheirView(f).favour -= 2f;
                r.outcome = -1;
                r.fact = "Dominion_Talk_Warn_Bad".Translate(f.Name);
            }
        }

        /// <summary>The menu of factions a rumour could be about, with the odds they believe it.</summary>
        public static List<FloatMenuOption> RumourMenu(Faction f, System.Action<Faction> pick)
        {
            var options = new List<FloatMenuOption>();
            foreach (Faction o in Comp.Cache.Simulated)
            {
                if (o == f || o.IsPlayer || o.defeated) continue;
                Faction captured = o;
                float c = Chance(f, TalkTopic.ShareRumour, o) ?? 0f;
                options.Add(new FloatMenuOption("Dominion_Talk_RumourAbout".Translate(o.Name, c.ToStringPercent()), () => pick(captured), o.def.FactionIcon, o.Color));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
            return options;
        }
    }
}
