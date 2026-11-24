using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageAttachMiner : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterAttachMiner, () => {
                return new CopterMissionStageAttachMiner();
            });
        }
        public int StageType => CopterMissionStageH.CopterAttachMiner;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            // At this point the copter is supposed to be right above the miner
            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            if (!CopterMissionUtils.GetMiner(S, data, out var miner)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            // If we somehow are not near, let's fly to where we need to be
            if (!copter.IsNear(miner.Tile.Position)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.CopterFlyToMiner);
                return true;
            }

            outData = data;
            // Make sure mission data is latest
            outData.Miner = miner.MissionData;

            // We are near and ready
            if (!copter.PickupTile(miner.Tile)) {
                // something went wrong?
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            outData.Miner.EntityId = 0;
            outData.SetStage(CopterMissionStageH.CopterLeaveShip);
            return true;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return null;
        }
    }
}
