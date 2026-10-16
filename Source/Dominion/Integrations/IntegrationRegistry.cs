using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace Dominion
{
    public static class IntegrationRegistry
    {
        /// <summary>Mods Dominion replaces. Running them together would double news, price changes and coups.</summary>
        public static readonly string[] IncompatibleMods = { "xwe1ss.thenews" };

        private static readonly List<DominionIntegration> all = new List<DominionIntegration>();
        private static readonly Dictionary<Type, DominionIntegration> byType = new Dictionary<Type, DominionIntegration>();

        public static IReadOnlyList<DominionIntegration> All => all;

        public static T Get<T>() where T : DominionIntegration =>
            byType.TryGetValue(typeof(T), out DominionIntegration i) ? (T)i : null;

        /// <summary>True when the integration exists and bound successfully.</summary>
        public static bool IsActive<T>() where T : DominionIntegration => Get<T>()?.IsActive ?? false;

        public static bool IsModActive(string packageId) =>
            ModLister.GetActiveModWithIdentifier(packageId, ignorePostfix: true) != null;

        internal static void Initialize()
        {
            all.Clear();
            byType.Clear();

            foreach (string id in IncompatibleMods)
                if (IsModActive(id))
                    DLog.Error($"'{id}' is active. Dominion replaces it, and running both will double news, prices and coups. Please disable one of them.");

            foreach (Type t in GenTypes.AllSubclassesNonAbstract(typeof(DominionIntegration)))
            {
                DominionIntegration integration;
                try
                {
                    integration = (DominionIntegration)Activator.CreateInstance(t);
                }
                catch (Exception e)
                {
                    DLog.Error($"Could not create integration {t.FullName}: {e}");
                    continue;
                }
                all.Add(integration);
                byType[t] = integration;
                Activate(integration);
            }

            all.SortBy(i => i.Required ? 0 : 1, i => i.Label);

            var summary = new StringBuilder("Integrations: ");
            foreach (DominionIntegration i in all)
                summary.Append(i.Label).Append('=').Append(i.Status).Append("  ");
            DLog.Message(summary.ToString());
        }

        private static void Activate(DominionIntegration integration)
        {
            if (!IsModActive(integration.PackageId) && !(integration.AlternatePackageIds?.Any(IsModActive) ?? false))
            {
                integration.Status = IntegrationStatus.NotInstalled;
                if (integration.Required)
                    DLog.Error($"Required mod '{integration.Label}' ({integration.PackageId}) is not active. Dominion needs it to work.");
                return;
            }

            if (!integration.Required && !DominionMod.Settings.IntegrationEnabled(integration.PackageId))
            {
                integration.Status = IntegrationStatus.Disabled;
                return;
            }

            var binder = new Binder();
            try
            {
                integration.Bind(binder);
            }
            catch (Exception e)
            {
                binder.Note("exception: " + e.Message);
                integration.BindingReport = binder.Report;
                integration.Status = IntegrationStatus.Failed;
                Report(integration, binder.Failures + 1);
                return;
            }

            integration.BindingReport = binder.Report;
            if (!binder.Ok)
            {
                integration.Status = IntegrationStatus.Failed;
                Report(integration, binder.Failures);
                return;
            }

            integration.Status = IntegrationStatus.Active;
            try
            {
                integration.OnActivated();
            }
            catch (Exception e)
            {
                integration.Status = IntegrationStatus.Failed;
                DLog.Error($"Integration '{integration.Label}' failed to activate: {e}");
            }
        }

        private static void Report(DominionIntegration integration, int failures)
        {
            string text = $"Integration '{integration.Label}' could not bind {failures} member(s), so it is switched off. " +
                          "This usually means that mod updated or has no 1.7 build yet.\n" + string.Join("\n", integration.BindingReport);
            if (integration.Required) DLog.Error(text);
            else DLog.Warning(text);
        }

        internal static void NotifyWorldLoaded()
        {
            foreach (DominionIntegration i in all)
            {
                if (!i.IsActive) continue;
                try
                {
                    i.OnWorldLoaded();
                }
                catch (Exception e)
                {
                    DLog.WarningOnce($"Integration '{i.Label}' OnWorldLoaded failed: {e}", i.PackageId.GetHashCode());
                }
            }
        }

        public static float Sum(System.Func<DominionIntegration, float> modifier)
        {
            float sum = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].IsActive) continue;
                try
                {
                    sum += modifier(all[i]);
                }
                catch (Exception e)
                {
                    DLog.WarningOnce($"Integration '{all[i].Label}' modifier failed: {e.Message}", all[i].PackageId.GetHashCode() ^ 0x5u.GetHashCode());
                }
            }
            return sum;
        }

        public static void ForEachActive(Action<DominionIntegration> action)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].IsActive) continue;
                try
                {
                    action(all[i]);
                }
                catch (Exception e)
                {
                    DLog.WarningOnce($"Integration '{all[i].Label}' failed: {e}", all[i].PackageId.GetHashCode() ^ 0x77);
                }
            }
        }

        public static string SelfTestReport()
        {
            var sb = new StringBuilder("Dominion integration self-test\n");
            foreach (DominionIntegration i in all)
            {
                sb.AppendLine($"{i.Label} [{i.PackageId}]{(i.Required ? " (required)" : "")}: {i.Status}");
                foreach (string line in i.BindingReport)
                    sb.AppendLine(line);
            }
            return sb.ToString();
        }
    }
}
