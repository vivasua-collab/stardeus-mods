using System;

namespace Game.Systems.Notify {
    [Flags]
    public enum NotificationFlags : uint {
        None = 0,
        Sticky = 1 << 0,
        Seen = 1 << 1,
        HasOccurrences = 1 << 2,
        Emergency = 1 << 3,
        TriggersSiren = 1 << 4,
        Silent = 1 << 5,
        BellAlways = 1 << 6,
        BellRare = 1 << 7,
        AutoAction = 1 << 8,
        Removed = 1 << 10,
        PulseOnly = 1 << 11,
    }
}
