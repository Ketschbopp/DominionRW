using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    public enum EnvoyMission { ImproveRelations, NegotiateTreaty, PeaceTalks, GatherIntel, ExchangeHostages, AttendSummit, AttendCouncil }

    public class WorldObjectCompProperties_Diplomacy : WorldObjectCompProperties
    {
        public WorldObjectCompProperties_Diplomacy()
        {
            compClass = typeof(WorldObjectComp_Diplomacy);
        }
    }

    /// <summary>
    /// On every settlement: a player caravan carrying the head of state or a diplomat can travel there on a
    /// diplomatic mission. Doing it in person beats the comms: higher acceptance, and some things only work face to face.
    /// </summary>
    public class WorldObjectComp_Diplomacy : WorldObjectComp
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            if (!(parent is Settlement settlement) || settlement.Faction == null || settlement.Faction.IsPlayer) yield break;
            if (!caravan.IsPlayerControlled || DiplomatUtility.BestEnvoy(caravan) == null) yield break;
            Faction f = settlement.Faction;
            bool hostile = f.HostileTo(Faction.OfPlayer);

            foreach (EnvoyMission mission in System.Enum.GetValues(typeof(EnvoyMission)))
            {
                if (mission == EnvoyMission.NegotiateTreaty)
                {
                    if (hostile) continue;
                    foreach (TreatyDef treaty in DefDatabase<TreatyDef>.AllDefsListForReading)
                    {
                        if (WorldComponent_Dominion.Instance.Treaties.CannotSignReason(treaty, Faction.OfPlayer, f) != null) continue;
                        TreatyDef t = treaty;
                        foreach (FloatMenuOption o in Option(caravan, settlement, mission, t))
                            yield return o;
                    }
                    continue;
                }
                if (!EnvoyMissions.Applies(mission, settlement, caravan)) continue;
                foreach (FloatMenuOption o in Option(caravan, settlement, mission, null))
                    yield return o;
            }
        }

        private static IEnumerable<FloatMenuOption> Option(Caravan caravan, Settlement settlement, EnvoyMission mission, TreatyDef treaty)
        {
            string label = EnvoyMissions.Label(mission, settlement.Faction, treaty, DiplomatUtility.BestEnvoy(caravan));
            return CaravanArrivalActionUtility.GetFloatMenuOptions(
                () => EnvoyMissions.CanDo(mission, settlement, treaty),
                () => new CaravanArrivalAction_DiplomaticMission(settlement, mission, treaty),
                label, caravan, settlement.Tile, settlement, null);
        }
    }

    public class CaravanArrivalAction_DiplomaticMission : CaravanArrivalAction
    {
        private Settlement settlement;
        private EnvoyMission mission;
        private TreatyDef treaty;

        public CaravanArrivalAction_DiplomaticMission() { }

        public CaravanArrivalAction_DiplomaticMission(Settlement settlement, EnvoyMission mission, TreatyDef treaty)
        {
            this.settlement = settlement;
            this.mission = mission;
            this.treaty = treaty;
        }

        public override string Label => ("Dominion_Mission_" + mission).Translate(settlement?.Faction?.Name ?? "?", treaty?.label ?? "");

        public override string ReportString => "Dominion_Mission_Report".Translate(settlement?.Label ?? "?");

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            if (settlement == null || settlement.Destroyed || settlement.Tile != destinationTile) return false;
            return EnvoyMissions.CanDo(mission, settlement, treaty);
        }

        public override void Arrived(Caravan caravan) => EnvoyMissions.Perform(mission, settlement, treaty, caravan);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Values.Look(ref mission, "mission");
            Scribe_Defs.Look(ref treaty, "treaty");
        }
    }

    public static class EnvoyMissions
    {
        public static float Cost(EnvoyMission m, TreatyDef treaty)
        {
            switch (m)
            {
                case EnvoyMission.ImproveRelations: return 10f;
                case EnvoyMission.NegotiateTreaty: return treaty?.influenceCost ?? 0f;
                case EnvoyMission.PeaceTalks: return 20f;
                case EnvoyMission.GatherIntel: return 10f;
                case EnvoyMission.ExchangeHostages: return 5f;
                default: return 0f;
            }
        }

        public static bool Applies(EnvoyMission m, Settlement s, Caravan caravan)
        {
            Faction f = s.Faction;
            bool hostile = f.HostileTo(Faction.OfPlayer);
            switch (m)
            {
                case EnvoyMission.ImproveRelations:
                case EnvoyMission.GatherIntel:
                    return !hostile;
                case EnvoyMission.PeaceTalks:
                    return hostile && !f.def.permanentEnemy;
                case EnvoyMission.ExchangeHostages:
                    return HostageUtility.AnyHostages(f);
                case EnvoyMission.AttendSummit:
                    return SummitSystem.SummitAbroadAt(s) != null;
                case EnvoyMission.AttendCouncil:
                    return CouncilSystem.CanRegisterAt(s);
                default:
                    return false;
            }
        }

        public static FloatMenuAcceptanceReport CanDo(EnvoyMission m, Settlement s, TreatyDef treaty)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            float cost = Cost(m, treaty);
            if (comp.Player.influence < cost)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Action_NeedsInfluence".Translate(cost.ToString("F0")));
            int until = comp.Player.CooldownUntil("Envoy_" + m, s.Faction.loadID);
            if (until > Find.TickManager.TicksGame)
                return FloatMenuAcceptanceReport.WithFailReason("Dominion_Action_Cooldown".Translate((until - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false)));
            if (m == EnvoyMission.AttendSummit && LeadershipUtility.HeadOfState() == null)
                return FloatMenuAcceptanceReport.WithFailReason(LeadershipUtility.NoHeadOfStateReason());
            return true;
        }

        public static string Label(EnvoyMission m, Faction f, TreatyDef treaty, Pawn envoy)
        {
            string label = ("Dominion_Mission_" + m).Translate(f.Name, treaty?.label ?? "");
            float? chance = Chance(m, f, treaty, envoy);
            float cost = Cost(m, treaty);
            if (cost > 0f) label += " (" + "Dominion_Dip_Cost".Translate(cost.ToString("F0")) + ")";
            if (chance.HasValue) label += " " + "Dominion_Mission_Chance".Translate(chance.Value.ToStringPercent());
            return label;
        }

        public static float? Chance(EnvoyMission m, Faction f, TreatyDef treaty, Pawn envoy)
        {
            float bonus = DiplomatUtility.InPersonBonus(envoy);
            switch (m)
            {
                case EnvoyMission.NegotiateTreaty:
                    return treaty == null ? (float?)null : Mathf.Clamp(DiplomacyHelpers.TreatyAcceptance(treaty, f) + bonus, 0.02f, 0.97f);
                case EnvoyMission.PeaceTalks:
                    float? baseChance = DefDatabase<DiplomacyActionDef>.GetNamedSilentFail("Dominion_ProposePeace")?.Worker.AcceptanceChance(f);
                    return Mathf.Clamp((baseChance ?? 0.2f) + bonus, 0.02f, 0.97f);
                case EnvoyMission.ExchangeHostages:
                    return HostageUtility.ExchangeAcceptance(f, bonus);
                default:
                    return null;
            }
        }

        public static void Perform(EnvoyMission m, Settlement s, TreatyDef treaty, Caravan caravan)
        {
            Faction f = s.Faction;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            Pawn envoy = DiplomatUtility.BestEnvoy(caravan);
            if (envoy == null || f == null || f.IsPlayer)
            {
                Messages.Message("Dominion_Mission_NoEnvoy".Translate(), caravan, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            FloatMenuAcceptanceReport ok = CanDo(m, s, treaty);
            if (!ok.Accepted)
            {
                Messages.Message(ok.FailReason, caravan, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (m == EnvoyMission.AttendSummit)
            {
                SummitSystem.AttendAbroad(s, caravan, envoy);
                return;
            }
            if (m == EnvoyMission.AttendCouncil)
            {
                if (CouncilSystem.CanRegisterAt(s)) CouncilSystem.RegisterPlayer(envoy);
                return;
            }

            comp.Player.AddInfluence(-Cost(m, treaty));
            comp.Player.SetCooldown("Envoy_" + m, f.loadID, Find.TickManager.TicksGame + 10 * GenDate.TicksPerDay);
            float? chance = Chance(m, f, treaty, envoy);
            bool success = !chance.HasValue || Rand.Chance(chance.Value);
            RelationState view = comp.GetRelation(f, Faction.OfPlayer);
            int social = DiplomatUtility.Social(envoy);
            string result;

            switch (m)
            {
                case EnvoyMission.ImproveRelations:
                    view.favour += 10f + social * 0.8f;
                    f.TryAffectGoodwillWith(Faction.OfPlayer, 3 + social / 4, canSendMessage: false, canSendHostilityLetter: false);
                    envoy.skills?.Learn(SkillDefOf.Social, 1000f);
                    result = "Dominion_Mission_ImproveRelations_Done".Translate(envoy.LabelShortCap, f.Name);
                    break;
                case EnvoyMission.NegotiateTreaty:
                    if (success) comp.Treaties.Sign(treaty, Faction.OfPlayer, f, Faction.OfPlayer);
                    else view.favour -= 2f;
                    result = (success ? "Dominion_Treaty_Accepted" : "Dominion_Treaty_Refused").Translate(f.Name, treaty.label);
                    break;
                case EnvoyMission.PeaceTalks:
                    if (success)
                    {
                        DiplomacyUtility.SetRelationKind(Faction.OfPlayer, f, FactionRelationKind.Neutral, Faction.OfPlayer, "Dominion_Reason_Peace".Translate());
                        result = "Dominion_Peace_Accepted".Translate(f.Name);
                    }
                    else if (Rand.Chance(0.35f * comp.GetState(f).personality.aggression))
                    {
                        Capture(envoy, caravan, f);
                        return;
                    }
                    else result = "Dominion_Peace_Refused".Translate(f.Name);
                    break;
                case EnvoyMission.GatherIntel:
                    FactionState fs = comp.GetState(f);
                    fs.playerIntel = Mathf.Min(100f, fs.playerIntel + 10f + social);
                    result = "Dominion_Mission_GatherIntel_Done".Translate(envoy.LabelShortCap, f.Name);
                    break;
                case EnvoyMission.ExchangeHostages:
                    if (success)
                    {
                        HostageUtility.Exchange(f, out int returned, out int released);
                        result = "Dominion_Hostages_Done".Translate(f.Name, returned, released);
                    }
                    else result = "Dominion_Hostages_Refused".Translate(f.Name);
                    break;
                default:
                    return;
            }
            EventBus.Raise(DominionEventDefOf.Dominion_EnvoyMission, Faction.OfPlayer, f, s.Tile.tileId,
                detail: ("Dominion_MissionShort_" + m).Translate());
            Messages.Message(result, caravan, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, historical: true);
        }

        /// <summary>A failed approach to a hostile, aggressive faction: the envoy is taken.</summary>
        private static void Capture(Pawn envoy, Caravan caravan, Faction f)
        {
            try
            {
                caravan.RemovePawn(envoy);
                f.kidnapped.Kidnap(envoy, null);
            }
            catch (System.Exception e)
            {
                DLog.Warning("Could not hand the captured envoy to " + f.Name + ": " + e.Message);
                if (!caravan.PawnsListForReading.Contains(envoy)) caravan.AddPawn(envoy, true);
                return;
            }
            EventBus.Raise(DominionEventDefOf.Dominion_EnvoyCaptured, f, Faction.OfPlayer, detail: envoy.LabelShort);
            Find.LetterStack.ReceiveLetter("Dominion_EnvoyCaptured_Label".Translate(), "Dominion_EnvoyCaptured_Text".Translate(envoy.LabelShortCap, f.Name),
                LetterDefOf.NegativeEvent, caravan, f);
        }
    }
}
