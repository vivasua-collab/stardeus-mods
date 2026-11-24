using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Space {
    [MessagePackObject]
    public class SpaceRegionData {
        [Key("Id")] public int Id;
        [Key("Seed")] public int Seed;
        [Key("Name")] public string Name;
        [Key("Position")] public Vector2 Position;
        [Key("Biome")] public SpaceBiomeData Biome;
        [Key("SOId")] public int SOId;

        public static SpaceRegionData Serialize(SpaceRegion reg) {
            var d = new SpaceRegionData();
            d.Id = reg.Id;
            d.Seed = reg.Seed;
            d.Name = reg.Name;
            d.Position = reg.Position;
            d.Biome = SpaceBiomeData.Serialize(reg.Biome);
            d.SOId = reg.SO.Id;
            return d;
        }

        public static SpaceRegion Deserialize(GameState s, SpaceMap map, SpaceRegionData data,
                SpaceSectorData[] allSectors) {
            var r = new SpaceRegion();
            r.Id = data.Id;
            r.Map = map;
            r.Seed = data.Seed;
            r.Name = data.Name;
            r.Position = data.Position;
            r.SO = s.Universe.Find(data.SOId);
            r.SO.Region = r;
            r.Biome = SpaceBiomeData.Deserialize(data.Biome);
            // links have to be established after all regions are deserialized

            r.Sectors = new List<SpaceSector>();
            foreach (var sec in allSectors) {
                if (sec.RegionId == r.Id) {
                    var sector = SpaceSectorData.Deserialize(s, sec);
                    sector.Region = r;
                    r.Sectors.Add(sector);
                }
            }
            return r;
        }

    }
}
