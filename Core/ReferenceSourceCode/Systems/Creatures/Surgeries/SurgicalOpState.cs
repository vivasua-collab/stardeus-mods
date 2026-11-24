namespace Game.Systems.Creatures.Surgeries {
    public enum SurgicalOpState : byte {
        Undefined,
        Pending,
        Preparing,
        Ongoing,
        Completed,
        Failed,
        Canceled,
        Active = Pending | Preparing | Ongoing
    }
}
