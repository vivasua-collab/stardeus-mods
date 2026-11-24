using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Copters {
    public struct CopterMissionStageRetrieveMiner : ICopterMissionStage {

        [SelfInit]
        private static void Register() {
            CopterMissionStages.Register(CopterMissionStageH.RetrieveMiner, () => {
                return new CopterMissionStageRetrieveMiner();
            });
        }
        public int StageType => CopterMissionStageH.RetrieveMiner;

        public bool TryTick(GameState S, long ticks, in CopterMissionData data,
                out CopterMissionData outData) {

            // Retrieval takes time
            if (Rng.UChance(0.95f)) {
                outData = default;
                return false;
            }

            outData = data;
            if (!TryRetrieveMiner(S, ref outData)) {
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            if (!CopterMissionUtils.GetCopterSO(S, in data, out var copterSO)) {
                outData.SetStage(CopterMissionStageH.Error);
                return true;
            }

            // var copterSpeed = S.Sys.Ship.SpeedInCurrentLayer
            //     * Tunable.Float(TunableKey.CopterSpeedBase)
            //         * data.Copter.Speed;
            // copterSO.Motion = SOMotionHoming.Create(copterSO, S.Universe.Player,
            //     copterSpeed);

            outData.SetStage(CopterMissionStageH.CopterFlyToShip);
            return true;
        }

        public bool TryRetrieveMiner(GameState S, ref CopterMissionData outData) {
            if (!CopterMissionUtils.GetSiteSO(S, outData, out var siteSO)) {
                return false;
            }
            siteSO.Data.RemoveFloat(SpaceObjectLooks.VarStarmapSizeMul);
            siteSO.Data.RemoveFloat(SpaceObjectLooks.VarStarfieldSizeMul);
            siteSO.Data.SetString(SpaceObjectLooks.VarSprite, IconId.CPickaxe);

            PlanetResource res;
            if (S.Sys.Planets.IsMinableResource(siteSO, out var idx, out var mt)) {
                if (!S.Sys.Planets.TryGetResourceForUpdate(siteSO, out idx, out res)) {
                    return false;
                }
            } else {
                return false;
            }

            // Collect unretrieved resource
            var toGet = Mathf.FloorToInt(res.Unretrieved);
            outData.Copter.StackSize = toGet;
            outData.Copter.MatTypeIdH = mt.IdH;
            res.Unretrieved = 0;

            // Remove the miner
            res.AutoMineSpeed = 0f;
            res.AutoMineMaxDepth = 0f;
            res.AutoMineEfficiency = 0f;
            res.UnretrievedMax = 0;

            var minerIdH = siteSO.Data.GetInt(ResourceMinerData.VarMinerType,
                DefIdsH.ObjectsDevicesAutodrillRig);

            var minerDef = The.Defs.FastGet(minerIdH);
            if (minerDef == null) {
                D.Err("Miner def not found by id hash: {0}", minerIdH);
                minerDef = The.Defs.FastGet(DefIdsH.ObjectsDevicesAutodrillRig);
            }
            // we need to construct MinerData and attach it to outData
            outData.Miner = new ResourceMinerData() {
                MinerDefId = minerDef.Id,
                PosOnShip = siteSO.Data.GetInt(ResourceMinerData.VarMinerPos, -1),
                RotOnShip = (Facing.Type) siteSO.Data.GetInt(ResourceMinerData.VarMinerRot, 0),
            };
            siteSO.Data.RemoveInt(ResourceMinerData.VarMinerType);
            siteSO.Data.RemoveInt(ResourceMinerData.VarMinerPos);
            siteSO.Data.RemoveInt(ResourceMinerData.VarMinerRot);

            S.Sys.Planets.UpdateResource(idx, res);
            S.Sig.SpaceObjectChanged.Send(siteSO);

            // if (UIShowing.Starmap && UIShowing.QuickChoice) {
            // TODO rebuild quick choice?
            // }
            return true;
        }


        public void TickProgress(GameState S, in CopterMissionData data) {
        }

        public string TitleFor(GameState S, CopterMissionData data) {
            return T.CopterMissionStageRetrieveMiner;
        }
    }
}
