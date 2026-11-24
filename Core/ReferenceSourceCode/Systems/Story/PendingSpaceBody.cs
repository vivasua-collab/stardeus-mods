using Game.Rendering;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Story {
    [MessagePackObject]
    public sealed class PendingSpaceBody {
        [Key("Pos")] public Vector2 Pos;
        [Key("Vel")] public Vector2 Velocity;
        [Key("StV")] public Vector2 StrikeV;
        [Key("Def")] public string DefId;
        [Key("VM")] public float VelMul;
        [Key("Exp")] public long ExpireTick;
        [Key("IsA")] public bool IsAsteroid;
        [Key("Hit")] public Vector2 HitPosition;
        [IgnoreMember] public AnimatedSpriteSet FauxSprite;
    }
}
