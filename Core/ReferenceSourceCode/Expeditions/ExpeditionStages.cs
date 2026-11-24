using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Migrations;
using KL.Collections;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Expeditions {
    public static class ExpeditionStages {
        [ExecuteOnReload]
        public static void CleanupOnExit() {
            stages.Clear();
        }

        private readonly static Dictionary<int, Func<IExpeditionStage>> stages = new();
        public static void Register(int stage, Func<IExpeditionStage> factory) {
            if (stages.TryGetValue(stage, out var existing)) {
                D.Err("Overwriting existing expedition stage! {0} -> existing: {1} -> new: {2}",
                    ExpeditionStageTypeH.ResolveName(stage), existing, factory);
            }
            stages[stage] = factory;
        }
        public static IExpeditionStage Find(int stage) {
            var factory = stages.Get(stage, null);
            if (factory == null) {
                factory = stages.Get(ExpeditionStageTypeH.PrepForReturn, null);
                return factory();
            }
            var instance = factory();
            if (instance.StageType != stage) {
                D.Err("Incorrect stage type returned by stage that is registered as {0}, reported: {1}: {2}",
                    ExpeditionStageTypeH.ResolveName(stage),
                    ExpeditionStageTypeH.ResolveName(instance.StageType),
                    instance);
            }
            return instance;
        }
    }
}
