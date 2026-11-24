using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Conditions {
    [MessagePackObject]
    public struct ConditionStateData {
        [Key("CondId")] public int ConditionId;
        [Key("Severity")] public Severity Severity;
        [Key("Start")] public long StartTick;
        [Key("Duration")] public int Duration;
        [Key("Flags")] public ConditionFlags Flags;

        public static ConditionStateData[] Serialize(
                Dictionary<int, ConditionState> conditions) {
            if (conditions == null) { return null; }
            if (conditions.Count == 0) { return null; }
            var states = new ConditionStateData[conditions.Count];
            int i = 0;
            foreach (var c in conditions) {
                states[i++] = Serialize(c.Value);
            }
            return states;
        }

        public static ConditionStateData Serialize(ConditionState state) {
            if (state == null) { return default; }
            return new ConditionStateData {
                ConditionId = state.Condition.IdH,
                Duration = state.Duration,
                // Drop the RegisteredWithSystem flag when saving
                Flags = state.Flags & ~ConditionFlags.RegisteredWithSystem,
                Severity = state.Severity,
                StartTick = state.StartTick,
            };
        }

        public static ConditionState Deserialize(ConditionStateData data, BeingData b) {
            var cond = Registries.Conditions.FastGet(data.ConditionId);
            if (cond == null) {
                D.Err("Failed getting condition by id {0} ({1})",
                    data.ConditionId, ConditionIdH.ResolveName(data.ConditionId));
                return null;
            }
            var state = new ConditionState() {
                Owner = b,
                Condition = cond,
                Duration = data.Duration,
                Flags = data.Flags,
                Severity = data.Severity,
                StartTick = data.StartTick,
            };
            if ((state.Flags & ConditionFlags.RegisteredWithSystem) == ConditionFlags.RegisteredWithSystem) {
                state.Flags &= ~ConditionFlags.RegisteredWithSystem;
                D.Err("ConditionState has RegisteredWithSystem flag on load! Removing: {0}", state);
            }
            return state;
        }
    }
}
