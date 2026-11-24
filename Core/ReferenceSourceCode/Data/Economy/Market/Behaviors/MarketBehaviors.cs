using System.Collections.Generic;
using KL.Collections;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Data.Economy {
    public static class MarketBehaviors {

        [ExecuteOnReload]
        public static void CleanupStatics() {
            registry.Clear();
            WeightedLongTerm.Clear();
            WeightedShortTerm.Clear();
            WeightedShortTermPositive.Clear();
        }

        private static readonly Dictionary<int, MarketBehavior> registry = new();
        public static Dictionary<int, MarketBehavior> All => registry;
        public static readonly Dictionary<MarketBehavior, float> WeightedLongTerm = new();
        public static readonly Dictionary<MarketBehavior, float> WeightedShortTerm = new();
        public static readonly Dictionary<MarketBehavior, float> WeightedShortTermPositive = new();

        private static void EnsureWeightedDicts() {
            if (WeightedLongTerm.Count != 0) { return; }
            foreach (var v in registry) {
                var b = v.Value;
                if (b.ChanceLongTerm > 0f) {
                    WeightedLongTerm[b] = b.ChanceLongTerm;
                }
                if (b.ChanceShortTerm > 0f) {
                    WeightedShortTerm[b] = b.ChanceShortTerm;
                    if (b.IsPositive) {
                        WeightedShortTermPositive[b] = b.ChanceShortTerm;
                    }
                }
            }
        }

        public static MarketBehavior Get(string id) {
            var f = registry.Get(Hashes.S(id), null);
            if (f == null) {
                D.Err("Unknown market behavior: {0}", id);
                return null;
            }
            return f;
        }

        public static MarketBehavior GetLongTerm(Rng rng) {
            EnsureWeightedDicts();
            return rng.WeightedFrom(WeightedLongTerm);
        }
        public static MarketBehavior GetShortTerm(Rng rng) {
            EnsureWeightedDicts();
            return rng.WeightedFrom(WeightedShortTerm);
        }
        public static MarketBehavior GetShortTermPositive(Rng rng) {
            EnsureWeightedDicts();
            return rng.WeightedFrom(WeightedShortTermPositive);
        }

        public static void Add(string id, int type, MarketBehavior b) {
            D.Ass(!registry.ContainsKey(type),
                "Registry already contains market behavior {0}!", id);
            registry[type] = b;
        }
    }
}
