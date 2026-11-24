using System;

namespace Game.Systems.Firex {
    [Flags]
    public enum ExplosionFlags : ushort {
        None = 1 << 0,
        /// <summary>
        /// Once explosion gets executed, this flag should be set to prevent
        /// bugs that trigger same explosion more than once
        /// </summary>
        Executed = 1 << 1,
        /// <summary>
        /// By default explosions cause event notifications
        /// </summary>
        SkipEvent = 1 << 2,
        IgnoreObstacles = 1 << 3,
        SkipFX = 1 << 4,
        /// <summary>
        /// No delay, just blow up as soon as we get this
        /// </summary>
        Immediate = 1 << 5,
        /// <summary>
        /// The explosion won't affect floors. This is for breach charges, etc.
        /// </summary>
        SkipFloors = 1 << 6,
        StructureOnly = 1 << 7,
        BlowHoleInFloor = 1 << 8,
        /// <summary>
        /// No damage, only fire
        /// </summary>
        FireOnly = 1 << 9,
        /// <summary>
        /// Custom explosions rely on a custom function instead of doing explosion cell damage
        /// </summary>
        Custom = 1 << 10,
        /// <summary>
        /// A spray rather than a blast: only walls and closed doors stop it, while devices,
        /// furniture and beings in the way do not (Firefoam Capsule)
        /// </summary>
        OnlyWallsBlock = 1 << 11,
    }
}
