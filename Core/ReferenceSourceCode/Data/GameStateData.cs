using System;
using System.Collections.Generic;
using Game.Components;
using Game.Data.Economy;
using Game.Data.Space;
using Game.Research;
using Game.Story.Events;
using MessagePack;
using UnityEngine;

namespace Game.Data {
    [MessagePackObject]
    public class GameStateData {
        [Key("GameName")] public string GameName;
        [Key("OSName")] public string ShipComputerName;
        [Key("UniqueId")] public string UniqueId;
        [Key("SaveVersion")] public int SaveVersion;
        [Key("GameVersion")] public List<string> GameVersions;
        [Key("RecentBuildItems")] public List<int> RecentItems;
        [Key("ResearchTree")] public List<ResearchProgress> Tree;
        [Key("Settings")] public ComponentData SettingsData;
        [Key("ClockTicks")] public long ClockTicks;
        [Key("SaveTime")] public long SaveTime;
        [Key("Seed")] public int Seed;
        [Key("StoryGenId")] public string StoryGenId;
        [Key("SeedStr")] public string SeedStr;
        [Key("SeedSpace")] public int SeedSpace;
        [Key("SeedSpaceStr")] public string SeedSpaceStr;
        [Key("RngState")] public uint RngState;
        [Key("Situation")] public Situation Situation;
        [Key("LastWinchTick")] public long LastWinchTick;
        [Key("BetaWaveSync")] public float BetaWavesSync;
        [Key("TelescopeProgress")] public float TelescopeProgress;
        [Key("TelescopeScanCount")] public int TelescopeScanCount;
        [Key("CameraPosition")] public Vector2 CameraPosition;
        [Key("CameraZoom")] public float CameraZoom;
        [Key("ScenarioData")] public ComponentData ScenarioData;
        [Key("Playtime")] public float Playtime;
        [Key("SessionCount")] public int SessionCount;
        [Key("Universe")] public UniverseData Universe;
        [Key("SystemsData")] public SystemsData Systems;
        [Key("Wallet")] public Wallet Wallet;
        [Key("Market")] public Market Market;
        [Key("IsAchievementsBlocked")] public bool IsAchievementsBlocked;
        [Key("Modifiers")] public List<string> Modifiers;
        [Key("Portfolio")] public MarketPortfolio Portfolio;
        [Key("Difficulty")] public string Difficulty;
        [Key("MirrorPoint")] public int MirrorPoint;
        [Key("MirrorEven")] public bool MirrorEven;
        [Key("UsedNames2")] public Dictionary<int, HashSet<int>> UsedNames;

        [Key("NewBlueprints")] public HashSet<string> NewBlueprints;
        [Key("Shared")] public string Shared;
    }
}
