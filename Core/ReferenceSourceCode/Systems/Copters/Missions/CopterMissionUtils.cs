using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Planets;
using Game.Data.Space;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.Copters {
    public static class CopterMissionUtils {
        public static string MissionTitleFor(GameState S, in CopterMissionData data) {
            if (data.IsDone) { return null; }
            if (data.IsError) { return T.Error; }
            if (CopterMissionStages.TryFind(data.Stage, out var stageImpl)) {
                var title = stageImpl.TitleFor(S, data);
                if (title == null) { return T.Preparing; }
                return title;
            }
            D.Err("Could not find mission impl for mission {0}", data);
            return T.Unknown;
        }
        public static bool GetCopterSO(GameState S, in CopterMissionData data,
                out SpaceObject copterSO) {
            copterSO = S.Universe.Find(data.Copter.SOId);
            if (copterSO == null) {
                D.Err("Copter SpaceObject not found by id! {0}. {1}",
                    data.Copter.SOId, data);
                return false;
            }
            return true;
        }
        public static bool GetSiteSO(GameState S, in CopterMissionData data,
                out SpaceObject siteSO) {
            siteSO = S.Universe.Find(data.TargetId);
            if (siteSO == null) {
                D.Err("Mining site SpaceObject not found by id! {0}. {1}",
                    data.TargetId, data);
                return false;
            }
            return true;
        }
        public static bool GetCopter(GameState S, in CopterMissionData data,
                out CopterFlyingComp copter) {
            if (data.Copter.EntityId == 0) {
                copter = null;
                return false;
            }
            copter = S.Components.Find<CopterFlyingComp>(data.Copter.EntityId);
            if (copter == null || !copter.Entity.IsActive) {
                if (copter == null || !copter.Entity.IsActive) {
                    D.Err("Active FlyingCopter not found by id! {0}. {1}",
                        data.Copter.EntityId, copter);
                }
                return false;
            }
            return true;
        }
        public static bool GetMiner(GameState S, in CopterMissionData data,
                out ResourceMinerComp miner) {
            miner = S.Components.Find<ResourceMinerComp>(data.Miner.EntityId);
            if (miner == null || !miner.Entity.IsActive) {
                D.Err("Active ResourceMiner not found by id! {0}. {1}",
                    data.Miner.EntityId, miner);
                return false;
            }
            return true;
        }
        public static Def GetMinerDef(in CopterMissionData data) {
            var defId = data.Miner.MinerDefId;
            if (defId == null) {
                D.Err("DefID not set in ResourceMinderData for mission: {0}", data);
                defId = DefIds.ObjectsDevicesAutodrillRig;
            }
            var def = The.Defs.TryGet(defId);
            if (def == null) {
                // fallback
                D.Err("Fallback! Did not find miner by def id {0}",
                    data.Miner.MinerDefId);
                def = The.Defs.FastGet(DefIdsH.ObjectsDevicesAutodrillRig);
            }
            return def;
        }
        public static string GetMinerTypeName(in CopterMissionData data) {
            var minerDefId = data.Miner.MinerDefId;
            var minerDef = The.Defs.TryGet(minerDefId);
            if (minerDef != null) {
                return minerDef.NameT;
            } else {
                D.Err("No miner def! {0}", minerDefId);
                return T.ObjectsDevicesAutodrillrig;
            }
        }

        public static string MissionDurationFor(in CopterMissionData mission) {
            return Units.TicksToTime(G.Ticks - mission.StartTick, true);
        }

        public static string MissionSummaryFor(GameState s, in CopterMissionData mission) {
            var title = MissionTitleFor(s, mission) ?? T.Unknown;
            return $"{title}: {MissionDurationFor(mission)}";
        }

        public static string MissionTooltipFor(GameState s, in CopterMissionData mission) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(T.OngoingCopterMissionTip);
            var target = s.Universe.Find(mission.TargetId);
            if (target != null) {
                string targetName;
                if (target.Parent != s.Sys.Ship.Player.Parent) {
                    targetName = target.ParentPath();
                } else {
                    targetName = target.StarmapLink;
                }
                sb.AppendFormat("\n - {0}: {1}", T.NounTarget, targetName);
            }
            if (!mission.Miner.IsEmpty) {
                var miner = GetMinerTypeName(mission);
                sb.AppendFormat("\n - {0}: {1}", T.Deploy, miner);
            } else {
                sb.AppendFormat("\n - {0}", T.VerbCollect);
            }
            if (mission.Copter.IsCarrying) {
                var mat = MatType.FastGet(mission.Copter.MatTypeIdH);
                if (mat != null) {
                    var amt = mission.Copter.StackSize;
                    if (amt > 1) {
                        sb.AppendFormat("\n - {0}: {1} x{2}",
                            T.CarryingObj, mat.Def.CodexLink, amt);
                    } else {
                        sb.AppendFormat("\n - {0}: {1}",
                            T.CarryingObj, mat.Def.CodexLink);
                    }
                }
            }
            return sb.ToString();
        }

        /// <returns>Recovery stage id</returns>
        public static bool TryRecoverFailed(GameState S, in CopterMissionData mission,
                out CopterMissionData recovered) {
            D.Warn("Recovering from copter mission failure: {0}", mission);
            recovered = mission;
            if (GetMiner(S, mission, out var miner)) {
                if (miner.Entity.IsActive && miner.IsReserved) {
                    D.Warn("Recovery found a reserved miner, unreserving: {0}",
                        miner);
                    miner.IsReserved = false;
                }
            }
            if (GetCopter(S, mission, out var copterFlying)) {
                if (copterFlying != null && copterFlying.Entity.IsActive) {
                    D.Warn("Recovery found a flying copter, will try to random park: {0}",
                        copterFlying);
                    recovered.SetStage(CopterMissionStageH.CopterLandOnShip);
                    return true;
                }
            } else if (GetCopterSO(S, mission, out var copterSO)) {
                if (copterSO != null) {
                    D.Warn("Recovery found a copter SO, will try to return to ship: {0}",
                        copterSO);
                    recovered.SetStage(CopterMissionStageH.CopterFlyToShip);
                    return true;
                }
            }
            D.Warn("Recovery failed to find a good state, will end mission: {0}",
                mission);
            return false;
        }
    }
}
