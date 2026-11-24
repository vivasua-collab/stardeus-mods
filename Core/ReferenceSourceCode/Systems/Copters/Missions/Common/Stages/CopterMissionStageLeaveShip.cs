using Game.CodeGen;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public struct CopterMissionStageLeaveShip : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterLeaveShip, () => {
                return new CopterMissionStageLeaveShip();
            });
        }
        public int StageType => CopterMissionStageH.CopterLeaveShip;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            if (!CopterMissionUtils.GetCopter(S, data, out var copter)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            if (copter.IsNearMapEdge()) {
                copter.ClearMotionTarget();
                if (!CopterMissionUtils.GetSiteSO(S, data, out var siteSO)) {
                    outData = data;
                    outData.SetStage(CopterMissionStageH.Error);
                    return true;
                }
                // This will spawn the copter in the right place, even if the player
                // changes the scope of the starmap
                Vector2 spawnPos;
                if (siteSO.Parent == S.Universe.Player.Parent) {
                    spawnPos = S.Universe.Player.Location
                        + (S.Rng.InsideUnitCircle().normalized
                            * S.Rng.Range(0.01f, 0.02f));
                } else {
                    // Player is somewhere else
                    spawnPos = siteSO.Location
                        + (S.Rng.InsideUnitCircle().normalized
                            * S.Rng.Range(0.1f, 0.2f));
                }
                var so = copter.PutOnStarmap(siteSO.Parent, spawnPos);
                outData = data;
                if (!CopterMissionUtils.GetSiteSO(S, data, out var targetSO)) {
                    outData.SetStage(CopterMissionStageH.Error);
                    return true;
                }
                // so.Motion = SOMotionHoming.Create(so, targetSO, copterSpeed);
                outData.Copter.EntityId = 0;
                outData.Copter.SOId = so.Id;
                outData.SetStage(CopterMissionStageH.CopterFlyToSite);
                return true;
            }
            outData = default;
            return false;

        }

        public void TickProgress(GameState S, in CopterMissionData data) {
            if (!CopterMissionUtils.GetCopter( S, data, out var copter)) {
                return;
            }
            copter.MoveTowardsMapEdge();
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageLeaveShip;
        }
    }
}
