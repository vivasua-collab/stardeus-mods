using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public struct CopterMissionStageRandomPark : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterRandomPark, () => {
                return new CopterMissionStageRandomPark();
            });
        }
        public int StageType => CopterMissionStageH.CopterRandomPark;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            var copterDef = The.Defs.TryGet(data.Copter.CopterDefId)
                ?? The.Defs.FastGet(DefIdsH.ObjectsDevicesCargoCopter);
            var near = Pos.IsOOB(data.Copter.ParkedPos) || data.Copter.ParkedPos == 0
                ? EntityUtils.ToPos(copter.Obj.Position) : data.Copter.ParkedPos;
            if (S.Sys.Copters.TryFindDropSpot(data, copterDef, data.Copter.ParkedRot, near,
                    out var parkSpot)) {
                D.Warn("Will park the copter at {0}", Pos.String(parkSpot));
                outData = data;
                outData.Copter.ParkedPos = parkSpot;
                outData.SetStage(CopterMissionStageH.CopterLandOnShip);
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
