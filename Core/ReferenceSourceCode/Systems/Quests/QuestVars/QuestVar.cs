using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Space;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Quests {
    public abstract class QuestVar {
        public delegate QuestVar Factory(GameState s, QuestVarDef def,
            QuestData data, int seed, bool isLoading);
        protected readonly GameState S;
        public readonly QuestVarDef Def;
        protected readonly QuestData data;
        protected ComponentConfig config => Def.Config;
        public readonly int Seed;
        protected QuestVar(GameState s, QuestVarDef def,
                QuestData data, int seed, bool isLoading) {
            S = s;
            this.data = data;
            Seed = seed;
            Def = def;
        }
        public override string ToString() {
            return $"QuestVar[{Def.Id}|{Def.Type}]";
        }

        public T GetValue<T>() {
            if (this is IQuestVar<T> varT) {
                return varT.Value;
            }
            throw new Exception($"QuestVar {Def.Id} is not IQuestVar<{typeof(T)}>! {this}");
        }

        /// <summary>
        /// Picks a target region using the var's CanBeVisited/MinRegionsAway/
        /// MinRegionDifficulty/MaxRegionDifficulty/SkipRegionsWithoutRelay/Var_Start config,
        /// starting from the player's region (or the resolved Var_Start region).
        /// </summary>
        protected SpaceRegion PickTargetRegion(Rng rng) {
            bool canBeVisited = config.GetBool(PropertyIdH.CanBeVisited, true);
            var minRegionsAway = config.GetInt(PropertyIdH.MinRegionsAway, 0);
            var minRegionDiff = config.GetFloat(PropertyIdH.MinRegionDifficulty, 0f);
            var maxRegionDiff = config.GetFloat(PropertyIdH.MaxRegionDifficulty, 1f);
            var skipRegionsWithoutRelay = config.GetBool(
                PropertyIdH.SkipRegionsWithoutRelay, false);
            var startVarId = config.GetString(PropertyIdH.Var_Start, null);
            SpaceRegion start = null;
            if (startVarId != null) {
                if (data.Variables.TryGetValue(Hashes.S(startVarId), out var startVar)) {
                    SpaceObject startVarSO = null;
                    if (startVar is IQuestVar<SpaceObject> svSO) {
                        startVarSO = svSO.Value;
                    }
                    if (startVarSO != null) {
                        start = SOUtils.RegionFor(startVarSO);
                    } else {
                        D.Err("StartVar {0} for var {1} has null value is not SpaceObject! | {2}",
                            startVar, this, data);
                    }
                } else {
                    D.Err("StartVar was not loaded yet or does not exist by id: {0}, for var {1}. Check var load order in quest def: {2}",
                        startVarId, this, data);
                }
            }
            start ??= A.PlayerSector.Parent.Region;
            return SOUtils.RandomAtDist(start, minRegionsAway, rng,
                canBeVisited: canBeVisited,
                minDiff: minRegionDiff,
                maxDiff: maxRegionDiff,
                skipRegionsWithoutRelay: skipRegionsWithoutRelay);
        }

        protected SpaceSector FindRandomSector(int seed) {
            const int maxAttempts = 5;
            var candidates = new Dictionary<SpaceSector, float>();
            // any qualifying sector, storm or not - used only if every attempt comes back stormy
            var fallback = new Dictionary<SpaceSector, float>();
            Rng rng = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++) {
                rng = new Rng(Hashes.Quick(seed, Primes.Prime014, attempt));
                var targetRegion = PickTargetRegion(rng);
                foreach (var sec in targetRegion.Sectors) {
                    if (sec == A.PlayerSector.Sector) { continue; }
                    if (sec.Biome.Template is not SectorTemplate secTpl) {
                        continue;
                    }
                    // relays, black holes, etc
                    if (!secTpl.MustHavePOIs) { continue; }
                    // more weight to unvisited sectors
                    var weight = sec.SO.IsVisited ? 0.5f : 1f;
                    // the same region can come up on more than one attempt
                    fallback.TryAdd(sec, weight);
                    // storm sectors are peaceful - quests must never send the player there
                    if (SpaceEffectMagneticStorm.SectorHasEffect(sec)) { continue; }
                    candidates.TryAdd(sec, weight);
                }
                if (candidates.Count > 0) { break; }
            }

            if (rng.TryWeightedFrom(candidates, out var sector)) {
                return sector;
            }
            if (rng.TryWeightedFrom(fallback, out sector)) {
                D.Warn("Only magnetic storm sectors available, using one anyway for {0}", this);
                return sector;
            }
            D.Err("Failed to find any potential sectors for signal, falling back to player sector {0}",
                this);
            return A.PlayerSector.Sector;
        }
    }
}
