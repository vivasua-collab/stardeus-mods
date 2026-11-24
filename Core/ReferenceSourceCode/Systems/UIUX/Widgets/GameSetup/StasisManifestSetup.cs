using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using KL.Randomness;

namespace Game.Systems.UIUX {
    // Rolls the stasis manifest on the Custom Start screen, so the player meets the sleepers
    // before the game starts; generation then puts exactly these people in the arrays
    // (ScenarioStasisAreaGenStage). Relationships are not rolled here: opinions need
    // registered ids, so they are dealt at generation from whoever is on the final list,
    // which also means any reroll gets fresh relationships for free.
    public static class StasisManifestSetup {
        public static void RollAll(GameSetupData data, Action<StartingBeingData> onClick) {
            data.StasisSleepers.Clear();
            var manifests = data.Scenario?.StasisManifests;
            if (manifests == null) { return; }
            var rng = Rng.Unseeded;
            var human = Species.Get(SpeciesIdH.Human);
            var petSpecies = new List<Species>();
            // One set for every array, so nobody on the ship shares a name
            var exceptNames = new HashSet<int>();
            foreach (var manifest in manifests) {
                var humans = rng.Range(manifest.MinHumans, manifest.MaxHumans + 1);
                var pets = rng.Range(manifest.MinPets, manifest.MaxPets + 1);
                var sleepers = new List<StartingBeingData>(humans + pets);
                for (int i = 0; i < humans; i++) {
                    sleepers.Add(NewSleeper(human, onClick, exceptNames));
                }
                for (int i = 0; i < pets; i++) {
                    var species = ManifestSys.RandomPetSpecies(rng, petSpecies);
                    if (species == null) { break; }
                    sleepers.Add(NewSleeper(species, onClick, exceptNames));
                }
                data.StasisSleepers[manifest] = sleepers;
            }
        }

        // A new person in the same chamber. A pet can come back as another kind of pet.
        public static void Reroll(GameSetupData data, StartingBeingData sleeper) {
            var exceptNames = new HashSet<int>();
            foreach (var list in data.StasisSleepers.Values) {
                foreach (var sb in list) {
                    if (sb == sleeper) { continue; }
                    var nameResult = sb.Data.Mind.Name.NameResult;
                    if (nameResult != null) {
                        exceptNames.Add(nameResult.Hash);
                    }
                }
            }
            if (sleeper.Species.IsPet) {
                var species = ManifestSys.RandomPetSpecies(Rng.Unseeded, new List<Species>());
                if (species != null) {
                    sleeper.Species = species;
                }
            }
            sleeper.Data = Generate(sleeper.Species, exceptNames);
        }

        public static int Count(GameSetupData data) {
            int count = 0;
            foreach (var list in data.StasisSleepers.Values) {
                count += list.Count;
            }
            return count;
        }

        private static StartingBeingData NewSleeper(Species species,
                Action<StartingBeingData> onClick, HashSet<int> exceptNames) {
            return new StartingBeingData {
                Species = species,
                IsSleeper = true,
                OnClick = onClick,
                Data = Generate(species, exceptNames),
            };
        }

        // The same kind of person ManifestSys generates for a seeded array
        private static BeingData Generate(Species species, HashSet<int> exceptNames) {
            var vars = new GenBeingVars(species, BeingType.StasisWakeup);
            vars.Body = FactionDef.Player.GenBodyVars;
            vars.ExceptNames = exceptNames;
            return BeingUtils.GenBeingWithInventory(vars, Rng.Unseeded, GenItemFlags.None);
        }
    }
}
