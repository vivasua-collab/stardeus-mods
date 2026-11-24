using System;

namespace Game.Systems.Motion {
    [Flags]
    public enum MotionProgressFlags : ushort {
        None = 0,
        Moving = 1 << 1,
        Failed = 1 << 2,
        HasPath = 1 << 3,
        Unsafe = 1 << 4,
        Accelerating = 1 << 5,
        Decelerating = 1 << 6,
        Teleporting = 1 << 7,
        /// <summary>
        /// There is a mechanic that slows down some beings when they move
        /// in the same direction and are in the same location. This is to spread
        /// them apart a bit so they wouldn't move too close to each other in
        /// congested groups.
        /// Sometimes we may want to move together, like when two beings are on
        /// a date. Then set this flag.
        /// </summary>
        IgnoreCongestion = 1 << 8,
        /// <summary>
        /// Waiting for a body of a hostile army to clear the next tile (HostileBodies)
        /// </summary>
        Blocked = 1 << 9,
    }
}
