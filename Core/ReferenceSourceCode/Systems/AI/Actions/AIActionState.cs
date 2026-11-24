namespace Game.Systems.AI {
    public enum AIActionState : byte {
        Undefined = 0,
        InProgress = 1,
        NeedsReplan = 2,
        Success = 3,
        Cancelled = 4,
        Failed = 5,
        /// <summary>
        /// It means a worker failed a task that can be done by
        /// another worker right away. We will immediately retry it.
        /// </summary>
        FailedSoft = 6,
        Error = 7,
        /// <summary>
        /// The goal is neither failed nor succeeded, we may have pre-assigned
        /// a different goal that needs to be done before this one.
        /// </summary>
        PutBack = 8,
    }
}
