using System.Collections.Generic;
using Game.Components;
using Game.Data.Economy;
using MessagePack;
using UnityEngine;

namespace Game.Data.Space {
    [MessagePackObject]
    public class SpaceObjectData {
        [Key("Id")] public int Id;
        [Key("TypeId")] public string TypeId;
        [Key("FactionId")] public string FactionId;
        [Key("TraderId")] public uint TraderId;
        [Key("FactionIds")] public string[] FactionIds;
        [Key("Name")] public string Name;
        [Key("IsSettled")] public bool IsSettled;
        [Key("Flags")] public SOFlags Flags;
        [Key("TerraformState")] public Dictionary<string, float> TerraformState;
        [Key("IsVisited")] public bool IsVisited;
        [Key("IsVisitedHS")] public bool IsVisitedHS;
        [Key("Layer")] public string Layer;
        [Key("Data")] public ComponentData Data;
        [Key("Parent")] public int ParentId;
        [Key("QuestId")] public uint QuestId;
        [Key("POIs")] public Dictionary<int, int> POIs;
        [Key("Knowledge")] public SOKnowledge Knowledge;
        [Key("Children")] public List<int> ChildrenIds;
        [Key("Location")] public Vector2 Location;
        [Key("MiningDifficulty")] public float MiningDifficulty;
        [Key("MatTypes")] public string[] MatTypes;
        [Key("Size")] public float Size;
        [Key("Map")] public MapData MapData;
    }
}
