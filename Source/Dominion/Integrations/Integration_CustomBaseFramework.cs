using System;
using System.Reflection;
using RimWorld.Planet;

namespace Dominion
{
    /// <summary>Custom Base Framework marks each faction's main seat ("Hauptsitz"), which Dominion uses as its capital.</summary>
    public class Integration_CustomBaseFramework : DominionIntegration
    {
        public override string PackageId => "EvilEyeGuy.CustomBaseFramework";
        public override string Label => "Custom Base Framework";

        private Func<Settlement, bool> isSeat;

        protected internal override void Bind(Binder b)
        {
            Type seats = b.Type("CustomBaseFramework.Hauptsitze");
            MethodInfo m = b.Method(seats, "IstHauptsitz", new[] { typeof(Settlement) });
            isSeat = b.Delegate<Func<Settlement, bool>>(m);
            b.Type("CustomBaseFramework.SettlementPlanDef");
        }

        /// <summary>True if CBF considers this settlement its faction's seat.</summary>
        public bool IsSeat(Settlement s)
        {
            if (!IsActive || isSeat == null || s == null) return false;
            try
            {
                return isSeat(s);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Custom Base Framework seat check failed: " + e, 0x0CBF);
                return false;
            }
        }
    }
}
