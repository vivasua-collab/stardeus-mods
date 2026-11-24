using System;

namespace Game.Systems.Creatures {
    [Flags]
    public enum BeingEnvFlags : byte {
        None = 0,
        Safe = 1,
        /// <summary>
        /// Base value checks for minO2, minTemp and maxTemp (no equipment mods)
        /// For showing space suit / helmet
        /// </summary>
        UnsafeBase = 2,
        InCombat = 4,
        TooCold = 8,
        TooHot = 16,
        LowO2 = 32,
        Controllable = 64,
        Outdoors = 128,
    }
}
