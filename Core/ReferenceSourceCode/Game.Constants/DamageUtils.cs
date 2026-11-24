using Game.Constants;
using KL.Randomness;

namespace Game.Constants {
    public static class DamageUtils {
        private static readonly DamageType[] envDamageRobo = new[] {
            DamageType.Heat,
            DamageType.Acid,
            DamageType.Water,
            DamageType.Energy,
        };
        private static readonly DamageType[] envDamageBio = new[] {
            DamageType.Cold,
            DamageType.Heat,
            DamageType.Acid,
            DamageType.Malnutrition,
        };
        public static readonly DamageType[] CombatDamageTypes = new[] {
            DamageType.Blunt,
            DamageType.Sharp,
            DamageType.Piercing,
            DamageType.Energy,
            DamageType.Explosive,
        };
        private static readonly DamageType[] physicalDamage = new[] {
            DamageType.Blunt,
            DamageType.Sharp,
            DamageType.Piercing,
        };
        public static DamageType RandomPhysical(Rng rng) {
            return rng.From(physicalDamage);
        }
        public static DamageType RandomCombat(Rng rng) {
            return rng.From(CombatDamageTypes);
        }
        public static DamageType RandomEnv(Rng rng, bool isBio) {
            if (isBio) {
                return rng.From(envDamageBio);
            } else {
                return rng.From(envDamageRobo);
            }
        }
        public static bool IsPhysical(DamageType type) {
            switch (type) {
                case DamageType.Blunt:
                case DamageType.Sharp:
                case DamageType.Energy:
                case DamageType.Explosive:
                case DamageType.Piercing: {
                    return true;
                }
                default: return false;
            }
        }

        public static bool CanCauseFire(DamageType type) {
            switch (type) {
                case DamageType.Blunt:
                case DamageType.Sharp:
                case DamageType.Energy:
                case DamageType.Explosive:
                case DamageType.Piercing: {
                    return true;
                }
                default: return false;
            }
        }

        public static bool CanExtinguishFire(DamageType type) {
            switch (type) {
                case DamageType.Water:
                case DamageType.Cold: return true;
                default: return false;
            }
        }
    }
}
