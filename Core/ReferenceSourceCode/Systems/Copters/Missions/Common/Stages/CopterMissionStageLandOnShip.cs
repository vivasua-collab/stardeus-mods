using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public struct CopterMissionStageLandOnShip : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterLandOnShip, () => {
                return new CopterMissionStageLandOnShip();
            });
        }
        public int StageType => CopterMissionStageH.CopterLandOnShip;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            var copterTarget = EntityUtils.CenterOf(data.Copter.ParkedPos);
            if (copter.IsNear(copterTarget)) {
                var def = The.Defs.TryGet(data.Copter.CopterDefId);
                if (def == null) {
                    // fallback
                    def = The.Defs.FastGet(DefIdsH.ObjectsDevicesCargoCopter);
                }

                outData = data;
                if (copter.TryLand(data.Copter.ParkedPos,
                        data.Copter.ParkedRot, def, out var copterComp)) {
                    if (data.Copter.StackSize > 0) {
                        var matType = MatType.FastGet(data.Copter.MatTypeIdH);
                        if (matType != null) {
                            EntityUtils.SpawnRawMaterial(matType,
                                data.Copter.StackSize,
                                copterComp.Tile.Transform.OutPort,
                                0.15f, true);
                        }
                    }
                    outData.SetStage(CopterMissionStageH.Done);
                } else {
                    outData.SetStage(CopterMissionStageH.CopterRandomPark);
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
            var copterTarget = EntityUtils.CenterOf(data.Copter.ParkedPos);
            copter.MoveTowards(copterTarget);
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageLandOnShip;
        }
    }
}
