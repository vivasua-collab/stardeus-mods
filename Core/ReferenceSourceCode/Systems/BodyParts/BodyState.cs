using System;

namespace Game.Systems.BodyParts {
    [Flags]
    public enum BodyState : byte {
        None = 0,
        Alive = 1 << 0,
        Injured = 1 << 1,
        Recovering = 1 << 2,
        Incapacitated = 1 << 3,
        Unhealthy = 1 << 4,
    }
}
