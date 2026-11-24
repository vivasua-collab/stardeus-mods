using System;
using System.Collections.Generic;
using Game.Components;
using MessagePack;

namespace Game.Data.Space {
    [MessagePackObject]
    public class MapData {
        // Contains tiles and objects that exist in here
        [Key("LastId")] public int LastId;
        [Key("Width")] public int Width;
        [Key("Height")] public int Height;
        [Key("PlanData")] public int[] PlanData;
        //[Key("SectionsData")] public int[] SectionsData;
        [Key("HeatData")] public float[] HeatData;
        [Key("OxygenData")] public float[] OxygenData;
        [Key("InsulationData")] public float[] InsulationData;
        [Key("AirtightData")] public float[] AirtightData;
        [Key("TilesFloor")] public Tile[] TilesFloor;
        [Key("TilesWalls")] public Tile[] TilesWalls;
        [Key("TilesObjects")] public Tile[] TilesObjects;
        [Key("ExtraGridTiles")] public Dictionary<int, Tile[]> ExtraGridTiles = new();
        [Key("BeingsData")] public List<Being> BeingsData;
        [Key("ObjsData")] public List<Obj> ObjsData;
        [Key("ComponentsData")] public ComponentState.StateData ComponentsData;
        [Key("Situation")] public Situation Situation;
        [Key("SituationHistory")] public Dictionary<int, Situation> SituationHistory;

        public static MapData Serialize(Map map) {
            if (map == null) { return null; }
            return map.GetData();
        }

        public static Map Deserialize(MapData map) {
            if (map == null) { return null; }
            var m = new Map();
            m.SetData(map);
            return m;
        }
    }
}
