using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// The player's orders in a war: a stance (how hard to push, and what it costs), hiring mercenaries, and the
    /// peace terms drafted at the peace table.
    /// </summary>
    public static class WarOrders
    {
        public const float StanceCost = 10f;
        public static readonly int[] MercenaryOffers = { 500, 1000, 2000 };
        public const int MercenaryCooldownDays = 5;

        /// <summary>Score gains for <paramref name="winner"/> are scaled by the player's stance in the war.</summary>
        public static float StanceFactor(War w, Faction winner)
        {
            if (!w.InvolvesPlayer) return 1f;
            bool playerGains = winner.IsPlayer;
            switch (w.stance)
            {
                case WarStance.Aggressive: return playerGains ? 1.3f : 1.15f;
                case WarStance.Defensive: return playerGains ? 0.85f : 0.7f;
                case WarStance.RaidSupply: return playerGains ? 0.9f : 1f;
                default: return 1f;
            }
        }

        public static string StanceLabel(WarStance s) => ("Dominion_Stance_" + s).Translate();

        public static string StanceDesc(WarStance s) => ("Dominion_Stance_" + s + "_Desc").Translate();

        public static string CannotChangeStanceReason(War w, WarStance s)
        {
            if (w.stance == s) return "Dominion_Stance_Current".Translate();
            if (WorldComponent_Dominion.Instance.Player.influence < StanceCost) return "Dominion_Action_NeedsInfluence".Translate(StanceCost.ToString("F0"));
            return null;
        }

        public static void SetStance(War w, WarStance s)
        {
            if (CannotChangeStanceReason(w, s) != null) return;
            WorldComponent_Dominion.Instance.Player.AddInfluence(-StanceCost);
            w.stance = s;
            string text = "Dominion_Stance_Changed".Translate(StanceLabel(s));
            w.Chronicle(text);
            Messages.Message(text, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static string CannotHireReason(War w, int silver)
        {
            int left = w.lastMercenaryTick + MercenaryCooldownDays * GenDate.TicksPerDay - Find.TickManager.TicksGame;
            if (left > 0) return "Dominion_Action_Cooldown".Translate(left.ToStringTicksToPeriod(allowSeconds: false));
            if (DiplomacyHelpers.AvailableSilver() < silver) return "Dominion_Action_NeedsSilver".Translate(silver);
            return null;
        }

        /// <summary>Mercenaries raid the enemy for silver: war score now, and their army and nerve worn down.</summary>
        public static void HireMercenaries(War w, int silver)
        {
            if (CannotHireReason(w, silver) != null) return;
            Faction enemy = w.Other(Faction.OfPlayer);
            int paid = DiplomacyHelpers.TakeSilver(silver);
            if (paid <= 0) return;
            w.lastMercenaryTick = Find.TickManager.TicksGame;
            float score = paid / 100f * Rand.Range(0.7f, 1.3f);
            WarUtility.AddScore(Faction.OfPlayer, enemy, score);
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            if (DominionScheduler.IsSimulated(enemy))
            {
                FactionState s = comp.GetState(enemy);
                s.militaryStrength = Mathf.Max(0f, s.militaryStrength - paid / 40f);
                s.warWeariness = Mathf.Min(100f, s.warWeariness + paid / 200f);
            }
            string text = "Dominion_Mercenaries_Hired".Translate(paid, enemy.Name, score.ToString("F0"));
            w.Chronicle(text);
            comp.Player.Log(enemy, text, 1);
            Messages.Message(text, MessageTypeDefOf.PositiveEvent, historical: true);
        }
    }

    /// <summary>
    /// Terms drafted at the peace table: occupied settlements kept or given back, silver either way, and prisoners
    /// released. Applied in place of the default terms (see <see cref="PeaceTerms"/>).
    /// </summary>
    public class PeaceDeal
    {
        public readonly War war;
        public readonly HashSet<Settlement> kept = new HashSet<Settlement>();
        public readonly HashSet<Settlement> demanded = new HashSet<Settlement>();
        public int silverOffered;
        public int silverDemanded;
        public bool releasePrisoners;

        public static readonly int[] SilverOptions = { 0, 250, 500, 1000, 2000 };

        public PeaceDeal(War war)
        {
            this.war = war;
            // Start where the war stands: keep what you hold, ask back what is yours.
            foreach (Settlement s in PeaceTerms.OccupiedBetween(war))
            {
                if (s.Faction == Faction.OfPlayer) kept.Add(s);
                else demanded.Add(s);
            }
        }

        public Faction Enemy => war.Other(Faction.OfPlayer);

        public List<Settlement> Occupied() => PeaceTerms.OccupiedBetween(war);

        public static List<Pawn> Prisoners(Faction f)
        {
            var list = new List<Pawn>();
            foreach (Map map in Find.Maps)
                if (map.IsPlayerHome)
                    foreach (Pawn p in map.mapPawns.PrisonersOfColonySpawned)
                        if (p.Faction == f) list.Add(p);
            return list;
        }

        private float GreedFactor => 1f + WorldComponent_Dominion.Instance.GetState(Enemy).personality.greed * 0.6f;

        /// <summary>The chance the enemy accepts these terms.</summary>
        public float Chance(StringBuilder explain = null)
        {
            var worker = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace")?.Worker as DiplomacyAction_ProposePeace;
            float baseChance = worker?.AcceptanceChance(Enemy) ?? 0.3f;
            if (explain != null && worker != null) explain.Append(worker.ChanceBreakdown(Enemy));
            float keep = 0f, give = 0f;
            foreach (Settlement s in Occupied())
            {
                if (s.Faction == Faction.OfPlayer) keep += kept.Contains(s) ? -0.1f : 0.04f;
                else give += demanded.Contains(s) ? -0.06f : 0.04f;
            }
            float offered = Mathf.Min(0.3f, silverOffered / 2500f * GreedFactor);
            float asked = -silverDemanded / 1500f;
            float prisoners = releasePrisoners ? Mathf.Min(0.15f, Prisoners(Enemy).Count * 0.03f) : 0f;
            if (explain != null)
            {
                DominionPaper.Term(explain, keep, "Dominion_Odds_KeepSettlements".Translate());
                DominionPaper.Term(explain, give, "Dominion_Odds_TheirSettlements".Translate());
                DominionPaper.Term(explain, offered, "Dominion_Odds_Silver".Translate(silverOffered));
                DominionPaper.Term(explain, asked, "Dominion_Odds_Reparations".Translate(silverDemanded));
                DominionPaper.Term(explain, prisoners, "Dominion_Odds_Prisoners".Translate());
            }
            return Mathf.Clamp(baseChance + keep + give + offered + asked + prisoners, 0.02f, 0.95f);
        }

        public string Breakdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Dominion_Odds_Title".Translate(Enemy.Name));
            float c = Chance(sb);
            sb.AppendLine();
            sb.Append("Dominion_Odds_Total".Translate(c.ToStringPercent()));
            return sb.ToString();
        }

        /// <summary>The terms as lines of the treaty.</summary>
        public List<string> Describe()
        {
            var lines = new List<string>();
            Faction enemy = Enemy;
            foreach (Settlement s in Occupied())
            {
                Faction orig = WorldComponent_Dominion.Instance.Cache.FactionById(WorldComponent_Dominion.Instance.Wars.OriginalOwnerOf(s));
                bool stays = s.Faction == Faction.OfPlayer ? kept.Contains(s) : !demanded.Contains(s);
                lines.Add(stays ? "Dominion_Terms_KeepsSettlement".Translate(s.Faction.Name, s.Label)
                    : "Dominion_Terms_ReturnsSettlement".Translate(s.Faction.Name, s.Label, orig?.Name ?? "?"));
            }
            if (silverOffered > 0) lines.Add("Dominion_Deal_YouPay".Translate(silverOffered, enemy.Name));
            if (silverDemanded > 0) lines.Add("Dominion_Deal_TheyPay".Translate(enemy.Name, silverDemanded));
            if (releasePrisoners) lines.Add("Dominion_Deal_Prisoners".Translate(Prisoners(enemy).Count, enemy.Name));
            if (lines.Count == 0) lines.Add("Dominion_Terms_WhitePeace".Translate());
            return lines;
        }

        public string CannotOfferReason()
        {
            if (silverOffered > 0 && DiplomacyHelpers.AvailableSilver() < silverOffered) return "Dominion_Action_NeedsSilver".Translate(silverOffered);
            return null;
        }

        /// <summary>Carries the terms out when the peace is made. Returns the letter text.</summary>
        public string Apply(War w, out string outcome)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Faction enemy = Enemy;
            var lines = new List<string> { "Dominion_Deal_Negotiated".Translate(enemy.Name) };
            foreach (Settlement s in Occupied())
            {
                Faction holder = s.Faction;
                Faction orig = comp.Cache.FactionById(comp.Wars.OriginalOwnerOf(s));
                bool stays = holder == Faction.OfPlayer ? kept.Contains(s) : !demanded.Contains(s);
                if (stays)
                {
                    lines.Add("Dominion_Terms_KeepsSettlement".Translate(holder.Name, s.Label));
                    WarUtility.Cede(s, holder);
                }
                else if (orig != null && !orig.defeated)
                {
                    lines.Add("Dominion_Terms_ReturnsSettlement".Translate(holder.Name, s.Label, orig.Name));
                    WarUtility.ReturnSettlement(s, orig);
                }
            }
            if (silverOffered > 0)
            {
                int paid = EconomyEffects.Transfer(Faction.OfPlayer, enemy, silverOffered);
                if (paid > 0) lines.Add("Dominion_Terms_PlayerPaid".Translate(paid, enemy.Name));
            }
            if (silverDemanded > 0)
            {
                int paid = EconomyEffects.Transfer(enemy, Faction.OfPlayer, silverDemanded);
                if (paid > 0) lines.Add("Dominion_Terms_PaidToPlayer".Translate(enemy.Name, paid));
            }
            if (releasePrisoners)
            {
                List<Pawn> freed = Prisoners(enemy);
                foreach (Pawn p in freed) GenGuest.PrisonerRelease(p);
                if (freed.Count > 0)
                {
                    lines.Add("Dominion_Deal_Released".Translate(freed.Count, enemy.Name));
                    comp.GetRelation(enemy, Faction.OfPlayer).favour += freed.Count * 3f;
                }
            }
            outcome = "Dominion_Deal_Outcome".Translate();
            return string.Join("\n", lines);
        }
    }

    /// <summary>Daily effects of the player's war orders, and each war's chronicle.</summary>
    public class WarOrdersSystem : SimSystem
    {
        public override int Order => 97;

        public override void DailyGlobal()
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            foreach (War w in comp.Wars.wars)
            {
                if (!w.InvolvesPlayer || w.stance != WarStance.RaidSupply) continue;
                Faction enemy = w.Other(Faction.OfPlayer);
                if (!DominionScheduler.IsSimulated(enemy)) continue;
                // Raiding supply lines: their trade and nerve suffer, a little every day.
                EconomyEffects.Shock(enemy, -0.008f, destroyGoods: false, days: 3);
                FactionState s = comp.GetState(enemy);
                s.warWeariness = Mathf.Min(100f, s.warWeariness + 0.6f);
            }
        }

        public override void OnEvent(DominionEvent ev)
        {
            if (ev.primary == null || ev.secondary == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            War w = comp?.Wars.Find(ev.primary, ev.secondary);
            if (w == null) return;
            w.Chronicle(ev.Headline, ev.id);
        }
    }
}
