using System;

namespace Game.Systems.Reg {
    [Flags]
    public enum RegPathReqFlags : byte {
        None = 0,
        Grounded = 1,
        Nearby = 2,
        GroundedNearby = Grounded | Nearby,
        // Doors the requester may not pass count as passable at a price (breaking them)
        Breach = 4,
    }
}
