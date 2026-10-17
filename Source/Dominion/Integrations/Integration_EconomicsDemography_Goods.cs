using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// Real goods in E&amp;D stockpiles: relief aid arrives as the food and medicine the caravan carried (not just its
    /// silver value), and a famine eats into the faction's stored food. Optional: without the members, aid is credited
    /// as silver and famines only change Dominion's numbers.
    /// </summary>
    public partial class Integration_EconomicsDemography
    {
        private MethodInfo addItem;
        private FieldInfo inventoryField;

        /// <summary>Share of a faction's stored food a famine consumes.</summary>
        private const float FamineFoodShare = 0.35f;

        private void BindGoods(Type stockpile, Binder b)
        {
            addItem = stockpile?.GetMethod("AddItem", BindingFlags.Public | BindingFlags.Instance);
            if (addItem != null)
            {
                ParameterInfo[] ps = addItem.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(ThingDef) || ps[1].ParameterType != typeof(int)) addItem = null;
            }
            inventoryField = stockpile?.GetField("inventory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (inventoryField != null && !typeof(Dictionary<string, int>).IsAssignableFrom(inventoryField.FieldType)) inventoryField = null;
            b.Note(addItem != null ? "relief aid reaches stockpiles as goods" : "stockpile AddItem not found: relief aid arrives as silver");
            b.Note(inventoryField != null ? "famines consume stored food" : "stockpile inventory not found: famines do not touch stored food");
        }

        /// <summary>
        /// Puts handed-over goods into a faction's E&amp;D stockpile (silver as silver, everything else as items).
        /// False if nothing could be credited, so the caller can fall back to crediting silver.
        /// </summary>
        public bool ReceiveGoods(Faction f, List<ThingDefCount> goods)
        {
            if (!IsActive || addItem == null || f == null || f.IsPlayer || goods == null || goods.Count == 0) return false;
            bool any = false;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null) return false;
                ParameterInfo[] ps = addItem.GetParameters();
                object[] args = new object[ps.Length];
                foreach (ThingDefCount g in goods)
                {
                    if (g.ThingDef == null || g.Count <= 0) continue;
                    if (g.ThingDef == ThingDefOf.Silver)
                    {
                        AddSilver(f, g.Count);
                        any = true;
                        continue;
                    }
                    args[0] = g.ThingDef;
                    args[1] = g.Count;
                    for (int i = 2; i < ps.Length; i++)
                        args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType == typeof(int) ? (object)(-1) : null);
                    addItem.Invoke(stock, args);
                    any = true;
                }
                setWealthDirty?.Invoke(stock, null);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Adding goods to an E&D stockpile failed: " + e.Message, 0x0ED8);
            }
            return any;
        }

        /// <summary>
        /// Removes <paramref name="share"/> of every human food in a faction's stockpile. Returns the market value consumed.
        /// </summary>
        public float ConsumeFood(Faction f, float share)
        {
            if (!IsActive || inventoryField == null || f == null || f.IsPlayer || share <= 0f) return 0f;
            float value = 0f;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null || !(inventoryField.GetValue(stock) is Dictionary<string, int> inventory) || inventory.Count == 0) return 0f;
                var keys = new List<string>(inventory.Keys);
                foreach (string key in keys)
                {
                    ThingDef def = DefOfKey(key);
                    if (def == null || !IsFood(def)) continue;
                    int have = inventory[key];
                    if (have <= 0) continue;
                    int eaten = Mathf.Clamp(Mathf.CeilToInt(have * share), 0, have);
                    if (eaten <= 0) continue;
                    if (have - eaten > 0) inventory[key] = have - eaten;
                    else inventory.Remove(key);
                    value += def.BaseMarketValue * eaten;
                }
                setWealthDirty?.Invoke(stock, null);
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Consuming E&D food failed: " + e.Message, 0x0ED9);
            }
            return value;
        }

        /// <summary>How many of an item a faction holds in its E&amp;D stockpile (all qualities), or -1 if unknown.</summary>
        public int StockOf(Faction f, ThingDef def)
        {
            if (!IsActive || inventoryField == null || f == null || f.IsPlayer || def == null) return -1;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null || !(inventoryField.GetValue(stock) is Dictionary<string, int> inventory)) return -1;
                int n = 0;
                foreach (KeyValuePair<string, int> kv in inventory)
                    if (DefOfKey(kv.Key) == def) n += kv.Value;
                return n;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Reading an E&D stockpile failed: " + e.Message, 0x0EDA);
                return -1;
            }
        }

        /// <summary>
        /// Takes up to <paramref name="count"/> of an item out of a faction's E&amp;D stockpile (any quality). Returns how
        /// many were taken, or -1 if the stockpile cannot be read (the caller then treats the faction's stores as unlimited).
        /// </summary>
        public int RemoveGoods(Faction f, ThingDef def, int count)
        {
            if (!IsActive || inventoryField == null || f == null || f.IsPlayer || def == null || count <= 0) return -1;
            try
            {
                object stock = StockpileOf(f);
                if (stock == null || !(inventoryField.GetValue(stock) is Dictionary<string, int> inventory)) return -1;
                int taken = 0;
                foreach (string key in new List<string>(inventory.Keys))
                {
                    if (taken >= count) break;
                    if (DefOfKey(key) != def) continue;
                    int n = Mathf.Min(count - taken, inventory[key]);
                    if (inventory[key] - n > 0) inventory[key] -= n;
                    else inventory.Remove(key);
                    taken += n;
                }
                setWealthDirty?.Invoke(stock, null);
                return taken;
            }
            catch (Exception e)
            {
                DLog.WarningOnce("Taking goods from an E&D stockpile failed: " + e.Message, 0x0EDB);
                return -1;
            }
        }

        private static bool IsFood(ThingDef def) =>
            def.IsNutritionGivingIngestible && def.ingestible != null && def.ingestible.HumanEdible && !def.IsDrug && !def.IsCorpse;

        /// <summary>E&amp;D keys items as "defName" or "defName_quality".</summary>
        private static ThingDef DefOfKey(string key)
        {
            if (key.NullOrEmpty()) return null;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(key);
            if (def != null) return def;
            int i = key.LastIndexOf('_');
            return i > 0 && int.TryParse(key.Substring(i + 1), out _) ? DefDatabase<ThingDef>.GetNamedSilentFail(key.Substring(0, i)) : null;
        }

        /// <summary>A famine empties the granaries: a share of stored food is eaten or spoils.</summary>
        private void OnEventGoods(DominionEvent ev)
        {
            if (ev?.def == null || ev.primary == null || ev.primary.IsPlayer || ev.def.defName != "Dominion_Famine") return;
            float share = Mathf.Clamp(FamineFoodShare * Mathf.Max(0.5f, ev.magnitude), 0.1f, 0.6f);
            float lost = ConsumeFood(ev.primary, share);
            if (lost > 0f) DLog.Verbose($"Famine in {ev.primary.Name} consumed stored food worth {lost:F0}");
        }
    }
}
