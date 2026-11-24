using System;

namespace Game.Systems.Story {
    [Flags]
    public enum StoryEventDataFlags : uint {
        None = 0,
        SkipEventLog = 1 << 0,
        SkipFanfare = 1 << 1,
        SkipSeen = 1 << 2,
        /// <summary>
        /// When event fails, if this flag is set, it will cooldown
        /// </summary>
        NeedsCooldown = 1 << 3,
        /// <summary>
        /// For faster / easier execution in tests, if applicable
        /// </summary>
        Debug = 1 << 4,
        /// <summary>
        /// For when an event fails due to ineligible conditions that were
        /// too expensive to check in CheckConditions
        /// </summary>
        SoftFailure = 1 << 5,
        SubEvent = SkipEventLog | SkipFanfare | SkipSeen
    }
}
