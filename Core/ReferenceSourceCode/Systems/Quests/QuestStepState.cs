using System;

namespace Game.Systems.Quests {
    public enum QuestStepState : int {
        Undefined = 0,
        Inactive,
        Active,
        Completed,
        Failed,
    }
}
