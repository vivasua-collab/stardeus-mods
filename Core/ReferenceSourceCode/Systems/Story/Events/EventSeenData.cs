using System;
using Game.Constants;
using Game.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Story {
    [MessagePackObject]
    public struct EventSeenData {
        [Key("Id")] public int Id;
        [Key("SeenCount")] public ushort SeenCount;
        [Key("StartCount")] public ushort StartCount;
        [Key("LastSeen")] public long LastSeen;
        [Key("LastStart")] public long LastStart;

        public override readonly string ToString() {
            return $"EventSeenData[{Id}|Seen(x{SeenCount}):{Units.TicksToTime(G.Ticks - LastSeen)}|Start(x{StartCount}):{Units.TicksToTime(G.Ticks - LastStart)}]";
        }
    }
}
