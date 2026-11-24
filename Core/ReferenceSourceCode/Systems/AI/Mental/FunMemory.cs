using System.Collections.Generic;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Mental {
    public sealed class FunMemory {
        // Must stay >= 2x the last X of every repetition curve. Uses spread evenly over the
        // window weigh 0.5 on average, so a smaller limit would make heavy use look lighter.
        public const int MaxTrackedUsesPerSource = 48;
        private const int NoveltyCacheTicks = 60;

        #if !PRODUCTION_BUILD
        public static int DebugCachedNoveltyComputes;
        #endif

        // use ticks per source, oldest first
        private readonly Dictionary<FunSource, List<long>> uses = new();
        public readonly BCallback Changed = new();

        private FunSource cachedSource;
        private Curve cachedCurve;
        // A sentinel tick would overflow in ticks - cachedTick and keep serving the stale value
        private bool hasCachedNovelty;
        private long cachedTick;
        private float cachedNovelty = 1f;

        public int SourceCount => uses.Count;
        public Dictionary<FunSource, List<long>>.Enumerator GetEnumerator() => uses.GetEnumerator();

        public void RecordUse(FunSource src, long ticks, long windowTicks) {
            if (!src.IsValid) { return; }
            if (!uses.TryGetValue(src, out var list)) {
                list = new List<long>(4);
                uses.Add(src, list);
            }
            RemoveExpired(list, ticks, windowTicks);
            if (list.Count >= MaxTrackedUsesPerSource) {
                list.RemoveAt(0);
            }
            list.Add(ticks);
            hasCachedNovelty = false;
            Changed.Invoke();
        }

        public void Restore(FunSource src, long[] useTicks) {
            if (!src.IsValid || useTicks == null || useTicks.Length == 0) { return; }
            if (!uses.TryGetValue(src, out var list)) {
                list = new List<long>(useTicks.Length);
                uses.Add(src, list);
            }
            var from = Mathf.Max(0, useTicks.Length - MaxTrackedUsesPerSource);
            for (int i = from; i < useTicks.Length; i++) {
                list.Add(useTicks[i]);
            }
            list.Sort();
            while (list.Count > MaxTrackedUsesPerSource) {
                list.RemoveAt(0);
            }
            hasCachedNovelty = false;
        }

        public void Clear() {
            uses.Clear();
            hasCachedNovelty = false;
            Changed.Invoke();
        }

        // Each use in the window weighs 1 - age / window, so yesterday's session counts more
        // than one from almost a week ago, and nothing jumps when an old use expires.
        public float EffectiveUses(FunSource src, long ticks, long windowTicks) {
            if (windowTicks <= 0 || !uses.TryGetValue(src, out var list)) {
                return 0f;
            }
            float sum = 0f;
            for (int i = list.Count - 1; i >= 0; i--) {
                long age = ticks - list[i];
                if (age >= windowTicks) { break; }
                if (age < 0) { age = 0; }
                sum += 1f - (float) age / windowTicks;
            }
            return sum;
        }

        public float Novelty(FunSource src, long ticks, long windowTicks, Curve curve) {
            if (!src.IsValid || curve == null) { return 1f; }
            var effectiveUses = EffectiveUses(src, ticks, windowTicks);
            if (effectiveUses <= 0f) { return 1f; }
            return Mathf.Clamp01(curve.Evaluate(effectiveUses));
        }

        // For per-tick use during a session: the source doesn't change and novelty barely
        // moves within an hour, so recompute at most once per NoveltyCacheTicks.
        public float CachedNovelty(FunSource src, long ticks, long windowTicks, Curve curve) {
            if (hasCachedNovelty && src == cachedSource && curve == cachedCurve
                    && ticks >= cachedTick && ticks - cachedTick < NoveltyCacheTicks) {
                return cachedNovelty;
            }
            #if !PRODUCTION_BUILD
            DebugCachedNoveltyComputes++;
            #endif
            cachedNovelty = Novelty(src, ticks, windowTicks, curve);
            cachedSource = src;
            cachedCurve = curve;
            cachedTick = ticks;
            hasCachedNovelty = true;
            return cachedNovelty;
        }

        public int UseCount(FunSource src, long ticks, long windowTicks) {
            if (!uses.TryGetValue(src, out var list)) { return 0; }
            int count = 0;
            for (int i = list.Count - 1; i >= 0; i--) {
                if (ticks - list[i] >= windowTicks) { break; }
                count++;
            }
            return count;
        }

        public long LastUseTick(FunSource src) {
            if (uses.TryGetValue(src, out var list) && list.Count > 0) {
                return list[list.Count - 1];
            }
            return 0;
        }

        public void CollectRecent(List<FunSource> res, long ticks, long windowTicks) {
            res.Clear();
            foreach (var kv in uses) {
                var list = kv.Value;
                if (list.Count > 0 && ticks - list[list.Count - 1] < windowTicks) {
                    res.Add(kv.Key);
                }
            }
            res.Sort((a, b) => LastUseTick(b).CompareTo(LastUseTick(a)));
        }

        private static void RemoveExpired(List<long> list, long ticks, long windowTicks) {
            int expired = 0;
            while (expired < list.Count && ticks - list[expired] >= windowTicks) {
                expired++;
            }
            if (expired > 0) {
                list.RemoveRange(0, expired);
            }
        }
    }
}
