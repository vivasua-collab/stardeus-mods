using System;

namespace Game.Systems.Space {
    [Flags]
    public enum HyperspaceStateFlags : byte {
        None = 0,
        CanEnterHyperspace = 1 << 1,
        WantsHyperspace = 1 << 2,
        InHyperspace = 1 << 3,
        PreparingForJump = WantsHyperspace & ~InHyperspace,
    }
}
