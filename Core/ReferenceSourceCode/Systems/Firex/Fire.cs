using System;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using MessagePack;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Firex {
    [MessagePackObject]
    public struct Fire {
        [Key("Idx")] public int Idx;
        [IgnoreMember] public bool IsValid => EntityId != 0;
        // This is set by the job, the job reads the 02 and reduces it, then
        // the FirexSys will have to apply it in after execute tick
        [IgnoreMember] public float O2;
        // Minimum temperature of the tile calculated from fire strength
        [IgnoreMember] public float Temp;
        // Wall fires will eat oxygen from the surroundings rather than from its own tile
        [IgnoreMember] public bool IsWall;
        [Key("EntityId")] public int EntityId;
        // Backlink to the flammable that is on fire
        [Key("FlamIdx")] public int FlamIdx;
        [Key("Ticks")] public int Ticks;
        // For dynamic fires on Beings and Obj
        [Key("CanMove")] public bool CanMove;
        [Key("Pos")] public int PosIdx;
        [Key("Position")] public Vector2 Position;
        // For multi tile devices and objects that are non-uniformly sized
        [Key("Size")] public float Size;
        // While FireStrength > 0, the object is on fire
        // > 1 = fire will split
        // 0f-1f = severity
        [Key("Strength")] public float Strength;
        [IgnoreMember] public float StrengthWithSize => Strength + Size;
        // If spread strength is high, fire will have a higher chance to spread
        [Key("SpreadStr")] public float SpreadStr;
        // If fire has fuel, it will burn it instead of O2
        // If fuel amt == 0, o2 will be consumed, until fire dies off
        [Key("FireFuelAmt")] public float FireFuelAmt;
        public static Fire CreateBalanced(GameState s, float str = 0.75f, float fuel = 0.0f) {
            if (s.Difficulty.Level < 1f) {
                str *= s.Difficulty.Level;
                if (str < 0.5f) {
                    str = 0.5f;
                }
            }
            return Create(str, fuel);
        }
        public static Fire Create(float str = 0.75f, float fuel = 0.0f) {
            return new Fire {
                Strength = str,
                FireFuelAmt = fuel,
            };
        }

        public Fire Spread(float str, float fuelAmtMul = 0.5f) {
            var newFire = Create(str);
            newFire.FireFuelAmt = FireFuelAmt * fuelAmtMul;
            return newFire;
        }

        public void ResizeFor(Vector2 entitySize) {
            if (entitySize.sqrMagnitude > 1f) {
                Size = (Maths.Max(entitySize.x, entitySize.y) - 1f) * 0.5f;
            } else {
                Size = 0f;
            }
        }

        public Fire Copy() {
            return new Fire() {
                Strength = Strength,
                FireFuelAmt = FireFuelAmt,
            };
        }

        public override string ToString() {
            #if !PRODUCTION_BUILD
            return D.DumpInstance(this, forLog: true);
            #else
            return $"Fire[{Idx}|Flam idx:{FlamIdx}|Entity:{EntityId}]";
            #endif
        }
    }
}
