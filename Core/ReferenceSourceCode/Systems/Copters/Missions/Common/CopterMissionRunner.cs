#if UNITY_EDITOR
// #define DEBUG_MISSIONS
#endif
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Unity.Burst;

namespace Game.Systems.Copters {
    public struct CopterMissionRunner {
        public static void OnTickProgress(GameState S,
                List<CopterMissionData> missions, int i) {
            var deployment = missions[i];
            if (deployment.IsDone || deployment.IsError) { return; }
            if (!CopterMissionStages.TryFind(deployment.Stage, out var stage)) {
                missions.RemoveAt(i);
                return;
            }
            #if DEBUG_MISSIONS
            D.Log("Miner deployment TickProgress stage {0} on {1}", stage, deployment);
            #endif
            stage.TickProgress(S, deployment);
        }

        // Return true if any state has changed
        public static bool OnTick(GameState S, long ticks,
                List<CopterMissionData> missions, int i) {
            var mission = missions[i];
            if (mission.IsDone) {
                #if DEBUG_MISSIONS
                D.Warn("CopterMission completed! {0}", mission);
                #endif
                // TODO achievements. stats.
            } else if (mission.IsError) {
                var lastSt = mission.LastStage;
                mission.Stage = lastSt;
                var stageTitle = CopterMissionUtils.MissionTitleFor(S, in mission);
                if (CopterMissionUtils.GetSiteSO(S, in mission, out var siteSO)) {
                    S.Sys.Log.AddLine($"{T.OngoingCopterMissionTip}: {siteSO.StarmapLink}: {Texts.Yellow(stageTitle)}: {Texts.Red(T.OperationFailed)}");
                } else {
                    S.Sys.Log.AddLine($"{T.OngoingCopterMissionTip}: {Texts.Yellow(stageTitle)}: {Texts.Red(T.OperationFailed)}");
                }
                D.Err("CopterMission had an error! {0}", mission);
                if (CopterMissionUtils.TryRecoverFailed(S, mission, out var recovered)) {
                    missions[i] = recovered;
                    D.Warn("Mission after recovery: {0}", recovered);
                    return true;
                } else {
                    D.Warn("Mission recovery failed: {0}", mission);
                }
            } else if (CopterMissionStages.TryFind(mission.Stage, out var stage)) {
                #if DEBUG_MISSIONS
                D.Warn("CopterMission Tick stage {0} on {1}", stage, mission);
                #endif
                if (stage.TryTick(S, ticks, mission, out var updated)) {
                    missions[i] = updated;
                    return true;
                }
                return false;
            }
            missions.RemoveAt(i);
            S.Sig.CopterMissionEnded.Send(mission);
            return true;
        }
    }
}
