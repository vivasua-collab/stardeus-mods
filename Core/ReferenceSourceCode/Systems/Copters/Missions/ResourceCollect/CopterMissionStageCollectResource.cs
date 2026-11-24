using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageCollectResource : ICopterMissionStage {
        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CollectResource, () => {
                return new CopterMissionStageCollectResource();
            });
        }
        public int StageType => CopterMissionStageH.CollectResource;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            // Collection takes time
            if (Rng.UChance(0.95f)) {
                outData = default;
                return false;
            }

            outData = data;
            if (!CopterMissionUtils.GetSiteSO(S, in data, out var siteSO)) {
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            if (!CopterMissionUtils.GetCopterSO(S, in data, out var copterSO)) {
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            if (S.Sys.Planets.IsMinableResource(siteSO, out var idx, out var mt)) {
                if (S.Sys.Planets.TryGetResource(idx, out var resource)) {
                    var toGet = Mathf.FloorToInt(resource.Unretrieved);
                    resource.Unretrieved = 0;
                    S.Sys.Planets.UpdateResource(idx, resource);
                    outData.Copter.StackSize = toGet;
                    outData.Copter.MatTypeIdH = mt.IdH;
                }
            }

            // var copterSpeed = S.Sys.Ship.SpeedInCurrentLayer
            //     * Tunables.CopterSpeedBase
            //         * data.Copter.Speed;
            // copterSO.Motion = SOMotionHoming.Create(copterSO, S.Universe.Player,
            //     copterSpeed);

            outData.SetStage(CopterMissionStageH.CopterFlyToShip);
            return true;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            if (CopterMissionUtils.GetSiteSO(S, in data, out var siteSO)) {
                if (S.Sys.Planets.TryGetResourceFor(siteSO, out _, out var mt)) {
                    return T.CopterMissionStageCollectResource_V1.F(mt.NameT);
                } else {
                    return T.Unknown;
                }
            }
            return T.Unknown;
        }
    }
}
