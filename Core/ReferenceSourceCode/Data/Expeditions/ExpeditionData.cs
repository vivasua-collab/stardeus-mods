using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data.Space;
using Game.Systems.Quests;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class ExpeditionData {
        [Key("StageType")] public int StageType;

        [Key("ShuttleId")] public int ShuttleId;
        [Key("Status")] public string Status;

        [Key("CrewIds")] public uint[] CrewIds;

        [Key("TargetId")] public int TargetId;

        [Key("StartTick")] public long StartTick;
        [Key("StageStartTick")] public long StageStartTick;
        [Key("EndTick")] public long EndTick;
        [Key("Seed")] public int Seed;
        [Key("IsUnloading")] public bool IsUnloading;
        [Key("EventCount")] public int EventCount;
        [Key("StageData")] public ComponentData StageData;
        [Key("LootObj")] public List<string> LootObj;
        [Key("MiningOrders")] public ExpeditionMiningOrders MiningOrders;
        [Key("QuestPackageId")] public uint QuestPackageId;
        [Key("CrewInitSanity")] public float[] CrewInitialSanity;
    }
}
