using System;

namespace Game.Systems.Notify {
    public enum NotificationType : byte {
        Immediate = 0,
        Critical = 1,
        Alert = 2,
        Warning = 3,
        Info = 4,
    }
}
