using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Expeditions;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Copters {
    public static class CopterMissionStages {

        [ExecuteOnReload]
        public static void CleanupStatics() {
            stages.Clear();
        }

        public delegate ICopterMissionStage StageFactory();
        private readonly static Dictionary<int, StageFactory> stages = new();
        public static void Register(int stage, StageFactory factory) {
            if (stages.TryGetValue(stage, out var existing)) {
                D.Err("Overwriting existing CopterMission stage! {0} -> existing: {1} -> new: {2}",
                    CopterMissionStageH.ResolveName(stage), existing, factory);
            }
            stages[stage] = factory;
        }

        public static bool TryFind(int stage, out ICopterMissionStage impl) {
            if (!stages.TryGetValue(stage, out var factory)) {
                D.Err("ICopterMissionStage has no factory: {0} ({1})", stage,
                    CopterMissionStageH.ResolveName(stage));
                impl = null;
                return false;
            }
            impl = factory();
            if (impl.StageType != stage) {
                D.Err("ICopterMissionStage by id {0} returned unexpected stage that claims stage id is {1}. {2}",
                    CopterMissionStageH.ResolveName(stage),
                    CopterMissionStageH.ResolveName(impl.StageType),
                    impl);
                return false;
            }
            return true;
        }
    }
}
