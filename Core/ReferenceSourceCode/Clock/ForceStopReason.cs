using System;

namespace KL.Clock {
    [Flags]
    public enum ForceStopReason : ushort {
        None = 0,
        Trade = 1 << 0,
        StasisWakeUp = 1 << 1,
        Cutscene = 1 << 2,
        MenuPanel = 1 << 3,
        BulkOperation = 1 << 4,
        Save = 1 << 5,
        Camera = 1 << 6,
        Choice = 1 << 7,
        Debug = 1 << 8,
        ShipModification = 1 << 9,
        HoveringNotifications = 1 << 10,

        SkipSound = StasisWakeUp | Cutscene | Save | Choice | Camera,
    }
}
