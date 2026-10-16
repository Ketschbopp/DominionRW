using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// How AI factions treat the player: they close borders and impose embargoes when they dislike you, lift
    /// them when relations recover, and now and then offer you a treaty when they like you.
    /// </summary>
    public class PlayerPolicySystem : SimSystem
    {
        public override int Order => 35;

        private const int OfferCooldownDays = 30;

        public override void DailyFaction(Faction f)
        {
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            RelationState view = comp.GetRelation(f, player);
            FactionState s = comp.GetState(f);
            float op = view.opinion;
            bool hostile = f.HostileTo(player);

            // Borders, with hysteresis so they do not flip back and forth.
            BorderStatus border = view.border;
            if (hostile) border = BorderStatus.Closed;
            else if (op < -50f) border = BorderStatus.Closed;
            else if (op < -30f && border == BorderStatus.Open) border = BorderStatus.Restricted;
            else if (op > -10f) border = BorderStatus.Open;
            if (border != view.border)
            {
                view.border = border;
                if (!hostile)
                    EventBus.Raise(DominionEventDefOf.Dominion_BordersChanged, f, player, detail: DiplomacyAction_Borders.BorderLabel(border).ToLower());
            }

            // Embargo
            if (!hostile && !view.embargo && op < -60f && s.personality.greed < 0.7f)
            {
                view.embargo = true;
                EventBus.Raise(DominionEventDefOf.Dominion_EmbargoDeclared, f, player);
            }
            else if (view.embargo && (hostile || op > -25f))
            {
                view.embargo = false;
                if (!hostile) EventBus.Raise(DominionEventDefOf.Dominion_EmbargoLifted, f, player);
            }

            // Offers
            int now = Find.TickManager.TicksGame;
            if (!hostile && op > 35f && now - comp.Player.LastOfferTick(f.loadID) > OfferCooldownDays * GenDate.TicksPerDay
                && Rand.Chance(0.05f))
            {
                TreatyDef offer = ChooseOffer(f, s, view, comp);
                if (offer != null)
                {
                    comp.Player.SetLastOffer(f.loadID, now);
                    // The offer travels in person; it is made when the envoy reaches the colony.
                    Delegation d = DelegationUtility.Dispatch(f, player, DelegationPurpose.OfferToPlayer);
                    if (d != null) d.treaty = offer;
                    else Offer(f, offer, null);
                }
            }
        }

        private static TreatyDef ChooseOffer(Faction f, FactionState s, RelationState view, WorldComponent_Dominion comp)
        {
            Faction player = Faction.OfPlayer;
            var wanted = new List<TreatyDef>();
            bool ally = f.RelationKindWith(player) == FactionRelationKind.Ally;
            if (s.personality.greed > 0.4f) wanted.Add(DominionTreatyDefOf.Dominion_TradeAgreement);
            if (view.distance >= 0f && view.distance < RelationsSystem.NeighbourDistance && s.personality.paranoia > 0.4f)
                wanted.Add(DominionTreatyDefOf.Dominion_NonAggressionPact);
            if (ally) wanted.Add(DominionTreatyDefOf.Dominion_IntelSharing);
            if (ally && view.opinion > 60f) wanted.Add(DominionTreatyDefOf.Dominion_DefensivePact);
            if (!ally && view.opinion > 70f && f.PlayerGoodwill >= 60) wanted.Add(DominionTreatyDefOf.Dominion_Alliance);
            // Close friends ask to join your Federation once its charter is in force.
            if (view.opinion > 70f && FederationUtility.CharterInForce && FederationUtility.IsMajor(f))
                wanted.Add(DominionTreatyDefOf.Dominion_FederationMembership);
            wanted.RemoveAll(t => comp.Treaties.CannotSignReason(t, f, player) != null);
            return wanted.Count > 0 ? wanted.RandomElement() : null;
        }

        /// <summary>The envoy reaches the colony: they walk in, and the offer is made.</summary>
        public static void EnvoyArrived(Faction f, TreatyDef treaty)
        {
            if (treaty == null || f.HostileTo(Faction.OfPlayer)) return;
            Map map = VisitorUtility.PlayerCapitalMap();
            List<Pawn> party = VisitorUtility.MakeEnvoyParty(f);
            Pawn envoy = party.Count > 0 ? party[0] : null;
            if (!VisitorUtility.SpawnVisitors(f, map, party, GenDate.TicksPerDay / 2)) envoy = null;
            Offer(f, treaty, envoy);
        }

        private static void Offer(Faction f, TreatyDef treaty, Pawn envoy)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            string text = "Dominion_Offer_Text".Translate(f.Name, treaty.label, treaty.description);
            if (envoy != null) text = "Dominion_Offer_EnvoyArrived".Translate(envoy.LabelShortCap, f.Name) + "\n\n" + text;
            var node = new DiaNode(text);
            node.options.Add(new DiaOption("Dominion_Offer_Accept".Translate())
            {
                action = () =>
                {
                    if (comp.Treaties.CannotSignReason(treaty, f, Faction.OfPlayer) == null)
                        comp.Treaties.Sign(treaty, f, Faction.OfPlayer, f);
                },
                resolveTree = true,
            });
            node.options.Add(new DiaOption("Dominion_Offer_Decline".Translate())
            {
                action = () => comp.GetRelation(f, Faction.OfPlayer).favour -= 3f,
                resolveTree = true,
            });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "Dominion_Offer_Title".Translate(f.Name)));
        }
    }
}
