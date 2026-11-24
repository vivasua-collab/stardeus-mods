using Game.Constants;
using MessagePack;

namespace Game.Systems.Copters {
    // When AutodrillRig tile gets removed, we need to know what we had
    // there may have been upgrades or different model with different
    // efficiency, etc
    [MessagePackObject]
    public struct CopterData {
        // Before copter started flying off base, we need to know which one we picked
        [Key("EntityId")] public int EntityId;
        [Key("SOId")] public int SOId;
        [Key("ParkedPos")] public int ParkedPos;
        [Key("ParkedRot")] public Facing.Type ParkedRot;
        [Key("CopterDefId")] public string CopterDefId;
        [Key("Speed")] public float Speed;
        [Key("Size")] public float Size;
        [Key("Sprite")] public string Sprite;
        [Key("MatTypeId")] public int MatTypeIdH;
        [Key("StackSize")] public int StackSize;
        [IgnoreMember] public bool IsCarrying => MatTypeIdH > 0 && StackSize > 0;
    }
}
