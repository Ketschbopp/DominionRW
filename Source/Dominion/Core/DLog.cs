using Verse;

namespace Dominion
{
    /// <summary>Prefixed logging. Verbose output is gated by the debug logging setting.</summary>
    public static class DLog
    {
        private const string Prefix = "[Dominion] ";

        public static void Message(string text) => Log.Message(Prefix + text);

        public static void Verbose(string text)
        {
            if (DominionMod.Settings != null && DominionMod.Settings.debugLogging)
                Log.Message(Prefix + text);
        }

        public static void Warning(string text) => Log.Warning(Prefix + text);

        public static void Error(string text) => Log.Error(Prefix + text);

        /// <summary>Logs a warning only once per key, for repeated failures.</summary>
        public static void WarningOnce(string text, int key) => Log.WarningOnce(Prefix + text, key);
    }
}
