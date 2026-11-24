using Game.Systems.AI;
using KL.Utils;

namespace Game.Systems.Creatures {
    // BeingData.AbilitySources is only registered when a being is spawned
    // (BodyPartSlot.AfterLoad / TraitSlot.AfterLoad), so ability-driven job slots
    // used to appear only after spawn and unspawned data (game setup crew preview)
    // listed fewer jobs than the being actually ended up with. This answers
    // "will the being have this ability once spawned" straight from the defs it
    // was generated with, mirroring what those AfterLoad calls register.
    public readonly struct GenBeingAbilities : IAbilityHolder {
        private readonly BeingData data;

        public GenBeingAbilities(BeingData data) {
            this.data = data;
        }

        public bool HasAbility(int abilityIdH) {
            var slots = data.Body?.Slots;
            if (slots != null) {
                // The mind species, with no fallback to the body - same rule as
                // BodyPartSlot.AddAbilitySources, which this mirrors.
                var species = data.Mind?.Species;
                for (int i = slots.Length - 1; i >= 0; i--) {
                    var slot = slots[i];
                    if (slot.IsPartRemoved || slot.InstalledDef == null) { continue; }
                    if (Adds(slot.InstalledDef.AddsAbilities, abilityIdH)) {
                        return true;
                    }
                    if (slot.HoldsMind && Adds(species?.AddsAbilities, abilityIdH)) {
                        return true;
                    }
                }
            }
            var traits = data.Mind?.Traits?.Slots;
            if (traits != null) {
                for (int i = traits.Count - 1; i >= 0; i--) {
                    if (Adds(traits[i].Trait.AddsAbilities, abilityIdH)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool Adds(string[] abilities, int abilityIdH) {
            if (abilities == null) { return false; }
            for (int i = abilities.Length - 1; i >= 0; i--) {
                if (Hashes.S(abilities[i]) == abilityIdH) {
                    return true;
                }
            }
            return false;
        }
    }
}
