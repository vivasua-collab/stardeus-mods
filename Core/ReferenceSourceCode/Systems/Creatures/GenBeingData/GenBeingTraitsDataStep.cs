using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Mods;
using Game.Systems.Backstories;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.Systems.Traits;
using KL.Collections;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Creatures {
    public sealed class GenBeingTraitsDataStep : GenBeingDataStep {
        [SelfInit]
        private static void Register() {
            RegisterStep(new GenBeingTraitsDataStep());
        }
        public const string StepId = "Traits";
        public override string Id => StepId;
        /// <summary>
        /// Mind has to be generated first
        /// </summary>
        public override int Order => 5;
        private readonly Dictionary<TraitDef, float> weightedTraits = new();
        public override bool Generate(GameState S, BeingData data, Rng rng,
                GenBeingVars vars) {
            if (vars.BodyWithoutParts) {
                return true;
            }

            GenerateFor(S, data, rng);

            // 1. get the possible trait types
            if (!SpeciesTraitType.TryFor(data.Species, out var traitTypes)) {
                // this type of species won't have any traits
                return true;
            }

            weightedTraits.Clear();
            for (int i1 = traitTypes.Count - 1; i1 >= 0; i1--) {
                SpeciesTraitType tt = traitTypes[i1];
                if (tt.SkipChance > 0f && rng.Chance(tt.SkipChance)) {
                    continue;
                }
                var traits = TraitDef.ForType(tt.TraitType);
                if (traits == null) { continue; }
                for (int i = traits.Count - 1; i >= 0; i--) {
                    TraitDef trait = traits[i];
                    if (trait.IsRoleOnly) { continue; }
                    if (!trait.IsAvailableForBodyType(vars.BodyType)) {
                        continue;
                    }
                    if (!trait.IsAvailableForSex(vars.Sex)) {
                        continue;
                    }
                    if (!trait.IsAvailableForSpecies(vars.Species)) {
                        continue;
                    }
                    var rarity = trait.Rarity * tt.Rarity;
                    weightedTraits.Add(trait, rarity);
                }
            }

            foreach (var role in data.Mind.Experience.Roles.Values) {
                if (role.IncompatibleTraits != null) {
                    foreach (var tr in role.IncompatibleTraits) {
                        if (weightedTraits.Remove(tr)) {
                            // D.Warn("Removing incompatible trait: {0} for role {1}",
                            //     tr.Id, role.Id);
                        }
                    }
                }
            }

            if (data.Species.Traits != null) {
                foreach (var trId in data.Species.Traits) {
                    var tr = TraitDef.Get(trId);
                    if (tr == null) {
                        D.Err("Invalid trait id {0} in Species.Traits for {1}",
                            trId, data.Species.Id);
                        continue;
                    }
                    data.Mind.Traits.AddTrait(tr);
                    weightedTraits.Remove(tr);
                }
            }

            // Role traits are independent rolls on top of the random picks,
            // so Chance is a real probability (1.0 = guaranteed)
            foreach (var role in data.Mind.Experience.Roles.Values) {
                if (role.AddsTraits == null) { continue; }
                foreach (var tr in role.AddsTraits) {
                    var trait = tr.Trait;
                    if (trait == null) { continue; }
                    if (!trait.IsAvailableForBodyType(vars.BodyType)) {
                        continue;
                    }
                    if (data.Mind.Traits.HasTrait(trait.IdH)) { continue; }
                    if (IsIncompatibleWithRoles(data.Mind.Experience, trait)) {
                        continue;
                    }
                    if (HasRoleConflictWith(data.Mind.Experience, trait)) {
                        continue;
                    }
                    if (!rng.Chance(tr.Chance)) { continue; }
                    data.Mind.Traits.AddTrait(trait);
                    weightedTraits.Remove(trait);
                    if (trait.IncompatibleTraits != null) {
                        foreach (var inc in trait.IncompatibleTraits) {
                            weightedTraits.Remove(inc);
                        }
                    }
                }
            }

            var numTraits = rng.Range(1, 4);
            if (numTraits > weightedTraits.Count) {
                numTraits = weightedTraits.Count;
            }

            for (int i = 0; i < numTraits; i++) {
                if (rng.TryWeightedFrom(weightedTraits, out var trait)) {
                    weightedTraits.Remove(trait);
                    if (HasRoleConflictWith(data.Mind.Experience, trait)) {
                        continue;
                    }
                    data.Mind.Traits.AddTrait(trait);
                    if (trait.IncompatibleTraits != null) {
                        foreach (var tr in trait.IncompatibleTraits) {
                            // remove incompatible traits from the weighted traits
                            weightedTraits.Remove(tr);
                        }
                    }
                }
            }

            return true;
        }

        private static bool IsIncompatibleWithRoles(Experience bs, TraitDef trait) {
            foreach (var r in bs.Roles.Values) {
                if (r?.IncompatibleTraits == null) { continue; }
                foreach (var inc in r.IncompatibleTraits) {
                    if (inc == trait) { return true; }
                }
            }
            return false;
        }

        private bool HasRoleConflictWith(Experience bs, TraitDef trait) {
            if (trait.LovesJobs == null && trait.IncapableJobs == null) {
                return false;
            }
            Caches.IntSet.Clear();
            Caches.IntSet2.Clear();
            var incapable = Caches.IntSet;
            var loves = Caches.IntSet2;
            // D.Warn("Checking trait: {0}", D.DumpInstance(trait));
            foreach (var r in bs.Roles.Values) {
                if (r == null) { continue; }
                if (r.IncapableJobs != null) {
                    foreach (var j in r.IncapableJobs) {
                        incapable.Add(j.IdHash);
                    }
                }
                if (r.LovesJobs != null) {
                    foreach (var j in r.LovesJobs) {
                        loves.Add(j.IdHash);
                    }
                }
            }
            if (incapable.Count == 0 && loves.Count == 0) { return false; }
            if (trait.IncapableJobs != null) {
                foreach (var j in trait.IncapableJobs) {
                    if (loves.Contains(j.IdHash)) {
                        // D.Warn("Skipping conflicting incapable job {2} in trait: {0} after {1}",
                        //     trait.Id, D.Dump(bs.Roles.Values), j.Id);
                        return true;
                    }
                }
            }
            if (trait.LovesJobs != null) {
                foreach (var j in trait.LovesJobs) {
                    if (incapable.Contains(j.IdHash)) {
                        // D.Warn("Skipping conflicting loves job {2} in trait: {0} after {1}",
                        //     trait.Id, D.Dump(bs.Roles.Values), j.Id);
                        return true;
                    }
                }
            }
            return false;
        }

        private void GenerateFor(GameState S, BeingData data, Rng rng) {
            if (data.Mind.Traits == null) {
                data.Mind.Traits = BeingTraits.BlankFor(data.Mind);
            }
        }
    }
}
