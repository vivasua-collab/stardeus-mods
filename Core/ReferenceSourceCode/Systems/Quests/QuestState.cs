using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestState {
        Undefined = 0,
        Available = 1 << 0,
        Active = 1 << 1,
        Completed = 1 << 2,
        Failed = 1 << 3,
        Rejected = 1 << 4,
        Deleted = 1 << 5,
        Ongoing = Available | Active,
        Ended = Completed | Failed | Rejected | Deleted,
        Unsuccessful = Failed | Rejected | Deleted,
    }
}
