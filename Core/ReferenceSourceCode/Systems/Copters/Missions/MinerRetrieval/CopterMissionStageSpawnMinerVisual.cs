using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageSpawnMinerVisual : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.SpawnMinerVisual, () => {
                return new CopterMissionStageSpawnMinerVisual();
            });
        }
        public int StageType => CopterMissionStageH.SpawnMinerVisual;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            outData = data;
            var minerDef = The.Defs.Get(data.Miner.MinerDefId);
            // We are near and ready
            if (!copter.PickupDef(minerDef)) {
                // something went wrong?
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            outData.SetStage(CopterMissionStageH.PutMinerOnShip);
            return true;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return null;
        }
    }
}
