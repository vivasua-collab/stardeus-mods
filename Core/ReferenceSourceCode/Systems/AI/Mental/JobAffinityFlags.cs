using System;

namespace Game.Systems.Mental {
    [Flags]
    public enum JobAffinityFlags : byte {
        None = 0,
        Incapable = 1,
        Loves = 2,
    }
}
