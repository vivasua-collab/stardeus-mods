using System;

namespace Game.Systems.Story {
    public enum StoryEventState {
        Undefined,
        /// <summary>
        /// Never set this in event implementations
        /// </summary>
        Starting,
        /// <summary>
        /// Never set this in event implementations
        /// </summary>
        WaitingForHook,
        /// <summary>
        /// Never set this in event implementations
        /// </summary>
        Ongoing,
        /// <summary>
        /// Set this yourself in event implementations when you want to signal
        /// the system that event has ended.
        /// </summary>
        Ending,
        /// <summary>
        /// Never set this yourself in event implementations, set to Ending instead.
        /// </summary>
        Ended,
        /// <summary>
        /// Event didn't happen due to unmet conditions or error. This event
        /// should not be displayed in logs.
        /// </summary>
        Failed,
    }
}
