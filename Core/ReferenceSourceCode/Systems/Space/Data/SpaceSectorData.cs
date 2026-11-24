using Game.Data;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceSectorData {
        [Key("Id")] public int Id;
        [Key("RegionId")] public int RegionId;
        [Key("Seed")] public int Seed;
        [Key("Name")] public string Name;
        [Key("Position")] public Vector2 Position;
        [Key("Biome")] public SpaceBiomeData Biome;
        [Key("SOId")] public int SOId;

        public static SpaceSectorData Serialize(SpaceSector sec) {
            var d = new SpaceSectorData();
            d.Id = sec.Id;
            d.RegionId = sec.Region.Id;
            d.Name = sec.Name;
            d.Seed = sec.Seed;
            d.Position = sec.Position;
            d.Biome = SpaceBiomeData.Serialize(sec.Biome);
            d.SOId = sec.SO.Id;
            return d;
        }

        public static SpaceSector Deserialize(GameState s, SpaceSectorData data) {
            var sec = new SpaceSector();
            sec.Id = data.Id;
            sec.Seed = data.Seed;
            sec.Name = data.Name;
            sec.Position = data.Position;
            sec.SO = s.Universe.Find(data.SOId);
            sec.Biome = SpaceBiomeData.Deserialize(data.Biome);
            sec.SO.Sector = sec;
            return sec;
        }
    }
}
