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
using KL.Randomness;

namespace Game.Systems.Copters {
    public struct CopterMissionStageFlyToSite : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.CopterFlyToSite, () => {
                return new CopterMissionStageFlyToSite();
            });
        }
        public int StageType => CopterMissionStageH.CopterFlyToSite;


        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {
            if (!CopterMissionUtils.GetCopterSO(S, data, out var copterSO)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            if (!CopterMissionUtils.GetSiteSO(S, data, out var siteSO)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            if (SOUtils.CanShuttleReachInSpace(copterSO, siteSO, out var hasReached, 2f)) {
                if (hasReached) {
                    outData = data;
                    SwitchToNextStage(ref outData);
                    return true;
                }
            }
            outData = default;
            return false;
        }

        private static void SwitchToNextStage(ref CopterMissionData outData) {
            switch (outData.MissionType) {
                case CopterMissionType.DeployMiner: {
                        outData.SetStage(CopterMissionStageH.DeployMiner);
                        break;
                    }
                case CopterMissionType.CollectResource: {
                        outData.SetStage(CopterMissionStageH.CollectResource);
                        break;
                    }
                case CopterMissionType.RetrieveMiner: {
                        outData.SetStage(CopterMissionStageH.RetrieveMiner);
                        break;
                    }
                // Support for old saves
                case CopterMissionType.Undefined: {
                    if (outData.Miner.IsEmpty) {
                        outData.MissionType = CopterMissionType.CollectResource;
                        outData.SetStage(CopterMissionStageH.CollectResource);
                    } else {
                        outData.MissionType = CopterMissionType.DeployMiner;
                        outData.SetStage(CopterMissionStageH.DeployMiner);
                    }
                    break;
                }
            }
        }

        public void TickProgress(GameState S, in CopterMissionData data) {
            if (CopterMissionUtils.GetCopterSO(S, data, out var so)) {
                if (CopterMissionUtils.GetSiteSO(S, data, out var siteSo)) {
                    // var x = Rng2.Noise1D(siteSo.Seed * Primes.Prime004, (uint) siteSo.Seed);
                    // var y = Rng2.Noise1D(siteSo.Seed * Primes.Prime011, (uint) siteSo.Seed);
                    // var targetOffset = new Vector2(x, y).normalized * 0.1f * siteSo.Size;
                    var copterSpeed = S.Sys.Ship.SpeedInCurrentLayer
                        * Tunables.CopterSpeedBase * data.Copter.Speed;
                    SOUtils.MoveTowards(so, siteSo, Consts.V2Zero,
                        copterSpeed * S.Clock.TicksPerUpdate);
                }
            }
        }
        public string TitleFor(GameState S, CopterMissionData data) {
            if (CopterMissionUtils.GetSiteSO(S, data, out var siteSO)) {
                return T.CopterMissionStageFlyTo_V1.F(siteSO.StarmapLink);
            }
            return null;
        }
    }
}
