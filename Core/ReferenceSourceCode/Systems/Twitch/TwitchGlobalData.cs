using System;
using System.Collections.Generic;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Twitch {
    [MessagePackObject]
    public class TwitchGlobalData {
        [Key("Config")] public TwitchConfig Config;
        [Key("Settings")] public TwitchSettings Settings;
        /// <summary>
        /// GameIdH => data
        /// </summary>
        [Key("GameData")] public Dictionary<int, TwitchGameData> GameData;
        /// <summary>
        /// Viewer Id => viewer
        /// </summary>
        [Key("Viewers")] public Dictionary<int, TwitchViewer> Viewers;
        [Key("CommandCosts")] public Dictionary<int, int> CommandCosts;

        public void Cleanup() {
            if (Viewers != null) {
                foreach (var viewer in Viewers.Values) {
                    viewer.Cleanup();
                }
            }
        }

        public void AfterLoad() {
            Viewers ??= new();
            GameData ??= new();
            CommandCosts ??= new();
            Settings ??= TwitchSettings.Default;
            foreach (var v in Viewers.Values) {
                v.AfterLoad();
            }
            TwitchRole.LoadKnown();
            // 1. add missing roles
            foreach (var r in TwitchRole.All) {
                if (!Settings.Roles.ContainsKey(r.Key)) {
                    D.Warn("Adding missing role: {0}", r.Value.Id);
                    Settings.Roles[r.Key] = TwitchRoleData.DefaultFor(r.Value);
                }
            }
        }

        public TwitchGameData GameDataFor(GameState state, bool createIfMissing) {
            var gameIdH = Hashes.S(state.UniqueId);
            if (!GameData.TryGetValue(gameIdH, out var data)) {
                if (!createIfMissing) {
                    return null;
                }
                data = TwitchGameData.CreateFor(state);
                GameData[gameIdH] = data;
            }
            return data;
        }
    }
}
