using System;
using System.Collections.Generic;
using Game.Data;
using MessagePack;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public class TwitchGameData {
        [Key("GameID")] public string GameId;
        [Key("GameName")] public string GameName;
        [Key("Queue")] public TwitchViewerQueue Queue;
        /// <summary>
        /// viewerId -> BeingData Id
        /// </summary>
        [Key("BeingIds")] public Dictionary<int, uint> BeingIds;
        /// <summary>
        /// viewerId -> MindObj Id
        /// </summary>
        [Key("MindIds")] public Dictionary<int, uint> MindIds;
        [Key("SkipWelcome")] public bool SkipWelcome;
        public static TwitchGameData CreateFor(GameState state) {
            return new TwitchGameData() {
                GameId = state.UniqueId,
                GameName = state.GameName,
                Queue = new(),
                BeingIds = new(),
                MindIds = new(),
            };
        }
    }
}
