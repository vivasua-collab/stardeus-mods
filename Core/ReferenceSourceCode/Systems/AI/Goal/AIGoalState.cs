namespace Game.Systems.AI {
    public enum AIGoalState : byte {
        Uninitialized = 0,
        Pending = 1,
        PreAssigned = 2,
        Available = 3,
        Bidding = 4,
        InProgress = 5,
        Failed = 6,
        Cancelled = 7,
        Interrupted = 8,
        Completed = 9,
        CoolingDown = 10,
        Error = 11,
        ErrorLooping = 12,
    }
}
