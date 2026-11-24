using System;
using Game.Components;
using MessagePack;

namespace Game.Systems.Sleep {
    [MessagePackObject]
    public struct BedAssignment {
        [Key("OwnerId")] public uint OwnerId;
        [Key("BedId")] public int BedId;
        [Key("Slot")] public byte Slot;

        public static BedAssignment For(uint ownerId, BedSlot slot) {
            return new BedAssignment {
                OwnerId = ownerId,
                BedId = slot.Comp.EntityId,
                Slot = slot.Number,
            };
        }
    }
}
