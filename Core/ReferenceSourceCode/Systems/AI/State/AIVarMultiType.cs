using Game.CodeGen;
using Game.Constants;
using KL.Utils;
using MessagePack;
using Unity.Burst;

namespace Game.Systems.AI {
    [MessagePackObject]
    public struct AIVarMultiType {
        public enum Kind : byte {
            Undefined,
            Bool,
            Int,
            Float
        }
        [Key("K")] public int Key;
        [Key("T")] public Kind Type;
        [Key("I")] public int Int;
        [Key("B")] public bool Bool;
        [Key("F")] public float Float;
        [IgnoreMember] public object ObjectValue {
            get {
                switch (Type) {
                    case Kind.Bool: return Bool;
                    case Kind.Int: return Int;
                    case Kind.Float: return Float;
                    default: return null;
                }
            }
        }
        public override string ToString() {
            switch (Type) {
                case Kind.Bool: return $"B:{AIVarsH.ResolveName(Key)}:{Bool}";
                case Kind.Int: return $"I:{AIVarsH.ResolveName(Key)}:{Int}";
                case Kind.Float: return $"F:{AIVarsH.ResolveName(Key)}:{Float}";
                default: return null;
            }
        }
        public static AIVarMultiType Create(int key, bool v) {
            return new AIVarMultiType {
                Key = key,
                Type = Kind.Bool,
                Bool = v,
            };
        }
        public static AIVarMultiType Create(int key, int v) {
            return new AIVarMultiType {
                Key = key,
                Type = Kind.Int,
                Int = v,
            };
        }
        public static AIVarMultiType Create(int key, float v) {
            return new AIVarMultiType {
                Key = key,
                Type = Kind.Float,
                Float = v,
            };
        }
        public static bool AreNotEqual(AIVarMultiType a, AIVarMultiType b) {
            if (a.Key != b.Key) { return true; }
            if (a.Type != b.Type) { return true; }
            switch (a.Type) {
                case Kind.Bool: return a.Bool != b.Bool;
                case Kind.Int: return a.Int != b.Int;
                case Kind.Float: return !Maths.Approximately(a.Float, b.Float);
            }
            return true;
        }

        public static bool AreEqual(AIVarMultiType a, AIVarMultiType b) {
            if (a.Key != b.Key) { return false; }
            if (a.Type != b.Type) { return false; }
            switch (a.Type) {
                case Kind.Bool: return a.Bool == b.Bool;
                case Kind.Int: return a.Int == b.Int;
                case Kind.Float: return Maths.Approximately(a.Float, b.Float);
            }
            return false;
        }

    }
}
