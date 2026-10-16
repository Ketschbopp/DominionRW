using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Dominion
{
    /// <summary>
    /// A value for UI code that is recomputed at most a few times per second of real time, whatever the game speed,
    /// and also while paused (so the window catches up after the player acts). Call <see cref="Invalidate"/>
    /// after a change you know about.
    /// </summary>
    public class UICache<T>
    {
        private readonly Func<T> compute;
        private readonly float interval;
        private T value;
        private float nextTime = -1f;

        public UICache(Func<T> compute, float intervalSeconds = 0.5f)
        {
            this.compute = compute;
            interval = intervalSeconds;
        }

        public T Value
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now >= nextTime)
                {
                    value = compute();
                    nextTime = now + interval;
                }
                return value;
            }
        }

        public void Invalidate() => nextTime = -1f;
    }

    /// <summary>Like <see cref="UICache{T}"/>, keyed (per faction, usually).</summary>
    public class UICache<TKey, T>
    {
        private readonly Func<TKey, T> compute;
        private readonly float interval;
        private readonly Dictionary<TKey, (T value, float next)> values = new Dictionary<TKey, (T, float)>();

        public UICache(Func<TKey, T> compute, float intervalSeconds = 0.5f)
        {
            this.compute = compute;
            interval = intervalSeconds;
        }

        public T Get(TKey key)
        {
            float now = Time.realtimeSinceStartup;
            if (values.TryGetValue(key, out (T value, float next) e) && now < e.next) return e.value;
            T v = compute(key);
            values[key] = (v, now + interval);
            return v;
        }

        public void Invalidate() => values.Clear();
    }
}
