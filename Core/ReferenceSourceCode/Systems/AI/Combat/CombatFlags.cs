using System;

namespace Game.Systems.AI.Combat {
    [Flags]
    public enum CombatFlags : byte{
        None = 0,
        Hostile = 1 << 0,
        Friendly = 1 << 1,
    }
}
