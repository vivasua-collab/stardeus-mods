using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestRewardFlags : int {
        None = 0,
        Surprise = 1 << 0,
        HasChance = 1 << 1,
        Penalty = 1 << 2,
        /// <summary>
        /// Won't be listed in the rewards list and won't be given. Steps can
        /// use it separately
        /// </summary>
        PackageContent = 1 << 3,
    }
}
