using Game.Systems.Ships;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipWeaponStatsData {
        [Key("Shots")] public int Shots;
        [Key("Hits")] public int Hits;
        [Key("Misses")] public int Misses;

        public static ShipWeaponStatsData Serialize(ShipWeaponStats stats) {
            return new ShipWeaponStatsData {
                Shots = stats.Shots,
                Hits = stats.Hits,
                Misses = stats.Misses
            };
        }

        public static ShipWeaponStats Deserialize(ShipWeaponStatsData data) {
            if (data == null) {
                return default;
            }
            return new ShipWeaponStats(data.Shots, data.Hits, data.Misses);
        }
    }
}
