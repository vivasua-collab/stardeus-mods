using System;
using KL.Utils;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIStateData {
        [Key("Vars")] public AIVar[] Vars;
        [Key("VarCount")] public byte VarCount;
        public static AIState Deserialize(AIStateData data, bool mustExist) {
            if (data.VarCount > 0) {
                return AIState.Load(data.Vars, data.VarCount);
            }
            if (data.Vars == null) {
                // blank
                if (mustExist) {
                    return AIState.Create();
                } else {
                    return default;
                }
            } else {
                // legacy data migration for tests
                var vars = data.Vars;
                byte cnt = (byte) vars.Length;
                if (vars.Length < AIState.InitVarSize) {
                    Array.Resize(ref vars, AIState.InitVarSize);
                }
                return AIState.Load(vars, cnt);
            }
        }

        public static AIStateData Serialize(AIState state) {
            if (AIState.IsNotEmpty(state)) {
                var data = new AIStateData();
                var d = AIState.Save(state);
                data.Vars = d.Item1;
                data.VarCount = d.Item2;
                return data;
            }
            return default;
        }

        public override string ToString() {
            return $"AIStateData[VarCount:{VarCount}|{D.Dump(Vars)}]";
        }
    }
}
