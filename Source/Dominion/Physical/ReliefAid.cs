using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Aid for a faction in crisis (famine, plague, disaster): a caravan brings medicine, food or silver to one of
    /// its settlements. Worth favour, goodwill and culture, and it steadies the faction.
    /// </summary>
    public static class ReliefAid
    {
        public const float MinValue = 300f;
        public const float MaxValue = 1500f;

        public static bool IsAid(Thing t) =>
            t.def == ThingDefOf.Silver || t.def.IsMedicine
            || (t.def.IsNutritionGivingIngestible && t.def.ingestible.HumanEdible && !t.def.IsDrug && !t.def.IsCorpse);

        public static float AidValue(Caravan caravan)
        {
            float v = 0f;
            foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                if (IsAid(t)) v += t.MarketValue * t.stackCount;
            return v;
        }

        /// <summary>Hands over aid up to <see cref="MaxValue"/>: medicine first, then food, then silver. Returns the value given.</summary>
        public static float Give(Caravan caravan, List<ThingDefCount> handedOver = null)
        {
            var items = new List<Thing>();
            foreach (Thing t in CaravanInventoryUtility.AllInventoryItems(caravan))
                if (IsAid(t)) items.Add(t);
            items.SortBy(t => t.def.IsMedicine ? 0 : t.def == ThingDefOf.Silver ? 2 : 1);

            float given = 0f;
            foreach (Thing t in items)
            {
                if (given >= MaxValue) break;
                float unit = Mathf.Max(0.01f, t.MarketValue);
                int count = Mathf.Min(t.stackCount, Mathf.CeilToInt((MaxValue - given) / unit));
                if (count <= 0) continue;
                given += unit * count;
                handedOver?.Add(new ThingDefCount(t.def, count));
                Thing part = t.SplitOff(count);
                if (!part.Destroyed) part.Destroy();
            }
            return given;
        }

        public static void Deliver(Faction f, Caravan caravan, Pawn envoy)
        {
            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            var handedOver = new List<ThingDefCount>();
            float value = Give(caravan, handedOver);
            FactionState s = comp.GetState(f);
            // The goods reach their E&D stockpile as themselves (silver value if that fails) and ease the crisis economy.
            if (!(IntegrationRegistry.Get<Integration_EconomicsDemography>()?.ReceiveGoods(f, handedOver) ?? false))
                EconomyEffects.Receive(f, Mathf.RoundToInt(value));
            EconomyEffects.Shock(f, Mathf.Min(0.06f, value / Mathf.Max(1000f, s.wealth) * 2f));
            RelationState view = comp.GetRelation(f, Faction.OfPlayer);
            view.favour += Mathf.Min(25f, value / 40f);
            f.TryAffectGoodwillWith(Faction.OfPlayer, Mathf.Clamp(Mathf.RoundToInt(value / 120f), 2, 12), canSendMessage: false, canSendHostilityLetter: false);
            s.playerCulture = Mathf.Min(100f, s.playerCulture + 5f);
            s.stability = Mathf.Min(100f, s.stability + 4f);
            s.crisisUntilTick = Find.TickManager.TicksGame; // the worst is over
            envoy?.skills?.Learn(SkillDefOf.Social, 800f);
            EventBus.Raise(DominionEventDefOf.Dominion_AidSent, Faction.OfPlayer, f, detail: value.ToStringMoney());
            Messages.Message("Dominion_Aid_Done".Translate(f.Name, value.ToStringMoney()), caravan, MessageTypeDefOf.PositiveEvent, historical: true);
        }
    }
}
