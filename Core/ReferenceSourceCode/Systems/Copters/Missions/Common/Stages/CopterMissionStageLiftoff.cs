using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public struct CopterMissionStageLiftoff : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterLiftoff, () => {
                return new CopterMissionStageLiftoff();
            });
        }
        public int StageType => CopterMissionStageH.CopterLiftoff;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            var copterId = data.Copter.EntityId;
            var copter = S.Components.Find<CopterComp>(copterId);
            outData = data;
            if (copter == null) {
                outData = data;
                D.Err("Failed to find a parked copter by id: {0}. Mission: {1}",
                    copterId, data.Id);
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            // Grab this before the Tile was removed
            var parkedPos = copter.Tile.PosIdx;
            var parkedRot = copter.Tile.Transform.Rotation;
            if (copter.TryLiftoff(out var flyingCopter)) {
                outData.Copter.EntityId = flyingCopter.EntityId;
                outData.Copter.ParkedPos = parkedPos;
                outData.Copter.ParkedRot = parkedRot;
                outData.Copter.CopterDefId = copter.Tile.DefinitionId;
                if (data.Miner.EntityId > 0) {
                    outData.SetStage(CopterMissionStageH.CopterFlyToMiner);
                } else {
                    // is it a collect mission?
                    outData.SetStage(CopterMissionStageH.CopterLeaveShip);
                }
            } else {
                D.Err("Failed to lift off a copter: {0}", copter);
                copter.IsReserved = false;
                outData.SetStage(CopterMissionStageH.Error);
            }
            return true;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }

        public string TitleFor(GameState S, CopterMissionData data) {
            return null;
        }
    }
}
