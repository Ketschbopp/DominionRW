using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// God-mode buttons for talk and trade, on the wars and treaties page of <see cref="Dialog_DominionGodMode"/>:
    /// make every standing deal's next delivery now, call any deal off, restock a faction's trade office, and clear the
    /// audience and talk cooldowns with faction A.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TalkGodMode
    {
        static TalkGodMode()
        {
            Dialog_DominionGodMode.ExtraWarTools.Add(Draw);
        }

        private static void Draw(Listing_Standard l)
        {
            l.Gap(6f);
            l.Label("Dominion_God_H_Talk".Translate().ToString().ToUpper());
            l.GapLine(4f);
            if (l.ButtonText("Dominion_God_DeliverAll".Translate(TradeDeals.Open.Count())))
            {
                foreach (TradeAgreement d in TradeDeals.Open.ToList()) TradeDeals.DevDeliverNow(d);
                Messages.Message("Dominion_God_Done".Translate("deliveries"), MessageTypeDefOf.TaskCompletion, false);
            }
            if (l.ButtonText("Dominion_God_CancelDeal".Translate()))
            {
                List<FloatMenuOption> options = TradeDeals.Open.ToList().Select(d => new FloatMenuOption(d.faction.Name + ": " + d.Summary, () => TradeDeals.Cancel(d))).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("Dominion_None".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            Faction a = Dialog_DominionGodMode.PickedA;
            if (a == null || a.IsPlayer) return;
            if (l.ButtonText("Dominion_God_ResetTalk".Translate(a.Name)))
            {
                PlayerState player = WorldComponent_Dominion.Instance.Player;
                player.SetCooldown("Audience", a.loadID, -1);
                foreach (TalkTopic t in DiplomacyTalk.Topics) player.SetCooldown("Talk_" + t, a.loadID, -1);
                Messages.Message("Dominion_God_Done".Translate("cooldowns"), MessageTypeDefOf.TaskCompletion, false);
            }
            if (l.ButtonText("Dominion_God_Restock".Translate(a.Name)))
            {
                TradeDeals.DevRestock(a);
                Messages.Message("Dominion_God_Done".Translate("restock"), MessageTypeDefOf.TaskCompletion, false);
            }
        }
    }
}
