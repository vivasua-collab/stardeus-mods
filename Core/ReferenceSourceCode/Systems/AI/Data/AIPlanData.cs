using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Migrations;
using KL.Utils;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public sealed class AIPlanData {
        [Key("Ticks")] public long Ticks;
        [Key("Cost")] public float Cost;
        [Key("Step")] public int StepIdx;
        [Key("Steps")] public string[] StepIds;

        public static AIPlanData Serialize(AIPlan plan) {
            if (plan == null || plan.Steps == null) { return null; }
            var data = new AIPlanData();
            data.Cost = plan.Cost;
            data.StepIdx = plan.StepIdx;
            data.Ticks = plan.Ticks;
            if (plan.Steps.Length > 0) {
                data.StepIds = new string[plan.Steps.Length];
                for (int i = data.StepIds.Length - 1; i >= 0; i--) {
                    data.StepIds[i] = plan.Steps[i].Id;
                }
            }
            return data;
        }

        public static AIPlan Deserialize(AIPlanData data) {
            if (data == null) { return null; }
            var plan = new AIPlan();
            plan.Cost = data.Cost;
            plan.StepIdx = data.StepIdx;
            plan.Ticks = data.Ticks;
            plan.Steps = new AIAction[data.StepIds.Length];
            for (int i = 0; i < data.StepIds.Length; i++) {
                string id = data.StepIds[i];
                var act = Registries.AIActions.Get(id, errorIfMissing: false);
                if (act == null && Legacy.AIActions.TryGetValue(id, out var repId)) {
                    act = Registries.AIActions.Get(repId, errorIfMissing: true);
                }
                if (act != null) {
                    plan.Steps[i] = act;
                } else {
                    D.ErrSoft("Plan may be invalid for agent after load due to missing action: {0}",
                        id);
                    return null;
                }
            }
            return plan;
        }
    }
}
