using System.Collections.Generic;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public sealed class ShipWeaponSettingsData {
        [Key("FireMode")] public ShipWeaponFireMode FireMode;

        [Key("ShootFluxThreshold")] public float ShootFluxThreshold;

        [Key("AutoTargetPriorities")]
        public Dictionary<string, int> AutoTargetPriorities;

        public static ShipWeaponSettingsData Serialize(ShipWeaponSettings settings) {
            if (settings == null) { return null; }
            return new ShipWeaponSettingsData {
                FireMode = settings.FireMode,
                ShootFluxThreshold = settings.ShootFluxThreshold,
                AutoTargetPriorities = SerializePriorities(settings.AutoTargetPriorities),
            };
        }

        public static ShipWeaponSettings Deserialize(ShipWeaponSettingsData data) {
            if (data == null) { return null; }
            return new ShipWeaponSettings {
                FireMode = data.FireMode,
                ShootFluxThreshold = data.ShootFluxThreshold,
                AutoTargetPriorities = DeserializePriorities(data.AutoTargetPriorities),
            };
        }

        private static Dictionary<string, int> SerializePriorities(Dictionary<ShipWeaponTargetTypeDef, int> priorities) {
            if (priorities == null) { return null; }
            var serialized = new Dictionary<string, int>();
            foreach (var kvp in priorities) {
                serialized[kvp.Key.Id] = kvp.Value;
            }
            return serialized;
        }

        private static Dictionary<ShipWeaponTargetTypeDef, int> DeserializePriorities(Dictionary<string, int> serialized) {
            if (serialized == null) { return null; }
            var deserialized = new Dictionary<ShipWeaponTargetTypeDef, int>();
            foreach (var kvp in serialized) {
                var targetTypeDef = ShipWeaponTargetTypeDef.Get(kvp.Key);
                if (targetTypeDef != null) {
                    deserialized[targetTypeDef] = kvp.Value;
                }
            }
            return deserialized;
        }
    }
}
