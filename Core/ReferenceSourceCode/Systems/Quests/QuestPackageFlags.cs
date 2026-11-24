using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestPackageFlags : int {
        None = 0,
        FailQuestOnOpen = 1 << 0,
        FailQuestOnDestroy = 1 << 1,
        CompleteQuestOnOpen = 1 << 2,
        QuestProviderRetaliateOnOpen = 1 << 3,
        Default = FailQuestOnOpen | FailQuestOnDestroy | QuestProviderRetaliateOnOpen,
    }
}
