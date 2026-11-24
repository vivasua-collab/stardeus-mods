using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using MessagePack;

namespace Game.Systems.Copters {
    [MessagePackObject]
    public struct CopterMissionData {
        // Mission number, starts with 1
        [Key("Id")] public int Id;
        [Key("Copter")] public CopterData Copter;
        [Key("Miner")] public ResourceMinerData Miner;
        [Key("Type")] public CopterMissionType MissionType;
        /// <summary>
        /// Don't set this directly, use SetStage! OK in constructor only.
        /// </summary>
        [Key("Stage")] public int Stage;
        [Key("LastStage")] public int LastStage;
        public void SetStage(int stage) {
            if (stage == Stage) { return; }
            LastStage = Stage;
            Stage = stage;
        }
        // Target SpaceObject id
        [Key("TargetId")] public int TargetId;
        [Key("StartTick")] public long StartTick;
        [IgnoreMember] public bool IsDone => Stage == CopterMissionStageH.Done;
        [IgnoreMember] public bool IsError => Stage == CopterMissionStageH.Error;
        public override string ToString() {
            return $"CopterMissionData[Id:{Id}/{MissionType}/{CopterMissionStageH.ResolveName(Stage)}(prev: {CopterMissionStageH.ResolveName(LastStage)}),Target:{TargetId},Start:{Units.TicksAgoFromNow(StartTick)}))]";
        }
    }
}
