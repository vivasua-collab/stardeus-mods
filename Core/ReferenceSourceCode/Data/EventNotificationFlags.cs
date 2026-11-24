using System;

namespace Game.Data {
    [Flags]
    public enum EventNotificationFlags {
        None = 0,
        Groupable = 1 << 0,
        Emergency = 1 << 1,
        TriggerSirens = 1 << 2,
        Silent = 1 << 3,
        AutoAction = 1 << 4,
        AddLogLine = 1 << 5,
        BellAlways = 1 << 6,
        BellRare = 1 << 7,
    }
}
