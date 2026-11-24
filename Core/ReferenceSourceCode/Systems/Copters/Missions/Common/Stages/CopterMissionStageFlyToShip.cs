using System;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageFlyToShip : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterFlyToShip, () => {
                return new CopterMissionStageFlyToShip();
            });
        }
        public int StageType => CopterMissionStageH.CopterFlyToShip;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            if (!CopterMissionUtils.GetCopterSO(S, data, out var copterSO)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            if (SOUtils.CanShuttleReachInSpace(copterSO, S.Universe.Player,
                    out var hasReached, 2f)) {
                if (hasReached) {
                    outData = data;
                    if (SpawnCopterOnShip(S, copterSO, out var spawned)) {
                        outData.Copter.EntityId = spawned.EntityId;
                        if (data.MissionType == CopterMissionType.RetrieveMiner) {
                            outData.SetStage(CopterMissionStageH.SpawnMinerVisual);
                        } else {
                            outData.SetStage(CopterMissionStageH.CopterLandOnShip);
                        }
                    } else {
                        outData.SetStage(CopterMissionStageH.Error);
                    }
                    return true;
                }
            }
            outData = default;
            return false;
        }

        private bool SpawnCopterOnShip(GameState S, SpaceObject copterSO,
                out CopterFlyingComp spawned) {
            // first spawn the Obj on ship, if that succeeded, remove the SO
            if (CopterFlyingComp.TrySpawnFlyingCopterOnMapEdge(S, out spawned)) {
                SOUtils.RemoveSO(copterSO);
                return true;
            }
            spawned = null;
            return false;
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
            if (CopterMissionUtils.GetCopterSO(S, data, out var so)) {
                var copterSpeed = S.Sys.Ship.SpeedInCurrentLayer
                    * Tunables.CopterSpeedBase * data.Copter.Speed;
                SOUtils.MoveTowards(so, S.Universe.Player, Consts.V2Zero,
                    copterSpeed * S.Clock.TicksPerUpdate);
            }
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageFlyToShip;
        }
    }
}
