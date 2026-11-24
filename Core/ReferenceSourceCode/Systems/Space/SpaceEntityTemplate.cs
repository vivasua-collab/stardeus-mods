using System;
using System.Collections.Generic;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Space {
    public abstract class SpaceEntityTemplate {
        public string Id;
        public int IdH;
        /// <summary>
        /// If QuantityCurve returns a positive integer, Rarity doesn't matter.
        /// Otherwise higher rarity = more popular option
        /// </summary>
        public float Rarity;
        public float[] DifficultyChance;
        public Curve DifficultyCurve => DifficultyChance;
        public float[] QuantityChance;
        public Curve QuantityCurve => QuantityChance;
        public float[] NumFactionsChance;
        public Curve NumFactionsCurve => NumFactionsChance;


        /// <summary>
        /// RegionTemplate Ids that is required to be assigned to a neighbor
        /// </summary>
        public string[] RequiredNeighbors;
        /// <summary>
        /// RegionTemplate Ids that cannot exist in a neighbor
        /// </summary>
        public string[] ForbiddenNeighbors;
        /// <summary>
        /// This faction will be force-assigned to own all sectors of the region
        /// </summary>
        public string ForceFaction;
        public float ForceDifficulty;
        /// <summary>
        /// Some of the sectors in this region must have the effects listed here
        /// </summary>
        public string[] ForceEffects;
        /// <summary>
        /// If MustHavePois is true, and this is true, there can't be deposits / POIs that imply life (plant fiber)
        /// </summary>
        public bool NoLife;


        /// <summary>
        /// 0 - can't be used as fallback
        /// 1 - can be used as non-final fallback (i.e. EmptySpace)
        /// 2 - can be used as final fallback (i.e. SpaceEvent)
        /// 3 - can be used as final fallback if no more options left (i.e. DerelictZone, TradeZone)
        /// </summary>
        public int FallbackLevel;

        public void AssignTo(ISpaceEntity ntt) {
            if (ntt is SpaceSector sec) {
                if (sec.Region.Biome.UnavailableTemplates?.Contains(this) == true) {
                    D.Err("Assigning template to sector that is forbidden in the region! {0} | {1}",
                        this, ntt);
                }
            }
            // D.Warn("Assigning template to: {0} -> {1}", Id, ntt);
            if (ntt.Biome.Template != null) {
                D.Err("Trying to re-assign biome template! from {0} to {1}",
                    ntt.Biome.Template, this);
            }
            ntt.Biome.SetTemplate(this);
            if (ForceDifficulty > 0f) {
                ntt.Biome.Difficulty = ForceDifficulty;
            }
            if (!Arrays.IsEmpty(ForceEffects)) {
                foreach (var effectId in ForceEffects) {
                    var cfg = SpaceEffectConfig.Get(effectId);
                    if (cfg != null) {
                        ntt.Biome.AddAssignable(cfg);
                    }
                }
            }
        }

        public abstract bool IsAllowedFor(ISpaceEntity ntt, Rng rng);

        public bool Matches(ISpaceEntity ntt, bool force, out float diffChance) {
            if (force) {
                diffChance = 1f;
            } else if (!Arrays.IsEmpty(DifficultyChance)) {
                diffChance = DifficultyCurve.Evaluate(ntt.Biome.Difficulty);
                if (diffChance < Maths.Epsilon) {
                    return false;
                }
            } else {
                diffChance = 0f;
                return false;
            }
            // check if any of the neighbors are forbidden
            if (!Arrays.IsEmpty(ForbiddenNeighbors)) {
                foreach (var link in ntt.Links) {
                    var n = link.OtherSideOf(ntt);
                    if (n is SpaceRegion nr && nr.Biome.Template != null) {
                        if (Arrays.Contains(ForbiddenNeighbors, nr.Biome.Template.Id)) {
                            return false;
                        }
                    }
                }
            }
            // check if this Id is forbidden in any of the neighbors
            foreach (var link in ntt.Links) {
                var n = link.OtherSideOf(ntt);
                if (n is SpaceRegion nr && nr.Biome.Template != null) {
                    if (!Arrays.IsEmpty(nr.Biome.Template.ForbiddenNeighbors)) {
                        if (Arrays.Contains(nr.Biome.Template.ForbiddenNeighbors, Id)) {
                            return false;
                        }
                    }
                }
            }
            if (!Arrays.IsEmpty(RequiredNeighbors)) {
                bool found = false;
                foreach (var link in ntt.Links) {
                    var n = link.OtherSideOf(ntt);
                    if (n is SpaceRegion nr && nr.Biome.Template != null) {
                        if (Arrays.Contains(RequiredNeighbors, nr.Biome.Template.Id)) {
                            found = true;
                            break;
                        }
                    }
                }
                if (!found) {
                    return false;
                }
            }
            return true;
        }
    }
}
