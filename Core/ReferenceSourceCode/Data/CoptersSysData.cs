using Game.Systems.Copters;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class CoptersSysData {
        [Key("LastMissionId")] public int LastMissionId;
        [Key("Missions")] public CopterMissionData[] Missions;
    }
}
