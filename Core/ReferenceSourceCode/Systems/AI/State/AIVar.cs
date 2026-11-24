using System.Runtime.CompilerServices;
using Game.CodeGen;
using Game.Constants;
using MessagePack;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIVar {
        [Key("K")] public int Key;
        [Key("B")] public bool Bool;
        [IgnoreMember] public bool IsValid => Key != 0;
        public static AIVar Create(int key, bool v) {
            return new AIVar {
                Key = key,
                Bool = v,
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreNotEqual(AIVar a, AIVar b) {
            if (a.Key != b.Key) { return true; }
            return a.Bool != b.Bool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEqual(AIVar a, AIVar b) {
            if (a.Key != b.Key) { return false; }
            return a.Bool == b.Bool;
        }
        public override string ToString() {
            if (AIVarsH.HashToName.TryGetValue(Key, out var name)) {
                return $"{name}:{Bool}";
            } else {
                return $"UNKNOWN({Key}:{Bool})";
            }
        }
    }
}
