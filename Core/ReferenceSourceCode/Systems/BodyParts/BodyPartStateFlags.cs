using System;

namespace Game.Systems.BodyParts {
    [Flags]
    public enum BodyPartStateFlags : byte {
        None = 0,
        Incapacitated = 1 << 0,
        ParentUnusable = 1 << 1,
        Injured = 1 << 2,
        Destroyed = 1 << 3,
        Infected = 1 << 4,
        Welded = 1 << 5,
        Crippled = 1 << 6,
    }
}
