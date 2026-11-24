using Game.Systems.Items;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Economy {
    // Picks a random market item for loot and signal offers the way traders pick stock:
    // gated by sector difficulty, weighted by rarity, never a NoRandomLoot item. A uniform,
    // ungated pick handed out 1M-credit story items in 0% sectors.
    public static class MarketLootPool {
        // Same base tolerance traders use (TradeSys.GenInventoryFor), kept deterministic
        public const float SectorDifficultyTolerance = 0.1f;
        private const float MinWeight = 0.01f;

        /// <param name="maxUnitValue">Only items whose adjusted unit value fits; 0 = no cap</param>
        public static bool TryPick(GameState S, string[] groups, float sectorDiff,
                float maxUnitValue, Def excluded, Rng rng, out MarketItem item) {
            var w = Caches.WeightedInts;
            w.Clear();
            MarketItem fallback = null;
            float fallbackValue = 0f;
            var maxDiff = sectorDiff + SectorDifficultyTolerance;
            foreach (var mi in MarketItem.CFDs.Values) {
                if (!IsEligible(S, mi, groups, excluded)) {
                    continue;
                }
                var value = UnitValue(S, mi);
                if (fallback == null
                        || mi.MinSectorDifficulty < fallback.MinSectorDifficulty
                        || (mi.MinSectorDifficulty == fallback.MinSectorDifficulty
                            && value < fallbackValue)) {
                    fallback = mi;
                    fallbackValue = value;
                }
                if (mi.MinSectorDifficulty > maxDiff) {
                    continue;
                }
                if (maxUnitValue > 0f && value > maxUnitValue) {
                    continue;
                }
                w[mi.IdH] = Mathf.Max(mi.Rarity, MinWeight);
            }
            if (rng.TryWeightedFrom(w, out var idH)) {
                w.Clear();
                item = MarketItem.CFDs[idH];
                return true;
            }
            w.Clear();
            item = fallback;
            if (fallback != null) {
                D.Warn("MarketLootPool: nothing fits sector difficulty {0} and value cap {1}, falling back to {2}",
                    sectorDiff, maxUnitValue, fallback.Id);
            }
            return fallback != null;
        }

        private static bool IsEligible(GameState S, MarketItem mi, string[] groups, Def excluded) {
            if (mi.NoRandomLoot || mi.Def == null) {
                return false;
            }
            if (excluded != null && mi.Def == excluded) {
                return false;
            }
            if (!InGroups(mi, groups)) {
                return false;
            }
            if (InventoryItemDef.TryForDef(mi.Def.IdH, out var invItem)
                    && invItem.SpawnRequirements != null
                    && !S.Situation.Matches(invItem.SpawnRequirements, S.Difficulty.Level)) {
                return false;
            }
            return true;
        }

        private static bool InGroups(MarketItem mi, string[] groups) {
            for (int i = 0; i < groups.Length; i++) {
                var group = MarketGroup.Find(groups[i]);
                if (group != null && Arrays.ContainsSafe(mi.GroupsH, group.IdH)) {
                    return true;
                }
            }
            return false;
        }

        public static float UnitValue(GameState S, MarketItem mi) {
            var adjusted = S.Market.Adjusted(mi);
            var value = adjusted != null ? adjusted.Value : mi.Value;
            return value < 1f ? 1f : value;
        }
    }
}
