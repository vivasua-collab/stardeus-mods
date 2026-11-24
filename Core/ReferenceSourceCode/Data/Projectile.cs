using System;
using Game.Constants;
using Game.VFXR;
using KL.Utils;
using MessagePack;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public sealed class Projectile {
        [Key("TypeId")] public string TypeId;
        [Key("Origin")] public Vector2 Origin;
        [Key("Velocity")] public Vector2 Velocity;
        [Key("Position")] public Vector2 Position;
        [Key("LastPosition")] public Vector2 LastPosition;
        [Key("LastImpactTest")] public Vector2 LastImpactTestPosition;
        [Key("Lifetime")] public float Lifetime;
        [Key("Damage")] public float Damage;
        [Key("Range")] public float Range; // When bullet will have to stop
        [Key("Distance")] public float Distance; // How much distance bullet already traveled
        [Key("FactionId")] public int FactionId;
        [Key("ShooterId")] public int ShooterId;
        [Key("TargetId")] public int TargetId;
        [IgnoreMember] public Vector2 ImpactPosition;
        [IgnoreMember] public bool IsActive;
        [IgnoreMember] public bool HadImpact;
        [IgnoreMember] public bool NeedsImpactCheck;
        [IgnoreMember] public bool HasReachedDistance => Distance > Range;
        [IgnoreMember] public Entity Shooter;
        [IgnoreMember] public Entity Target;
        [IgnoreMember] public int LastImpactTestPosIdx;
        [IgnoreMember] public ParticleSlot Slot;
        [IgnoreMember] public INamedEntity ShooterName {
            get {
                if (Shooter is Tile t) {
                    return t;
                } else if (Shooter is Being b) {
                    return b;
                } else {
                    return NamedEntity.For(T.Unknown);
                }
            }
        }

        public void MoveTo(Vector2 newPos) {
            LastPosition = Position;
            Position = newPos;
            Distance += Vectors.Distance(LastPosition, newPos);
        }

        public override string ToString() {
            return $"Projectile[{TypeId},{Shooter}->{Target},{Position}]";
        }

        public float2 LerpedPosition(float tickProgress) {
            return Vector2.Lerp(LastPosition, Position, tickProgress);
        }
    }
}
