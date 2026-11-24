using System;

namespace Game.Systems.AI {
    [Flags]
    public enum AIGoalFlags : ushort {
        None = 0,
        UnusedReserved = 1 << 0,
        /// <summary>
        /// When the goal is finished or cancelled, it gets removed completely.
        /// </summary>
        NonRetryable = 1 << 1,
        ForceAssigned = 1 << 2,
        /// <summary>
        /// Normally there will be a reachability check when trying to bid on goals.
        /// The check will attempt to validate the ability to reach Goal.Target.
        /// If this flag is set, the location will not be attempted to be verified
        /// </summary>
        IgnoreLocation = 1 << 3,
        // If this is set, we'll try to do region path finding to exact location,
        // otherwise only near it. Default is near.
        LocationExact = 1 << 4,
        /// <summary>
        /// The task will never get interrupted or reassigned until completed or
        /// failed, no matter what. Mostly used for mental breakdowns.
        /// </summary>
        NonInterruptable = 1 << 5,
        /// <summary>
        /// For tasks created by the player
        /// </summary>
        ByPlayer = 1 << 6,
        /// <summary>
        /// For tasks that don't have a target
        /// </summary>
        NoTarget = 1 << 7,
        /// <summary>
        /// This task can be performed by nanobots
        /// </summary>
        NanobotCompatible = 1 << 8,
        /// <summary>
        /// There is a nanobot hive that can work on this task
        /// </summary>
        NanobotWorkable = 1 << 9,
        /// <summary>
        /// This task has been streamlined (adjacent task of same type was
        /// pre-assigned)
        /// </summary>
        Streamlined = 1 << 10,
        /// <summary>
        /// Some tasks involve seeing dead bodies, so let's ignore the debuff
        /// for tasks marked with this flag
        /// </summary>
        IgnoreDeadBodies = 1 << 11,
        /// <summary>
        /// Failed because no worker able to do it can walk there. Saved, so the
        /// Unreachable tasks alert and the retry on island merge survive a load.
        /// </summary>
        Unreachable = 1 << 12,
        /// <summary>
        /// Background work the system that created it owns entirely (per-room floor
        /// cleaning): no target entity, so it never shows on the map or in task lists, the
        /// player can't cancel or reprioritize it, and it is not saved, its owner rebuilds it.
        /// </summary>
        Hidden = 1 << 13,
    }
}
