using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Required. Economics &amp; Demography is the source of every faction's population and wealth.
    /// Calls go through reflection, but only once per faction per day, so the cost is negligible.
    /// </summary>
    public partial class Integration_EconomicsDemography : DominionIntegration
    {
        public override string PackageId => "helldan.economicsdemography";
        public override string Label => "Economics & Demography";
        public override bool Required => true;

        private FieldInfo instanceField;
        private MethodInfo isSimulated;
        private MethodInfo getPopulation;
        private MethodInfo getTotalLiving;
        private MethodInfo getStockpile;
        private MethodInfo modifyPopulation;
        private MethodInfo getTotalWealth;
        private FieldInfo inflationField;
        private FieldInfo priceModifiersField;
        private MethodInfo tryConsumeWealth;
        private FieldInfo silverField;
        private MethodInfo setWealthDirty;
        private MethodInfo produceGoods;
        private bool productionPatched;

        private readonly object[] oneArg = new object[1];

        protected internal override void Bind(Binder b)
        {
            Type manager = b.Type("EconomicsDemography.WorldPopulationManager");
            instanceField = b.Field(manager, "Instance");
            isSimulated = b.Method(manager, "IsSimulatedFaction", new[] { typeof(Faction) });
            getPopulation = b.Method(manager, "GetPopulation", new[] { typeof(Faction) });
            getTotalLiving = b.Method(manager, "GetTotalLiving", new[] { typeof(Faction) });
            getStockpile = b.Method(manager, "GetStockpile", new[] { typeof(Faction) });
            modifyPopulation = b.Method(manager, "ModifyPopulation");
            inflationField = b.Field(manager, "currentInflation");
            Type stockpile = b.Type("EconomicsDemography.VirtualStockpile");
            getTotalWealth = b.Method(stockpile, "GetTotalWealth", Type.EmptyTypes);
            priceModifiersField = b.Field(manager, "globalPriceModifiers");
            tryConsumeWealth = b.Method(stockpile, "TryConsumeWealth");
            silverField = b.Field(stockpile, "silver");
            setWealthDirty = b.Method(stockpile, "SetWealthDirty", Type.EmptyTypes);
            // Optional: without it, Dominion's economic climate only changes its own index, not E&D's production.
            produceGoods = manager?.GetMethod("ProduceGoodsCategorized", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            b.Note(produceGoods != null ? "production hook: ProduceGoodsCategorized" : "production hook not found (climate will not reach E&D production)");
            BindGoods(stockpile, b);
            BindLinks(manager, stockpile, b);
        }

        protected internal override void OnWorldLoaded() => ResetLinks();

        /// <summary>Dominion's economic climate scales E&amp;D's production for each faction.</summary>
        protected internal override void OnActivated()
        {
            PatchLinks();
            if (produceGoods == null || productionPatched) return;
            try
            {
                DominionMod.Harmony.Patch(produceGoods,
                    prefix: new HarmonyLib.HarmonyMethod(typeof(Integration_EconomicsDemography), nameof(ProductionPrefix)));
                productionPatched = true;
            }
            catch (Exception e)
            {
                DLog.Warning("Could not hook Economics & Demography production: " + e.Message);
            }
        }

        /// <summary>Harmony prefix on WorldPopulationManager.ProduceGoodsCategorized: the labour force works harder or less.</summary>
        public static void ProductionPrefix(Faction f, ref int adults, ref float elders, ref float kids)
        {
            try
            {
                if (f == null || WorldComponent_Dominion.Instance == null) return;
                NoteProduction(f);
                float factor = EconomyEffects.ProductionFactor(f);
                if (Mathf.Approximately(factor, 1f)) return;
                adults = Mathf.Max(0, Mathf.RoundToInt(adults * factor));
                elders *= factor;
                kids *= factor;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Production hook failed: " + e.Message, 0x0ED5);
            }
        }

        private object StockpileOf(Faction f)
        {
            object manager = IsActive ? Manager : null;
            if (manager == null || f == null) return null;
            try
            {
                oneArg[0] = f;
                return getStockpile.Invoke(manager, oneArg);
            }
            finally
            {
                oneArg[0] = null;
            }
        }

        /// <summary>Adds silver to a faction's E&amp;D stockpile (tribute, reparations, aid received).</summary>
        public void AddSilver(Faction f, int amount)
        {
            if (amount <= 0 || f == null || f.IsPlayer) return;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null) return;
                silverField.SetValue(stock, (int)silverField.GetValue(stock) + amount);
                setWealthDirty?.Invoke(stock, null);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Adding E&D silver failed: " + e.Message, 0x0ED6);
            }
        }

        /// <summary>Takes up to <paramref name="amount"/> silver from a faction's stockpile. Returns what was taken.</summary>
        public int TakeSilver(Faction f, int amount)
        {
            if (amount <= 0 || f == null || f.IsPlayer) return 0;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null) return 0;
                int have = (int)silverField.GetValue(stock);
                int taken = Mathf.Clamp(amount, 0, have);
                silverField.SetValue(stock, have - taken);
                setWealthDirty?.Invoke(stock, null);
                return taken;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Taking E&D silver failed: " + e.Message, 0x0ED7);
                return 0;
            }
        }

        /// <summary>People move with a settlement that changes hands: the share living there goes to the new owner.</summary>
        public void MovePopulation(Faction from, Faction to, int amount)
        {
            if (amount <= 0 || from == null || to == null || from == to) return;
            ModifyPopulation(from, -amount);
            if (!to.IsPlayer) ModifyPopulation(to, amount);
        }

        private object Manager => instanceField?.GetValue(null);

        /// <summary>
        /// Reads a faction's adult (fighting-age) population, total living population and stockpile wealth.
        /// Returns false if E&amp;D is not bound or does not simulate this faction.
        /// </summary>
        public bool TryRead(Faction f, out int adults, out int living, out float wealth)
        {
            adults = 0;
            living = 0;
            wealth = 0f;
            if (!IsActive || f == null) return false;
            object manager = Manager;
            if (manager == null) return false;
            try
            {
                oneArg[0] = f;
                if (!(bool)isSimulated.Invoke(manager, oneArg)) return false;
                adults = (int)getPopulation.Invoke(manager, oneArg);
                living = (int)getTotalLiving.Invoke(manager, oneArg);
                object stockpile = getStockpile.Invoke(manager, oneArg);
                if (stockpile != null)
                    wealth = (float)getTotalWealth.Invoke(stockpile, null);
                return true;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Reading Economics & Demography failed: " + e, 0x0ED1);
                return false;
            }
            finally
            {
                oneArg[0] = null;
            }
        }

        /// <summary>World price inflation (1 = none), or 1 if unavailable.</summary>
        public float Inflation
        {
            get
            {
                object manager = IsActive ? Manager : null;
                return manager != null ? (float)inflationField.GetValue(manager) : 1f;
            }
        }

        /// <summary>Adds (or with a negative amount removes) population. Used by wars, plagues and famine.</summary>
        public void ModifyPopulation(Faction f, int amount)
        {
            object manager = IsActive ? Manager : null;
            if (manager == null || f == null || amount == 0) return;
            try
            {
                ParameterInfo[] ps = modifyPopulation.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = f;
                args[1] = amount;
                for (int i = 2; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                modifyPopulation.Invoke(manager, args);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Changing population through Economics & Demography failed: " + e, 0x0ED2);
            }
        }
    
        /// <summary>
        /// Takes goods worth <paramref name="value"/> silver out of a faction's E&amp;D stockpile, at E&amp;D's
        /// own prices. Used for embargo losses. Returns false when the stockpile cannot cover it.
        /// </summary>
        public bool TryConsumeWealth(Faction f, float value)
        {
            object manager = IsActive ? Manager : null;
            if (manager == null || f == null || value <= 0f) return false;
            try
            {
                oneArg[0] = f;
                object stockpile = getStockpile.Invoke(manager, oneArg);
                if (stockpile == null) return false;
                ParameterInfo[] ps = tryConsumeWealth.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = value;
                if (ps.Length > 1) args[1] = priceModifiersField.GetValue(manager);
                for (int i = 2; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                return (bool)tryConsumeWealth.Invoke(stockpile, args);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Consuming Economics & Demography wealth failed: " + e, 0x0ED3);
                return false;
            }
            finally
            {
                oneArg[0] = null;
            }
        }

        /// <summary>
        /// Riots and revolutions push a small share of people out. They settle with the most stable
        /// neighbouring faction, so the world population is kept.
        /// </summary>
        public override void OnEvent(DominionEvent ev)
        {
            OnEventGoods(ev);
            if (!DominionMod.Settings.emigration || ev.primary == null || ev.primary.IsPlayer) return;
            float share;
            if (ev.def == DominionEventDefOf.Dominion_Riots) share = 0.01f;
            else if (ev.def == DominionEventDefOf.Dominion_Revolution) share = 0.03f;
            else return;

            WorldComponent_Dominion comp = WorldComponent_Dominion.Instance;
            FactionState s = comp.GetState(ev.primary);
            int leaving = Mathf.Min(Mathf.RoundToInt(s.population * share * Mathf.Max(0.5f, ev.magnitude)), 40);
            if (leaving < 1) return;

            Faction destination = null;
            float best = 60f;
            foreach (Faction other in comp.Cache.Simulated)
            {
                if (other == ev.primary || other.HostileTo(ev.primary)) continue;
                float st = comp.GetState(other).stability;
                if (st > best) { best = st; destination = other; }
            }

            ModifyPopulation(ev.primary, -leaving);
            if (destination != null) ModifyPopulation(destination, leaving);
            EventBus.Raise(DominionEventDefOf.Dominion_Emigration, ev.primary, destination, magnitude: leaving / 20f,
                detail: leaving.ToString(), source: PackageId);
        }
    }
}
