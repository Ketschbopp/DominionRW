using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Required. Economics &amp; Demography is the source of every faction's population and wealth.
    /// Calls go through reflection, but only once per faction per day, so the cost is negligible.
    /// </summary>
    public class Integration_EconomicsDemography : DominionIntegration
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
    }
}
