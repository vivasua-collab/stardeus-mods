using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageDeployMiner : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.DeployMiner, () => {
                return new CopterMissionStageDeployMiner();
            });
        }
        public int StageType => CopterMissionStageH.DeployMiner;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            // Deployment takes time
            if (Rng.UChance(0.95f)) {
                outData = default;
                return false;
            }

            if (!TryDeployMiner(S, data)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }
            // Miner was already deployed, we don't carry it anymore
            outData.Miner = default;

            if (!CopterMissionUtils.GetCopterSO(S, in data, out var copterSO)) {
                outData = data;
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            // var copterSpeed = S.Sys.Ship.SpeedInCurrentLayer
            //     * Tunable.Float(TunableKey.CopterSpeedBase)
            //         * data.Copter.Speed;
            // copterSO.Motion = SOMotionHoming.Create(copterSO, S.Universe.Player,
            //     copterSpeed);

            outData = data;
            outData.SetStage(CopterMissionStageH.CopterFlyToShip);
            return true;
        }

        public bool TryDeployMiner(GameState S, in CopterMissionData data) {
            if (!CopterMissionUtils.GetSiteSO(S, data, out var siteSO)) {
                return false;
            }

            var sprite = data.Miner.Sprite;
            var size = data.Miner.Size;
            siteSO.Data.SetFloat(SpaceObjectLooks.VarStarmapSizeMul, size);
            siteSO.Data.SetFloat(SpaceObjectLooks.VarStarfieldSizeMul, size * 0.25f);
            siteSO.Data.SetString(SpaceObjectLooks.VarSprite, sprite);
            var minerDefId = data.Miner.MinerDefId;
            if (string.IsNullOrEmpty(minerDefId)) {
                minerDefId = DefIds.ObjectsDevicesAutodrillRig;
            }
            siteSO.Data.SetInt(ResourceMinerData.VarMinerType, Hashes.S(minerDefId));
            siteSO.Data.SetInt(ResourceMinerData.VarMinerPos, data.Miner.PosOnShip);
            siteSO.Data.SetInt(ResourceMinerData.VarMinerRot, (int) data.Miner.RotOnShip);
            if (!S.Sys.Planets.TryGetResourceForUpdate(siteSO, out var idx, out var res)) {
                return false;
            }
            res.AutoMineSpeed = data.Miner.Speed;
            res.AutoMineMaxDepth = data.Miner.MaxDepth;
            res.AutoMineEfficiency = data.Miner.Efficiency;
            res.UnretrievedMax = data.Miner.Storage;
            S.Sys.Planets.UpdateResource(idx, res);
            S.Sig.SpaceObjectChanged.Send(siteSO);
            The.AchievementTracker.Unlock(
                Achievements.AchievementAutodrillDeployed.AchievementId);
            // not reliable enough
            // if (UIShowing.Starmap && UIShowing.QuickChoice && siteSO.UI != null) {
            //     SpaceObjectUICtx.ShowQuickChoiceFor(siteSO, siteSO.UI);
            // }
            return true;
        }


        public void TickProgress(GameState S, in CopterMissionData data) {
        }

        public string TitleFor(GameState S, CopterMissionData data) {
            var title = CopterMissionUtils.GetMinerTypeName(data);
            return T.CopterMissionStageDeployMiner_V1.F(title);
        }
    }
}
