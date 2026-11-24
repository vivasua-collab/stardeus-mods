using System;

namespace Game.Systems.Backstories {
    [Flags]
    public enum AgePhase : byte {
        None = 0,
        Childhood = 1,
        YoungAdult = 2,
        MidLife = 4,
        Retirement = 8,
    }
}
