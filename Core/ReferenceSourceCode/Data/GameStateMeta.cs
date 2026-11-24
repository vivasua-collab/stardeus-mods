using System;
using System.Collections.Generic;
using Game.Utils;
using KL.Utils;
using MessagePack;

namespace Game.Data {
    [MessagePackObject]
    public class GameStateMeta {
        [IgnoreMember] public DateTime LastAccessTime => DateTime.FromFileTimeUtc(LoadTime);
        [IgnoreMember] public DateTime LastWriteTime => DateTime.FromFileTimeUtc(SaveTime);
        [IgnoreMember] public string SaveName;
        [IgnoreMember] public string SaveDir;
        [IgnoreMember] public bool IsTemplateMode;
        // Neither the meta file nor the save could be read (e.g. a path over 260 characters),
        // so this one only names the file
        [IgnoreMember] public bool IsPlaceholder;
        [IgnoreMember] public string SaveFile {
            get {
                var hasDir = !string.IsNullOrWhiteSpace(SaveDir);
                var hasName = !string.IsNullOrWhiteSpace(SaveName);
                if (hasDir && hasName) {
                    return Files.CombinePath(SaveDir, SaveName);
                }
                if (GameName == null) {
                    GameName = $"Unnamed {DateTime.Now:d}";
                }
                if (!hasDir) {
                    D.Err("No save dir for game meta! {0}", GameName);
                    SaveDir = SaveLoadUtils.SaveDir(GameName);
                }
                if (!hasName) {
                    D.Err("No save name for game meta! {0} | {1}",
                        GameName, SaveName);
                    SaveName = GameName;
                }
                return Files.CombinePath(SaveDir, SaveName);
            }
        }
        [Key("GameName")] public string GameName;
        [Key("UniqueId")] public string UniqueId;
        [Key("SaveVersion")] public int SaveVersion;
        [Key("GameVersion")] public string GameVersion;
        [Key("ClockTicks")] public long ClockTicks;
        [Key("GridWidth")] public int GridWidth;
        [Key("GridHeight")] public int GridHeight;
        [Key("Seed")] public int Seed;
        [Key("SeedStr")] public string SeedStr;
        [Key("ModifierTooltips")] public Dictionary<string, string> ModifierTooltips;
        [Key("Mods")] public Dictionary<string, string> Mods;
        [Key("Scenario")] public string ScenarioId;
        [Key("IsPermadeath")] public bool IsPermadeath;
        [Key("IsGameOver")] public bool IsGameOver;
        [Key("Storygen")] public string StoryGenId;
        [Key("Playtime")] public float Playtime;
        [Key("SaveTime")] public long SaveTime;
        [Key("LoadTime")] public long LoadTime;
        [Key("IsAutoSave")] public bool IsAutoSave;
        [Key("Difficulty")] public string Difficulty;
        [Key("IsAchievementsBlocked")] public bool IsAchievementsBlocked;
    }
}
