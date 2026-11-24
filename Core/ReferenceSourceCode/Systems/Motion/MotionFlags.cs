
using System;

namespace Game.Systems.Motion {
    [Flags]
    public enum MotionFlags : byte {
        None = 0,
        Immobilized = 1,
        Grounded = 2,
        InSpace = 4,
        ForcedWalk = 8,
        Riding = 16,
        // Main reason for this: being will not float in space while being carried by
        // someone else
        Carried = 32,
    }
}
