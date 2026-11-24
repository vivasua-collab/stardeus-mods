using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Traits {
    public sealed class SpeciesTraitType {
        private static readonly Dictionary<Species, List<SpeciesTraitType>> bySpecies = new();

        [SelfInit]
        private static void Init() {
            bySpecies.Clear();
        }
        public string Id;
        public Species Species;
        public TraitType TraitType;
        public string SpeciesType;
        public string TraitTypeId;
        /// <summary>
        /// Must be > 0.0 && <= 1.0. Closer to 1.0 = more common
        /// </summary>
        public float Rarity;
        /// <summary>
        /// Chance of someone of the species to not have any of these traits at all
        /// </summary>
        public float SkipChance;

        public static void LoadKnown() {
            if (bySpecies.Count != 0) { return; }
            var types = The.ModLoader.LoadObjects<SpeciesTraitType>("Config/Traits/SpeciesTypes");
            foreach (var s in types) {
                s.Species = Species.Get(s.SpeciesType);
                s.TraitType = TraitType.Get(s.TraitTypeId);
                if (s.Rarity == 0f) {
                    D.Err("Rarity not defined for species trait type: {0}. Falling back to 0.5",
                        s.Id);
                    s.Rarity = 0.5f;
                }
                if (!bySpecies.TryGetValue(s.Species, out var list)) {
                    list = new List<SpeciesTraitType>();
                    bySpecies[s.Species] = list;
                }
                list.Add(s);
            }
        }

        public static bool TryFor(Species species, out List<SpeciesTraitType> traitTypes) {
            return bySpecies.TryGetValue(species, out traitTypes);
        }
        public override string ToString() {
            return $"SpeciesTraitType[{SpeciesType}->{TraitTypeId}]";
        }
    }
}
