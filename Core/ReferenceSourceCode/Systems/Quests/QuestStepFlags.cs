using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestStepFlags : int {
        None = 0,
        Optional = 1 << 0,
        CompletesQuest = 1 << 1,
        FailsQuest = 1 << 2,
        ActiveOnStart = 1 << 3,
        /// <summary>
        /// Hidden until activated
        /// </summary>
        HideInactive = 1 << 4,
        /// <summary>
        /// Hidden until completed
        /// </summary>
        Secret = 1 << 5,
        /// <summary>
        /// Hidden until quest becomes accepted
        /// </summary>
        HideIfQuestUnaccepted = 1 << 6,
        /// <summary>
        /// For quest steps that should tick even if the quest is available
        /// </summary>
        TickAvailable = 1 << 7,
    }
}
