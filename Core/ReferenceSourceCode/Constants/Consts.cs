using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Constants {
    public static class Consts {
        /// <summary>
        /// When mod loader will be trying to get a file or a folder, the following load order will be used.
        ///
        /// Files in %ModDir%/CommonPre will always be tried first.
        /// Then the %ModDir%/%Version% list in descending oder.
        /// Then %ModDir%/CommonPost.
        /// Then anything in the %ModDir%/ folder.
        ///
        /// When new versions of the game will get released, they will be added
        /// to this array after "CommonPre".
        /// </summary>
        public static readonly string[] ModDirLoadOrder = new string[] {
            "CommonPre",
            "0.15",
            "0.14",
            "0.13",
            "0.12",
            "0.11",
            "CommonPost",
            ""
        };

        public const string ModsVersionTag = "v0.15";
        // FIXME this should go into CurveDefs
        public static readonly Curve QualityStatCurve = new float[] {
            0f, 0.5f,
            0.25f, 0.6f,
            0.5f, 0.8f,
            0.95f, 1f,
            1.0f, 1.1f,
        };
        public static readonly float StopwatchFreq = Stopwatch.Frequency;
        public const int MapChunkSize = 8;
        public const int MapChunkSizeSquared = MapChunkSize * MapChunkSize;
        public static bool IsAMD;
        public const float TooltipTimeoutShort = 1.0f;
        public static bool IsLessAwesomeGPU;
        public static bool IsPotato;
        public static bool IsLaptop;
        public const int GraphicsCacheVersion = 19;
        public const bool DefaultNebulaOn = true;
        public const bool DefaultWidescreenAutoPadding = false;
        public static bool DisableHoverEffects;
        public static bool HasComputeShaders;
        public const string VirtualMouseName = "VirtualMouse";
        public const int MaxTaskCap = 1024*4;
        public const int MinTaskCap = 128;
        public const float DoubleClickTime = 0.250f; // 250 ms
        // Mass selection cap. The multi entity panel and the entity action bar
        // both cost per selected entity, so unbounded selections freeze the game.
        public const int MaxSelectedEntities = 1000;
        public static bool IsVersionLatest;
        public static bool SkipVersionCheck;
        public const string PublicChangelog = "https://stardeusgame.com/changelog/";

        public static readonly float4 CombatTargetColor = Colors.ToF4(new Color32(240, 24, 24, 128));
        public static readonly Color32 ColorCompatiblePlacement = new(77, 134, 19, 48);
        public static readonly Color32 ColorGreenBar = Colors.FromHex("#5EB509");
        public static readonly Color32 ColorHealthBar = Colors.FromHex("#DA343B");
        public static readonly Color32 ColorBatteryBar = Colors.FromHex("#3363CB");
        public static readonly Color32 ColorToiletBar = Colors.FromHex("#DFBB29");
        public static readonly Color32 ColorConstructBar = Colors.FromHex("#DFBB29");
        public static readonly Color32 ColorFuelBar = Colors.FromHex("#5EB509");
        public static readonly Color ColorLandingZone = new(1f, 0.6f, 0f, 0.7f);
        public static readonly Color ColorRoof = new(0.255f, 0.291f, 0.345f, 1f);
        public static readonly Color ColorNoBuildZone = new(0.9f, 0.1f, 0.1f, 0.5f);
        public static readonly Color ColorNoFlyZone = new(0.8f, 0.4f, 0.4f, 0.5f);
        public static readonly Color ColorCornerGap = new(1f, 0.05f, 0.05f, 0.95f);
        public const float NeedDropRateSleepAwake = 100f / (TicksPerHour * 16);
        public const float NeedDropRateSleepAsleep = 100f / TicksPer8Hours;
        public const float NeedDropRateRestAsleep = 150f / TicksPer8Hours;
        public const float NeedDropRatePurposeOperating = 50f / TicksPer8Hours;
        public const float NeedDropRateFunOperating = -25f / TicksPer8Hours;
        public const float NeedDropRateToilet = 100f / (TicksPerHour * 20);
        public const float NeedDropRateShower = 100f / (TicksPerHour * 30);
        public const float NeedDropRateToiletSatisfying = 100f / TicksPer30Minutes;
        public const float NeedDropRateShowerSatisfying = 100f / TicksPerHour;
        public const float NeedDropRateHunger = 100f / (TicksPerDay * 3);
        public const float NeedDropRateVerySlow = 1f / TicksPerDay;
        public const float NeedDropRateSlow = 1f / TicksPerHour * 3f;
        public const float NeedDropRateNormal = 1f / TicksPerHour;
        public const float NeedDropRateFast = 1f / TicksPer30Minutes;
        public const float CPUHeatAccumulation = 0.00003f;
        // Never change this or you will burn in hell
        // This will prevent old saves from loading
        public const int CurrentSaveVersion = 3;
        public static string Dir => Application.dataPath;
        public static readonly Vector2 V2Zero = new(0, 0);
        public static readonly Vector2 V2Up = new(0, 1);
        public static readonly Vector2 V2Left = new(-1, 0);
        public static readonly Vector2 V2Right = new(1, 0);
        public static readonly Vector2 V2Down = new(0, -1);
        public static readonly Vector2 V2Invalid = new(-1, -1);
        public static readonly Vector2 V2One = new(1, 1);
        public static readonly Vector2 V2OneTenth = new(0.1f, 0.1f);
        public static readonly Vector2 V2Half = new(0.5f, 0.5f);
        public static readonly Vector3 V3Zero = new(0, 0, 0);
        public static readonly Vector3 V3One = new(1, 1, 1);
        public static readonly Quaternion QId = Quaternion.identity;
        public static readonly Quaternion Q0 = Quaternion.Euler(0, 0, 0);
        public static readonly Quaternion Q90 = Quaternion.Euler(0, 0, 90);
        public static readonly Quaternion Q180 = Quaternion.Euler(0, 0, 180);
        public static readonly Quaternion Q270 = Quaternion.Euler(0, 0, 270);
        public const float UIScaleMin = 0.5f;
        public const float UIScaleMax = 1.25f;
        public const float UIFontScaleMin = 0.85f;
        public const float UIFontScaleMax = 1.25f;
        public static float UIScaleSteamDeckDefault = 0.75f;
        public static float UIFontScaleSteamDeckDefault = 1.15f;
        public static float UIFontScaleDefault = 1f;
        public static int DefaultLightMapScale = 3;
        public static int DefaultLightMapScaleLowEnd = 4;
        public static int DefaultLightMapCellSize = 16;
        public static int DefaultLightMapMaxLightsPerCellLowEnd = 256;
        public static int DefaultLightMapMaxLightsPerCell = 1024;
        public static readonly long TimeOffset = new DateTime(1970, 1, 1)
            .ToFileTimeUtc();
        public const long TickDurationInGameTime =
            TimeSpan.TicksPerMinute / TicksPerMinute;
        public const string TimeFormat = "HH:mm";
        private static string version;
        #if UNITY_EDITOR
        public static bool CacheGraphics = true;
        #else
        public static bool CacheGraphics = true;
        #endif
        private static string build;
        #if STARDEUS_DEMO
        public static string FullVersion => $"{Version}.{Build}-demo";
        public static string ShortVersion => $"{Version}-demo";
        #else
        public static string FullVersion => $"{Version}.{Build}";
        public static string ShortVersion => Version;
        #endif
        public static string Version {
            get {
                if (string.IsNullOrWhiteSpace(version)) {
                    version = Resources.Load<TextAsset>("sync/version").text.Trim();
                }
                return version;
            }
        }
        public static string Build {
            get {
                if (string.IsNullOrWhiteSpace(build)) {
                    build = Resources.Load<TextAsset>("sync/build_number").text.Trim();
                    if (string.IsNullOrWhiteSpace(build)) {
                        build = "1";
                    }
                }
                return build;
            }
        }
        #if !PRODUCTION_BUILD
        public static string BuildDate => System.DateTime.UtcNow.ToString("yyyy.MM.dd");
        #else
        private static string buildDate;
        public static string BuildDate {
            get {
                if (string.IsNullOrWhiteSpace(buildDate)) {
                    buildDate = Resources.Load<TextAsset>("sync/build_date").text.Trim();
                }
                return buildDate;
            }
        }
        #endif
        public const int MaxConcurrentSoundFX = 8;
        public const int MaxConcurrentLoudSoundFX = 8;
        public static WaitForEndOfFrame WaitEOF = new();
        #if PRODUCTION_BUILD
        public static WaitForSecondsRealtime Wait100Ms = new(0.1f);
        public static WaitForSecondsRealtime Wait500Ms = new(0.5f);
        public static WaitForSecondsRealtime Wait1Sec = new(1f);
        public static WaitForSecondsRealtime Wait2Sec = new(2f);
        public static WaitForSecondsRealtime Wait3Sec = new(3f);
        public static WaitForSecondsRealtime Wait5Sec = new(5f);
        #else
        public static bool SlowTests => CachedPlayerPrefs.GetInt("SlowTests", 0) == 1;
        public static WaitForSecondsRealtime Wait100Ms {
            get {
                if (The.TestsAreRunning && !SlowTests) {
                    D.Err("Waiting 0.1 sec in tests!");
                }
                return new(0.1f);
            }
        }
        public static WaitForSecondsRealtime Wait500Ms {
            get {
                if (The.TestsAreRunning) {
                    D.Err("Waiting 0.5 sec in tests!");
                }
                return new(0.5f);
            }
        }
        public static WaitForSecondsRealtime Wait1Sec {
            get {
                if (The.TestsAreRunning) {
                    D.Err("Waiting 1 sec in tests!");
                }
                return new(1f);
            }
        }
        public static WaitForSecondsRealtime Wait2Sec {
            get {
                if (The.TestsAreRunning) {
                    D.Err("Waiting 2 sec in tests!");
                }
                return new(2f);
            }
        }
        public static WaitForSecondsRealtime Wait3Sec {
            get {
                if (The.TestsAreRunning) {
                    D.Err("Waiting 3 sec in tests!");
                }
                return new(3f);
            }
        }
        public static WaitForSecondsRealtime Wait5Sec {
            get {
                if (The.TestsAreRunning) {
                    D.Err("Waiting 5 sec in tests!");
                }
                return new(5f);
            }
        }
        #endif
        // FIXME profile and figure out best size
        public const int DefaultSpriteShardSize = 32;
        public const int DefaultParticleShardSize = 32;
        public const float PanAreaScreenPercent = 0.03f;
        public const float EdgePanSpeed = 1f;
        public const float DefaultUIScale = 1f;
        public const int DefaultCancelDragDistancePx = 20;
        public const float CameraFocusZoomLevel = 20f;
        public const float CameraDollyDefaultAcceleration = 1f;
        public const int DefaultEnergyConnectorRange = 10;
        public const int DefaultEnergyConnectorMaxConn = 8;
        public const float DefaultZoomSensitivity = 5f;
        public const float DefaultPanSensitivity = 10f;
        public const float DefaultDeadZoneMin = 0.125f;
        public const float DefaultDeadZoneMinMin = 0.05f;
        public const float DefaultDeadZoneMinMax = 0.45f;
        public const float DefaultDeadZoneMax = 0.925f;
        public const float DefaultDeadZoneMaxMin = 0.55f;
        public const float DefaultVMouseSens = 1000f;
        public const float DefaultVMouseAccel = 0.3f;
        public const float DefaultVMouseMaxSpeed = 1.3f;
        public const float VMouseSensMin = 100f;
        public const float VMouseSensMax = 3000f;
        public const float VMouseAccelMax = 3f;
        public const float VMouseMaxSpeedMin = 1f;
        public const float VMouseMaxSpeedMax = 3f;
        //public static bool DefaultUIScrollInverted = false;
        public const float DefaultUIScrollSpeed = 5;
        public const bool DefaultUIScrollInverted = false;
        public const float DefaultEdgePanSpeed = 0f;
        public const float DefaultTooltipDelay = 0.25f;
        public const float TooltipDelayMin = 0.1f;
        public const float TooltipDelayMax = 2f;
        public const float TooltipDelayStep = 0.05f;
        #if !PRODUCTION_BUILD
        public static int MapMargin = 20;
        #else
        public const int MapMargin = 20;
        #endif
        public const float CornerMarginPercent = 0.25f;
        public static int CornerMargin = 60;
        public static void SetCornerMargin(int width) {
            var margin = Mathf.RoundToInt(width * CornerMarginPercent);
            CornerMargin = Mathf.Clamp(margin, MinCornerMargin, MaxCornerMargin);
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                if (width < 160) {
                    MapMargin = 4;
                    CornerMargin = 5;
                }
            }
            #endif
        }

        public const int MinCornerMargin = 60;
        public const int MaxCornerMargin = 80;
        public const int FloodFillTimeBudget = 30; // seconds
        ///<summary>In Game Time</summary>
        public const int TicksPerMonth = TicksPerDay * 30;
        public const int TicksPer2Months = TicksPerDay * 30 * 2;
        public const int TicksPer3Months = TicksPerDay * 30 * 3;
        public const int TicksPer6Months = TicksPerDay * 30 * 6;
        public const int TicksPer9Months = TicksPerDay * 30 * 9;
        ///<summary>In Game Time</summary>
        public const int TicksPerYear = TicksPerMonth * 12;
        public const int TicksPer2Years = TicksPerMonth * 12 * 2;
        public const int TicksPer3Years = TicksPerMonth * 12 * 3;
        public const int TicksPer5Years = TicksPerMonth * 12 * 5;
        public const int TicksPer10Years = TicksPerMonth * 12 * 10;
        ///<summary>In Game Time</summary>
        public const int TicksPerWeek = TicksPerDay * 7;
        public const int TicksPer2Weeks = TicksPerDay * 14;
        public const int TicksPer3Weeks = TicksPerDay * 21;
        public const int TicksPer4Weeks = TicksPerWeek * 4;
        public const int TicksPer5Weeks = TicksPerWeek * 5;
        public const int TicksPer6Weeks = TicksPerWeek * 6;
        ///<summary>In Game Time</summary>
        public const int TicksPerDay = TicksPerHour * 24;
        public const int TicksPer1Days = TicksPerHour * 24;
        public const int TicksPer2Days = TicksPerHour * 24 * 2;
        public const int TicksPer3Days = TicksPerHour * 24 * 3;
        public const int MatRateHistoryDuration = TicksPer3Days;
        public const int TicksPer4Days = TicksPerHour * 24 * 4;
        public const int TicksPer5Days = TicksPerHour * 24 * 5;
        public const int TicksPer6Days = TicksPerHour * 24 * 6;
        public const int TicksPer7Days = TicksPerHour * 24 * 7;
        public const int TicksPer8Days = TicksPerHour * 24 * 8;
        public const int TicksPer9Days = TicksPerHour * 24 * 9;
        public const int TicksPer10Days = TicksPerHour * 24 * 10;
        public const int TicksPer11Days = TicksPerHour * 24 * 11;
        public const int TicksPer12Days = TicksPerHour * 24 * 12;
        public const int TicksPer13Days = TicksPerHour * 24 * 13;
        public const int TicksPer14Days = TicksPerHour * 24 * 14;
        ///<summary>In Game Time</summary>
        public const int TicksPerHour = TicksPerMinute * 60;
        public const int TicksPer1Hours = TicksPerHour;
        public const int TicksPer2Hours = TicksPerHour * 2;
        public const int TicksPer3Hours = TicksPerHour * 3;
        public const int TicksPer4Hours = TicksPerHour * 4;
        public const int TicksPer6Hours = TicksPerHour * 6;
        public const int TicksPer7Hours = TicksPerHour * 7;
        public const int TicksPer8Hours = TicksPerHour * 8;
        public const int TicksPer9Hours = TicksPerHour * 9;
        public const int TicksPer10Hours = TicksPerHour * 10;
        public const int TicksPer12Hours = TicksPerHour * 12;
        public const int TicksPer16Hours = TicksPerHour * 16;
        public const int TicksPer18Hours = TicksPerHour * 18;
        public const int TicksPer20Hours = TicksPerHour * 20;
        public const int TicksPer23Hours = TicksPerHour * 23;
        public const int TicksPer24Hours = TicksPerHour * 24;
        public const int TicksPer30Hours = TicksPerHour * 30;
        public const int TicksPer32Hours = TicksPerHour * 32;
        public const int TicksPer36Hours = TicksPerHour * 36;
        public const int TicksPer40Hours = TicksPerHour * 40;
        public const int TicksPer48Hours = TicksPerHour * 48;
        ///<summary>In Game Time</summary>
        public const int TicksPerMinute = 1;
        public const int TicksPer1Minutes = TicksPerMinute;
        public const int TicksPer2Minutes = TicksPerMinute * 2;
        public const int TicksPer3Minutes = TicksPerMinute * 3;
        public const int TicksPer5Minutes = TicksPerMinute * 5;
        public const int TicksPer10Minutes = TicksPerMinute * 10;
        public const int TicksPer15Minutes = TicksPerMinute * 15;
        public const int TicksPer20Minutes = TicksPerMinute * 20;
        public const int TicksPer30Minutes = TicksPerMinute * 30;
        public const int TicksPer40Minutes = TicksPerMinute * 40;
        public const int TicksPer45Minutes = TicksPerMinute * 45;
        public const int TicksPer50Minutes = TicksPerMinute * 50;
        public const int TicksPer55Minutes = TicksPerMinute * 55;
        public const int TicksPer60Minutes = TicksPerMinute * 60;
        public const int TicksPer90Minutes = TicksPerMinute * 90;
        ///<summary>In Game Time</summary>
        public const float TicksPerSecond = TicksPerMinute / 60f;
        public const float DefaultLongPressTime = 0.33f;
        public const float DefaultAudioMaxSilence = 60f;
        public const float AudioSilenceMinMultiplier = 0.1f;
        public static float LongPressTime => The.Prefs.GetFloat(
            Pref.GLongPressTime, PrefH.GLongPressTime, DefaultLongPressTime);

        public const string DateFormatShort = "yyyy-MM-dd";
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
        public const string DateTimeFormatAutoSave = "yyyy-MM-dd-HHmm";
        public const string AutoSavePrefix = "Autosave_";
        #if PRODUCTION_BUILD
        public static int MaxLoadCoroutines = 8;
        #else
        public static int MaxLoadCoroutines = 200000;
        #endif
        // Distance in pixels on starmap of how far shuttle can be from the base to
        // still be able to return to it
        public const float SpaceShuttleProximity = 500;
        public const float MaxAtmosphereOxygenSaturation = 0.21f;
        public const bool DefaultRunInBG = true;
        public const bool DefaultDragRelBuildCon = true;
        public static readonly Color ColorResearchRedText = new Color32(134, 19, 19, 255);
        public const float CaloriesToHungerMultiplier = 0.6f;
        public const float UIScrollRectPanSpeed = 100;
        #if !PRODUCTION_BUILD
        public static string ToDevDir(string dir) {
            if (The.TestsAreRunning) {
                return $"{dir}_Editor";
            } else {
                return $"{dir}_Editor_{DevVersion}";
            }
        }

        public static string SaveDirectory => ToDevDir("Saves");
        public static string PatchedDirectory => ToDevDir("Patched");
        #else
        public const string SaveDirectory = "Saves";
        public const string PatchedDirectory = "Patched";
        #endif
        public const float DragMinDist = 0.1f;
        public const float ToolRectFontSize = 3;
        public const int GridTileSize = 1; // Unity units
        public const int LightTextureScale = 1;
        public static readonly Dictionary<string, float> VolumeDefaults = new() {
            { Pref.GVolumeMaster, 1f },
            { Pref.GVolumeMusic, 0.5f },
            { Pref.GVolumeAmbience, 1f },
            { Pref.GVolumeFX, 1f },
            { Pref.GVolumeUI, 1f },
        };

        public static readonly Dictionary<string, bool> AudioSettingDefaults = new() {
            { Pref.GCutoffMusic, true },
            { Pref.GCutoffAmbience, true },
            { Pref.GCutoffFX, true },
            { Pref.GAudioUIHoverSounds, true },
            { Pref.GVoiceAlerts, true },
        };
        // Max chunk size
        //public const int GridChunkSize = 126;
        // TODO find optimal chunk size
        public const int GridChunkSize = 64;
        public const int MinIslandSize = 1;
        public const int MinAutoRescueIslandSize = 30;
        public const float MinOxygen = 0.05f; // in reality it's 19.5%
        public const float MinComfyOxygen = 0.15f; // in reality it's ~20%
        public const float MinBiologicalSafeTemp = 273 - 20; // -20 celsius
        public const float MaxBiologicalSafeTemp = 273 + 50; // +50 celsius
        public const float MinBiologicalComfyTemp = 273 + 10; // +15 celsius
        public const float MaxBiologicalComfyTemp = 273 + 40; // +35 celsius
        // Fire will go out when below this
        public const float FireMinTemp = 273 - 50; // -50 celsius
        public const float FireBaseTemp = 273 + 200; // 200 celsius
        public const string AmbientLightColorDefault = "Medium";
        public const string PostProcessingDefault = "Default";
        public const string DefaultShipComputerName = "ShipOS";
        public static readonly Color32 AmbientColorFogOfWar = new Color32(0, 0, 0, 255);

        // Used in shader to apply light on sources that are not lit by our lights (asteroids, shuttle flying, etc)
        public static readonly Color AmbientLitColor = new Color32(184, 171, 186, 255);
        public static readonly Color ColorTransparent = new(1, 1, 1, 0);
        public static readonly Color ColorBlack = new(0, 0, 0, 1);
        public static readonly Color ColorWhite = new(1, 1, 1, 1);
        public static readonly Color ColorNameHealthy = new(1, 1, 1, 1);
        public static readonly Color ColorNameInjured = new(0.72f, 0.08f, 0.08f, 1);
        public static readonly Vector4 ColorWhiteV4 = new Vector4(1, 1, 1, 1);
        public static readonly float4 ColorWhiteF4 = new float4(1, 1, 1, 1);
        public static readonly Color ResearchTreeLineColor = new(1, 1, 1, 0.5f);
        public static readonly Color StarmapLineColor = new Color32(230, 25, 148, 192);
        public static readonly Color ColorFireFrom = new Color32(249, 158, 50, 192);
        public static readonly float3 ColorFireFromF3 = new float3(249f/255f, 158f/255f, 50/255f);
        public static readonly Color ColorFireTo = new Color32(230, 117, 23, 225);
        public static readonly float3 ColorFireToF3 = new float3(230f/255f, 117f/255f, 23/255f);
        public static readonly Color ColorHeatFrom = new Color32(51, 92, 153, 128);
        public static readonly Color ColorHeatTo = new Color32(255, 0, 0, 192);
        public static readonly Color ColorOxygenFrom = new Color32(255, 0, 0, 128);
        public static readonly Color ColorOxygenTo = new Color32(71, 153, 51, 192);
        public static readonly Color ColorResFrom = new Color32(0, 0, 255, 128);
        public static readonly Color ColorResTo = new Color32(71, 153, 51, 192);
        public const bool LabelEnemyAshesDefault = false;
        public const bool DefaultSmoothDrag = true;
        public const bool DefaultSmoothZoom = true;
        public static readonly Vector2 HeatRange = new(
            Units.CelsiusToKelvin(-30),
            Units.CelsiusToKelvin(100));
        public static readonly Vector2 OxygenRange = new(0.03f, 0.25f);
        public static readonly Color ColorTurretRange = new Color32(255, 0, 0, 96);
        // a drafted shooter's reach: faint, since red read as a forbidden area
        public static readonly Color ColorWeaponRange = new Color32(220, 220, 220, 48);
        public static readonly Color DefaultLightColor = new Color32(128, 128, 128, 255);
        public static readonly Color AreaPreviewColor = new Color32(233, 175, 32, 32);
        public static readonly Color AreaOutlineColor = new Color32(64, 85, 191, 32);
        public static readonly Color FreeSelectRectColor = new Color32(32, 223, 80, 128);
        //jpublic static readonly Color FreeSelectBeingColor = new Color32(32, 223, 80, 128);
        public static readonly Color FreeSelectBeingColor = new Color(0.25f, 0.45f, 0.75f, 225f/256f);
        public static readonly Color FreeSelectEnemyColor = new Color32(204, 0, 0, 225);
        public static readonly Color FreeSelectObjColor = new Color32(32, 223, 80, 128);
        public static readonly Color FreeSelectTileColor = new Color32(32, 223, 80, 128);
        public static readonly Color FreeSelectTileHoverColor = new Color32(204, 122, 0, 64);
        public static readonly Color StoreRectColor = new Color32(255, 161, 0, 128);
        public static readonly Color BeingPathColor = new Color32(163, 194, 171, 64);
        public static readonly Color HyperQueuePathColorPassive = new Color32(255, 255, 255, 128);
        public static readonly Color HyperQueuePathColorActive = new Color32(59, 173, 31, 225);
        public static readonly Color HyperQueuePreviewPathColorPassive = new Color32(255, 255, 255, 128);
        public static readonly Color HyperQueuePreviewPathColorActive = new Color32(59, 173, 31, 128);
        public static readonly Color BeingPathPreviewColor = new Color32(16, 198, 6, 128);
        public static readonly Color BeingPathPreviewUnsafeColor = new Color32(247, 8, 8, 192);
        public static readonly Color EnergyConnectionColorTeleporter = new(255/255f, 140/255f, 0/255f, 1f);
        public static readonly Color EnergyConnectionColorTeleporterBidirectional = new(204/255f, 37/255f, 0/255f, 1f);
        public static readonly float4 EnergyConnectionColorConnectorF4 = new(32/255f, 80/255f, 223/255f, 128/255f);
        public static readonly float4 EnergyConnectionColorCreateF4 = new(223/255f, 32/255f, 175/255f, 192/255f);
        public static readonly Color EnergyConnectionColorConnector = new(32/255f, 80/255f, 223/255f, 128/255f);
        public static readonly Color EnergyConnectionColorCreate = new Color32(223, 32, 175, 192);
        public static readonly float4 EnergyConnectionColorCreateConnectF4 = new(128/255f, 223/255f, 32/255f, 128/255f);
        public static readonly Color EnergyConnectionColorCreateDisconnect = new Color32(223, 32, 32, 128);
        public static readonly float4 EnergyConnectionColorCreateDisconnectF4 = new(223/255f, 32/255f, 32/255f, 128/255f);
        public static readonly Color EnergyConnectionColorConsumer = new Color32(32, 224, 223, 128);
        public static readonly float4 EnergyConnectionColorConsumerF4 = new(32/255f, 224/255f, 223/255f, 128/255f);
        public static readonly Color EnergyConnectionColorConsumerDeficit = new Color32(134, 19, 19, 128);
        public static readonly float4 EnergyConnectionColorConsumerDeficitF4 = new(134/255f, 19/255f, 19/255f, 128/255f);
        public static readonly Color EnergyConnectionColorOff = new Color32(166, 195, 201, 128);
        public static readonly float4 EnergyConnectionColorOffF4 = new(166/255f, 195/255f, 201/255f, 128/255f);
        public static readonly Color EnergyConnectionColorProducer = new Color32(102, 197, 25, 128);
        public static readonly float4 EnergyConnectionColorProducerF4 = new(102/255f, 197/255f, 25/255f, 128/255f);
        public static readonly Color EnergyGridCoverageColor = new Color32(32, 80, 223, 32);
        public static readonly Color EnergyGridConnectorReachColor = new Color32(204, 163, 102, 64);
        public static readonly float4 EnergyGridConnectorReachColorF4 = new float4(204, 163, 102, 64) / 255f;
        public static readonly Color SelectionGreyColor = new Color32(56, 46, 52, 192);
        public static readonly Color SelectionFlashColorHalf = new Color32(223, 32, 175, 64);
        public static readonly Color SelectionFlashColorTransparent = new Color32(223, 32, 175, 0);
        public static readonly Color GridColor = new Color32(204, 255, 255, 64);
        public static readonly Color PlanGuidelinesColor = new Color32(204, 255, 255, 32);
        public static readonly Color PlanGuidelinesAccentColor = new Color32(204, 255, 255, 64);
        public static readonly Color PlanGuidelinesMirrorColor = new Color32(32, 223, 80, 64);
        public static readonly Color RedMarkedTileColor = new Color32(204, 0, 0, 192);
        public static readonly Color RedRectColor = new Color32(204, 0, 0, 64);
        public static readonly Color GreenRectColor = new Color32(41, 204, 0, 64);
        public static readonly Color GreenMarkedTileColor = new Color32(41, 204, 0, 192);
        public static readonly Color YellowMarkedTileColor = new Color32(204, 163, 0, 192);
        public static readonly Color YellowRectColor = new Color32(204, 122, 0, 64);
        public static readonly Color BlueRectColor = new Color32(64, 115, 191, 64);
        public static readonly Color WorkSpotColor = new Color32(99, 38, 115, 192);
        public static readonly Color OutPortColor = new Color32(70, 51, 153, 192);
        public static readonly Color InPortColor = new Color32(38, 115, 69, 192);
        public static readonly Color WorkSpotColor2 = new Color32(255, 255, 255, 128);
        public static readonly Color GridBoundsColor = new Color32(214, 194, 209, 64);
        public static readonly Color ColorHeaterRange = new Color32(204, 0, 0, 96);
        public static readonly Color ColorCoolerRange = new Color32(51, 128, 230, 96);
        public static readonly Color ColorOxygenRange = new Color32(45, 115, 38, 96);
        public static readonly Color ColorWorkRadiusRange = new Color32(230, 180, 40, 96);
        public static readonly Color ColorChartGreen = new Color32(51, 255, 0, 255);
        public static readonly Color ColorChartRed = new Color32(255, 0, 0, 255);
        public static readonly Color ColorChartBlueGreen = new Color32(0, 255, 255, 255);
        public static readonly Color ColorChartBlueBright = new Color32(0, 128, 255, 255);
        public static readonly Color ColorChartYellow = new Color32(255, 128, 0, 255);
        public static readonly Color ColorChartPurple = new Color32(191, 64, 140, 255);
        public static readonly Color ColorChartLightPurple = new Color32(217, 140, 186, 255);
        public const float EnergyConnectionThickness = 0.1f;
        public const float GridBorderThickness = 0.05f;
        public const int UndoHistorySize = 20;
        public const int NewsCharBudget = 2000;
        public const bool ZipSaves = true;
        //public const int InitialTileGraphicQueueSizeOverflowLimit = 1024;
        public const int MaxTileGraphicChangeSplit = 1024 * 4;
        public const int MinTileGraphicChangeSplit = 1024;
        // 0 - pause panel, 1 - close menu and pause, 2 - close menu only
        public const int DefaultEscBehavior = 1;
        /*
        // Big ship
        public const int MinShipWidth = 250;
        public const int MaxShipWidth = 275;
        public const int MinShipHeight = 100;
        public const int MaxShipHeight = 125;
        */
        /*
        // Small ship
        public const int MinShipWidth = 100;
        public const int MaxShipWidth = 125;
        public const int MinShipHeight = 50;
        public const int MaxShipHeight = 75;
        */
        // Medium ship
        public const int MinShipWidth = 250;
        public const int MaxShipWidth = 275;
        public const int MinShipHeight = 80;
        public const int MaxShipHeight = 100;
        // Big cells
        /*
        public const int MinShipCellWidth = 7;
        public const int MaxShipCellWidth = 11;
        public const int MinShipCellHeight = 7;
        public const int MaxShipCellHeight = 11;
        */
        // Small cells
        public static int MinShipCellWidth => Tunables.MinShipCellWidth;
        public static int MaxShipCellWidth => Tunables.MaxShipCellWidth;
        public static int MinShipCellHeight => Tunables.MinShipCellHeight;
        public static int MaxShipCellHeight => Tunables.MaxShipCellHeight;
        public const float AdvertDistanceCost = 1 / 1400f;
        public const float AdvertDistanceCostMax = 0.5f;
        // How much damage is applied from comfortable temp range difference
        public const float BeingHeatDamageFactor = 0.1f;
        public const float BeingFireDamageAmount = 5f;
        public const float TileHeatDamageFactor = 0.1f;
        public const float TileFireDamageAmount = 5f;
        public const float ObjHeatDamageFactor = 0.1f;
        public const float ObjFireDamageAmount = 5f;
        public const float MinLandingZoneIntegrity = 0.7f;
        public const float MinLandingZoneSize = 100;
        public static int MaxConcurrentPathfindingRequests =>
            Math.Max(Math.Min(8, SystemInfo.processorCount / 2), 2);
        public readonly static int SpawnBeingDelayFrom = Mathf.RoundToInt(
            Units.DaysToSeconds(10) * TicksPerSecond);
        public readonly static int SpawnBeingDelayTo = Mathf.RoundToInt(
            Units.DaysToSeconds(20) * TicksPerSecond);
        internal static readonly Color CursorLightColor = new(1, 1, 1, 0.33f);
        public const float DefaultRoomDimMul = 0.5f;
        public const float DefaultLightIntensity = 0.75f;
        public const float MinLightIntensity = 0.5f;
        public const float MaxLightIntensity = 1f;
        public const float MinPlantGrowthForHarvest = 0.6f;
        public static int ChartWidth => Math.Min(800, Screen.width / 2);
        public static int ChartHeight => Math.Min(600, Screen.height / 2);
        public static int DefaultUnits;
        public static int DefaultAutoRepairPriority = 3;
        public static int DefaultAutoRebuildPriority = 3;
        public static int DefaultBodyPartProcPriority = 4;
        public static bool AutoExpandTipsDefault = Screen.height > 1000;
        public const int MaxTooltipLength = 20000;
        public const float FloorMaxAirtight = 0.05f;
        // Lower than this will lose pressure
        public const float FloorMinAirtight = 0.025f;
        public const float FloorMaxInsulation = 0.05f;
        public const float WallMinAirtight = 0.9999f;
        public const int TintMaxVFX = 64;
        public const int SwarmMaxVFX = 20;
        public const int TeleportMaxVFX = 15;
        public const string TwitchTokenGeneratorURLBase = "https://twitchtokengenerator.com/";
        public const string TwitchTokenGeneratorURL = "https://twitchtokengenerator.com/?scope=chat%3Aread+chat%3Aedit+whispers%3Aread+whispers%3Aedit";
        public const string TwitchHelpURL = "https://docs.google.com/document/d/1j_ktqxRiaPwGMCK023O9dPNfdflfam-dVxu37H5CgGc/edit?usp=sharing";
        public const string DevVersion = "v15";
        public const int CurrentMinorVersion = 15;
        public const string LoadSaveUpdateVersion = "v0.15";
        public const string LoadSaveOldBranch = "v0_14_x";
        #if !PRODUCTION_BUILD
        public const string TestAutoUpgradeVersion = "v0.12";
        #endif
    }
}
