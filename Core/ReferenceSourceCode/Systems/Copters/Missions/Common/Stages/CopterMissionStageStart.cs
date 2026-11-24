using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public struct CopterMissionStageStart : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.Start, () => {
                return new CopterMissionStageStart();
            });
        }
        public int StageType => CopterMissionStageH.Start;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            // We have to see if there is an available copter
            var copter = CopterComp.FirstAvailable(S);
            if (copter == null) {
                // D.Warn("Copter not available for resource miner deployment mission. Waiting.");
                // if this is the only mission, it should fail!
                if (S.Sys.Copters.OngoingMissionCount == 1) {
                    // we're trying to start a mission and there are 0 available copters,
                    // let's abort this one
                    outData = data;
                    outData.SetStage(CopterMissionStageH.Error);
                    D.Err("Aborting the only copter mission without a copter");
                    return true;
                }
                outData = default;
                return false;
            }
            outData = data;
            outData.Copter = copter.MissionData;
            copter.IsReserved = true;
            outData.SetStage(CopterMissionStageH.CopterLiftoff);
            return true;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageStart;
        }
    }
}
