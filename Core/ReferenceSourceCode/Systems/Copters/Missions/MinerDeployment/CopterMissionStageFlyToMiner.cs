using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageFlyToMiner : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterFlyToMiner, () => {
                return new CopterMissionStageFlyToMiner();
            });
        }
        public int StageType => CopterMissionStageH.CopterFlyToMiner;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
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
            if (copter.IsNear(miner.Tile.Position)) {
                copter.ClearMotionTarget();
                outData = data;
                outData.SetStage(CopterMissionStageH.CopterAttachMiner);
                return true;
            }
            outData = default;
            return false;

        }

        public void TickProgress(GameState S, in CopterMissionData data) {
            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                return;
            }
            if (!CopterMissionUtils.GetMiner(S, data, out var miner)) {
                return;
            }
            copter.MoveTowards(miner.Tile.Position);
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            var title = CopterMissionUtils.GetMinerTypeName(data);
            return T.CopterMissionStageFlyTo_V1.F(title);
        }
    }
}
