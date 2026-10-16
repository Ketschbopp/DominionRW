using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Resolves another mod's types and members by name, once, at startup. Every lookup is recorded
    /// so the self-test can show exactly what resolved and what did not. Nothing here throws:
    /// a missing member makes <see cref="Ok"/> false and the integration switches itself off.
    /// </summary>
    public class Binder
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private readonly List<string> report = new List<string>();
        private int failures;

        public bool Ok => failures == 0;
        public int Failures => failures;
        public IReadOnlyList<string> Report => report;

        public Type Type(string fullName)
        {
            Type t = AccessTools.TypeByName(fullName);
            Record(t != null, "type " + fullName);
            return t;
        }

        /// <summary>Finds a method by name. If <paramref name="parameters"/> is null, the first overload is used.</summary>
        public MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            MethodInfo m = null;
            if (type != null)
            {
                m = parameters != null
                    ? type.GetMethod(name, All, null, parameters, null)
                    : type.GetMethods(All).FirstOrDefault(x => x.Name == name);
            }
            Record(m != null, $"method {type?.Name ?? "?"}.{name}");
            return m;
        }

        public FieldInfo Field(Type type, string name)
        {
            FieldInfo f = type?.GetField(name, All);
            Record(f != null, $"field {type?.Name ?? "?"}.{name}");
            return f;
        }

        public PropertyInfo Property(Type type, string name)
        {
            PropertyInfo p = type?.GetProperty(name, All);
            Record(p != null, $"property {type?.Name ?? "?"}.{name}");
            return p;
        }

        /// <summary>Builds a fast delegate for a resolved method, so calls avoid reflection.</summary>
        public TDelegate Delegate<TDelegate>(MethodInfo method) where TDelegate : Delegate
        {
            if (method == null) return null;
            try
            {
                return AccessTools.MethodDelegate<TDelegate>(method);
            }
            catch (Exception e)
            {
                Record(false, $"delegate for {method.DeclaringType?.Name}.{method.Name} ({e.GetType().Name})");
                return null;
            }
        }

        public void Note(string text) => report.Add("  · " + text);

        private void Record(bool ok, string what)
        {
            if (!ok) failures++;
            report.Add((ok ? "  ✓ " : "  ✗ ") + what);
        }
    }
}
