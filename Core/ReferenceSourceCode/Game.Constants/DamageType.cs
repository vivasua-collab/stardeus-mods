using System;
using System.Collections.Generic;
using KL.Randomness;
using KL.Utils;

namespace Game.Constants {
    public enum DamageType : byte {
        None,
        // Bats, fists, etc
        // Leaves bruises, concussions if head is hit, can break bones
        // high amount of damage can damage internal organs
        Blunt,
        // Knives, blades, teeth => can cut / tier limbs off if high damage,
        // small damage does superficial bleeding, no damage to internal organs
        Sharp,
        // Bullets, daggers => can damage internal organs
        Piercing,
        // Electric shocks, energy rifles, laser weapons
        // Can fry circuits, can burn skin, high amount of energy can
        // do same stuff what piercing can do
        Energy,
        Explosive,
        Structural,
        Acid,
        Heat,
        Cold,
        Water,
        Organ,
        Malnutrition,
        Decay,
        // Organ damage from blood toxicity (ConditionToxemia). Its own type, because a slot
        // holds one wound per type and Organ wounds belong to Hypoxia and Hyperthermia
        Toxin,
    }

    public static class DamageTypes {
        // Only damage types that can scar (Scar.ChanceFor) - bruises never do
        private readonly static Dictionary<DamageType, float> weightedScarDamageTypes = new() {
            { DamageType.Acid, 0.1f },
            { DamageType.Energy, 0.15f },
            { DamageType.Explosive, 0.15f },
            { DamageType.Heat, 0.1f },
            { DamageType.Piercing, 0.2f },
            { DamageType.Sharp, 0.3f },
        };

        public static string NameFor(DamageType type) {
            switch (type) {
                case DamageType.None: return T.NotAvailable;
                case DamageType.Acid: return T.DamageTypeAcid;
                case DamageType.Blunt: return T.AttackTypeBlunt;
                case DamageType.Cold: return T.DamageTypeCold;
                case DamageType.Energy: return T.AttackTypeEnergy;
                case DamageType.Explosive: return T.DamageTypeExplosive;
                case DamageType.Heat: return T.DamageTypeHeat;
                case DamageType.Water: return T.DamageTypeWater;
                case DamageType.Structural: return T.DamageTypeStructural;
                case DamageType.Organ: return T.DamageTypeOrgan;
                case DamageType.Piercing: return T.AttackTypePiercing;
                case DamageType.Sharp: return T.AttackTypeSharp;
                case DamageType.Decay: return T.DamageTypeDecay;
                case DamageType.Malnutrition: return T.DamageTypeHunger;
                case DamageType.Toxin: return T.DamageTypeToxin;
            }
            D.Err("No name for DamageType: {0}", type);
            return type.ToString();
        }

        public static DamageType RandomForScar(Rng rng) {
            return rng.WeightedFrom(weightedScarDamageTypes);
        }
    }
}
