using Game.Constants;
using MessagePack;

namespace Game.Systems.Copters {
    // When AutodrillRig tile gets removed, we need to know what we had
    // there may have been upgrades or different model with different
    // efficiency, etc
    [MessagePackObject]
    public struct ResourceMinerData {
        public const string VarMinerType = "MinerType";
        public const string VarMinerPos = "MinerPos";
        public const string VarMinerRot = "MinerRot";
        // Before miner is lifted off base, we need to know which one we picked
        [Key("EntityId")] public int EntityId;
        // At the moment of pickup, when Tile gets removed, we fill
        // speed, efficiency and storage size
        [Key("Speed")] public float Speed;
        [Key("MaxDepth")] public float MaxDepth;
        [Key("Efficiency")] public float Efficiency;
        [Key("Storage")] public int Storage;
        [Key("MinerDefId")] public string MinerDefId;
        [Key("PosOnShip")] public int PosOnShip;
        [Key("RotOnShip")] public Facing.Type RotOnShip;
        // Visual size in starmap / sprite
        [Key("Size")] public float Size;
        [Key("Sprite")] public string Sprite;
        [IgnoreMember] public bool IsEmpty => EntityId == 0 && MinerDefId == null;
    }
}
