//#define DEBUG_TWITCH
using System;

namespace Game.Systems.Twitch {
    [Flags]
    public enum TwitchViewerFlags : byte {
        None = 0,
        Viewer = 1 << 0,
        VIP = 1 << 1,
        Subscriber = 1 << 2,
        Moderator = 1 << 3,
        Broadcaster = 1 << 4,
    }
}
