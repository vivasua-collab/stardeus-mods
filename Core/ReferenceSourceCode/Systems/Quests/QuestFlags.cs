using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestFlags : int {
        None = 0,
        Tutorial = 1 << 0,
        Main = 1 << 1,
        Side = 1 << 2,
        Random = 1 << 3,
        Template = 1 << 4,
        Unique = 1 << 5,
        HideProgress = 1 << 6,
        /// <summary>
        /// mostly for Unique, if it fails and has this, it can be retried
        /// </summary>
        Retryable = 1 << 7,
        /// <summary>
        /// For quests that have to be added automatically (tutorials, main quests, some side quests)
        /// </summary>
        AutoAdd = 1 << 8,
        /// <summary>
        /// For signal quests
        /// </summary>
        Signal = 1 << 9,
        /// <summary>
        /// Personal quest built on the giver's past (grudges, hidden stashes) - clones have none
        /// </summary>
        RequiresMemories = 1 << 10,
        /// <summary>
        /// Personal quest a colonist gives only once per game, once accepted (a hidden stash)
        /// </summary>
        OncePerGiver = 1 << 11,
        /// <summary>
        /// The provider space object (a signal) is removed when the quest is accepted, and the
        /// quest keeps no provider
        /// </summary>
        ConsumeProvider = 1 << 12,
    }
}
