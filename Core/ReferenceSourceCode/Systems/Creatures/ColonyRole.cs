namespace Game.Systems.Creatures {
    /// <summary>
    /// A being's special, non-hostile standing within the colony (guest passenger, future
    /// prisoner/visitor, …). Distinct from <see cref="BeingState"/> (lifecycle) and from
    /// Game.Systems.Backstories.BeingRole (career/experience). A being has at most one.
    /// Lives on the Mind (identity), so it follows mind transfers. Default Unassigned (0),
    /// so old saves load with no migration.
    /// </summary>
    public enum ColonyRole : byte {
        Unassigned = 0,
        // A temporary guest of a different, non-hostile faction (e.g. a delivery quest
        // passenger): lives aboard, does no colony work, defects if their faction raids.
        Passenger = 1,
        // A colonist who deserted during a mental breakdown (stole a shuttle and left for
        // good). Removed from the ship as Missing; kept for a future "returns" story hook.
        Deserter = 2,
        // A colonist manually sent back into cryo-stasis via StasisArrayComp (see
        // StasisUtils). Removed from the ship as Missing; may be offered back as a wake-up
        // choice by StasisWakeUpStoryEvent.
        InStasis = 3,
        // future: Prisoner, Visitor, ...
    }
}
