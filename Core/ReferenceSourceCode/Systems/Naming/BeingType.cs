using System;

namespace Game.Systems.Naming {
    [Flags]
    public enum BeingType : byte {
        None = 0,
        StartingCrew = 1,
        StasisWakeup = 2,
        Friendly = 4,
        Enemy = 8,
        Neutral = 16,
        Trader = 32,
        /// <summary>
        /// Both biological and robotic, when mind gets initialized
        /// </summary>
        Newborn = 64,
        /// <summary>
        /// For being produced by devices such as cloning pod or fabricator for robots
        /// </summary>
        Clone = 128,
    }
}
