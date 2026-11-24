using System;

namespace Game.Systems.Mental {
    [Flags]
    public enum JobTypeFlags : ushort {
        None = 0,
        Mindless = 1 << 0,
        Intellectual = 1 << 1,
        Physical = 1 << 2,
        Challenging = 1 << 3,
        MicroPrecision = 1 << 4,
        Violent = 1 << 5,
        Caring = 1 << 6,
        Internal = 1 << 7,
        Creative = 1 << 8,
        Unskilled = 1 << 9,
        Dangerous = 1 << 10,
        NanobotCompatible = 1 << 11,
    }
}
