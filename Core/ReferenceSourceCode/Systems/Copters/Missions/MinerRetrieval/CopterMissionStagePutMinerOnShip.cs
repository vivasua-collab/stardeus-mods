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
    public struct CopterMissionStagePutMinerOnShip : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.PutMinerOnShip, () => {
                return new CopterMissionStagePutMinerOnShip();
            });
        }
        public int StageType => CopterMissionStageH.PutMinerOnShip;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            if (Pos.IsOOB(data.Miner.PosOnShip) || data.Miner.PosOnShip == 0) {
                outData = data;
                outData.SetStage(CopterMissionStageH.PutMinerOnShipRandom);
                return true;
            }

            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            var minerTarget = EntityUtils.CenterOf(data.Miner.PosOnShip);
            if (copter.IsNear(minerTarget)) {
                var def = CopterMissionUtils.GetMinerDef(data);
                outData = data;
                if (copter.TryPutDown(def, data.Miner.PosOnShip, data.Miner.RotOnShip)) {
                    outData.SetStage(CopterMissionStageH.CopterLandOnShip);
                } else {
                    outData.SetStage(CopterMissionStageH.PutMinerOnShipRandom);
                }
                return true;
            } else {
                outData = default;
                return false;
            }
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                return;
            }
            var copterTarget = EntityUtils.CenterOf(data.Miner.PosOnShip);
            copter.MoveTowards(copterTarget);
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageLandOnShip;
        }
    }
}
