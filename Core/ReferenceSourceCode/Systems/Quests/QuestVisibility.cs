using System;

namespace Game.Systems.Quests {
    [Flags]
    public enum QuestVisibility {
        Undefined = 0,
        Hidden = 1 << 0,
        Visible = 1 << 1,
    }
}
