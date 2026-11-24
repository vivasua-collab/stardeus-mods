using KL.Grid;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Motion {
    [MessagePackObject]
    public struct Position {
        [Key("Idx")] public int Idx;
        [Key("Position")] public Vector2 Exact;
        // Moving towards this
        [SkipDumpInstance]
        [IgnoreMember] public Vector2 Next;
        [SkipDumpInstance]
        [IgnoreMember] public int NextIdx;
        [IgnoreMember] public bool IsValid => Exact.x != 0f && Exact.y != 0f;
        public override string ToString() {
            return $"Position[{Pos.String(Idx)}|{Exact}]";
        }
    }
}
