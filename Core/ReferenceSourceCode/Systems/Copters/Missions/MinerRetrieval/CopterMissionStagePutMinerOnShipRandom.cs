#if UNITY_EDITOR
// #define DEBUG_RANDOM_PARK
#endif
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStagePutMinerOnShipRandom : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.PutMinerOnShipRandom, () => {
                return new CopterMissionStagePutMinerOnShipRandom();
            });
        }
        public int StageType => CopterMissionStageH.PutMinerOnShipRandom;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            var def = CopterMissionUtils.GetMinerDef(data);
            var near = Pos.IsOOB(data.Miner.PosOnShip) || data.Miner.PosOnShip == 0
                ? EntityUtils.ToPos(copter.Obj.Position) : data.Miner.PosOnShip;
            if (S.Sys.Copters.TryFindDropSpot(data, def, data.Miner.RotOnShip, near,
                    out var dropSpot)) {
                D.Warn("Will put the miner down at {0}", Pos.String(dropSpot));
                outData = data;
                outData.Miner.PosOnShip = dropSpot;
                outData.SetStage(CopterMissionStageH.PutMinerOnShip);
                return true;
            }
            // The search covers the whole map, so when nothing fits, the copter hovers where
            // it is until room frees up, instead of flying to spots that cannot fit it either
            outData = default;
            return false;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageRandomPark;
        }
    }
}
