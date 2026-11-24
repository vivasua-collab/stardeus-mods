using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Data;
using Game.Research;
using KL.Collections;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen {
    public static class GenSpawn {
        private static readonly Dictionary<Def, float> candidates = new();
        // When we want to spawn an item, if category has a chance, we will first
        // roll to find out if this category is allowed in the derelict.
        // We want to reduce chance of any chess piece to show up by a lot, for example
        // (ChessPitySys makes up for it with bad luck protection, keep its ChessCategory in sync)
        private static readonly Dictionary<string, float> categoryChances = new() {
            { "Chess", 0.3f },
            { "Furniture", 0.5f },
            { "UniqueDevice", 0.2f },
            { "RareDevice", 0.1f },
            { "Warp", 0.05f },
            { "Computing", 0.3f },
            { "Cosmetics", 1.1f },
            { "Research", 0.2f },
            { "Security", 0.4f },
            { "Production", 0.7f },
            { "Reactor", 0.3f },
            { "SolarPanel", 0.2f },
        };
        private static readonly Dictionary<string, float> categoryRepeatChances = new() {
            { "Furniture", 0.9f },
            { "Comfort", 0.9f },
            { "Cosmetics", 0.9f },
            { "Storage", 0.5f },
            { "Reactor", 0.3f },
            { "Medical", 0.7f },
            { "Robotics", 0.8f },
            { "Connector", 1.0f },
        };
        private static readonly Dictionary<string, float> categoryRepeatSameItemChances = new() {
            { "Furniture", 0.9f },
            { "Connector", 1.0f },
            { "Comfort", 0.9f },
            { "Cosmetics", 0.1f },
            { "Storage", 0.2f },
            { "Reactor", 0.1f },
            { "Medical", 0.1f },
            { "SolarPanel", 0.1f },
            { "Robotics", 0.1f },
        };
        public static void ChooseRandomDefs(GameState s, Rng rng, int count,
                int techLevel, Func<Def, bool> isDefUnlocked, Dictionary<Def, int> defs) {
            var maxTier = techLevel;
            if (maxTier == 0) {
                D.Err("Max GenSpawn tech tier is 0! Setting to 2");
                maxTier = 2;
            }
            candidates.Clear();
            foreach (var def in The.Defs.Defs.Values) {
                if (def.Spawnable == null) {
                    continue;
                }
                if (def.Spawnable.Chance == 0f
                        && def.Spawnable.ChanceLocked == 0f) {
                    continue;
                }
                if (!def.IsInSeason) { continue; }
                if (def.IsBeing || def.IsAbstract
                        || Researchable.TreeNode(def) || def.IsTemplate) {
                    continue;
                }
                if (def.Tier - def.Spawnable.TierDiff > maxTier) {
                    // 0 chance, too high tier
                    continue;
                }
                float chance;
                // Defs without research prerequisites are lootable-only and can never be
                // unlocked, so ChanceLocked is their real chance (otherwise it's dead config)
                if (!Researchable.IsEmptyPrereq(def) && isDefUnlocked(def)) {
                    chance = def.Spawnable.Chance;
                    if (chance <= 0f) { continue; }
                } else {
                    chance = def.Spawnable.ChanceLocked;
                    if (chance <= 0f) { continue; }
                }
                // scatter the chance a bit so it won't pick assembler all the fucking time
                candidates.Add(def, chance * rng.Range(0.9f, 1.1f));
            }

            int attempts = 1000;
            rng = rng.Fork();
            #if UNITY_EDITOR
            //D.Warn("Picking loot from candidates: {0}", D.Dump(candidates));
            #endif
            int addedCount = 0;
            var usedCategories = new HashSet<string>();
            while (attempts-- > 0 && addedCount < count) {
                if (rng.TryWeightedFrom(candidates, out var def)) {
                    var cat = def.Spawnable.Category;
                    bool remove = true;
                    if (cat != null && categoryRepeatSameItemChances.TryGetValue(cat, out var chanceKeep)) {
                        if (rng.Chance(chanceKeep)) {
                            remove = false;
                        }
                    }
                    if (remove && !candidates.Remove(def)) {
                        D.Err("Could not remove candidate: {0}", def.Id);
                    }
                    int q = 1;
                    if (def.Spawnable.Quantity?.Component != null) {
                        var check = GenSpawnChecks.Get(def.Spawnable.Quantity.Component);
                        if (check != null) {
                            q = check.Quantity(s, def, def.Spawnable.Quantity, rng);
                        }
                    }
                    if (q > 0) {
                        if (cat != null && categoryChances.TryGetValue(cat, out var chance)) {
                            if (!rng.Chance(chance)) {
                                // D.Warn("Skipping def with chance {0}: {1}", chance, def.Id);
                                continue;
                            }
                        }
                        if (def.Spawnable.SkipOwned > 0 && s.CountByDef(def) >= def.Spawnable.SkipOwned) {
                            continue;
                        }
                        if (cat == null || !categoryRepeatChances.TryGetValue(cat, out var repeatChance)) {
                            repeatChance = 0f;
                        }
                        // Don't add items from same category more than once
                        bool add = false;
                        if (cat == null) {
                            add = true;
                        } else {
                            // allow to repeat by not adding to used categories
                            if (repeatChance > 0f && rng.Chance(repeatChance)) {
                                add = true;
                            // if category wasn't used, let's add
                            } else if (usedCategories.Add(cat)) {
                                add = true;
                            }
                        }
                        if (add) {
                            if (defs.TryGetValue(def, out var dq)) {
                                defs[def] = dq + q;
                                addedCount++;
                            } else {
                                defs.Add(def, q);
                                addedCount++;
                            }
                        }
                    }
                } else {
                    if (addedCount == 0) {
                        D.Err("Failed getting any loot from weighted candidates. Blank input?");
                    //} else {
                        // D.Warn("Failed getting more loot from weighted candidates. Will return {0} defs", addedCount);
                    }
                    break;
                }
                // D.Warn("Picked loot attempts left of 1000: {0}", attempts);
            }
        }

    }
}
