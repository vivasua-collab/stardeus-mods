using System;

namespace Game.Systems.Creatures {
    [Flags]
    public enum BeingGroupFlags : byte {
        None = 0,
        PlayerFaction = 1 << 0
    }
}
