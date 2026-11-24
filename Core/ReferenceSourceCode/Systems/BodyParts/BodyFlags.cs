using System;

namespace Game.Systems.BodyParts {
    [Flags]
    public enum BodyFlags : byte {
        None = 0,
        Clone = 1,
        Missing = 2,
        Invincible = 4,
    }
}
