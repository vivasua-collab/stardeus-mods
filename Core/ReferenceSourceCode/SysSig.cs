using System;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Platform.Presence;
using Game.Procgen;
using Game.Scenarios;
using Game.Systems.UIUX;
using Game.Visuals;
using KL.Signals;
using UnityEngine;
using UnityEngine.Video;

namespace Game {
    // WARNING every new signal here has to be system one (with true in constructor!!!!)
    public class SysSig {
        public Signal1<Sprite> ShowImageBlowout = new(nameof(ShowImageBlowout), true);
        public Signal1<VideoClip> ShowVideoBlowout = new(nameof(ShowVideoBlowout), true);
        public Signal0 HideImageBlowout = new(nameof(HideImageBlowout), true);
        public Signal0 BeforeReadyCore = new(nameof(BeforeReadyCore), true);
        public Signal1<Def> AfterDefInitialized = new(nameof(AfterDefInitialized), true);
        public Signal1<ActionType> BindingChanged = new(nameof(BindingChanged), true);
        public Signal1<string> AchievementSelected = new(nameof(AchievementSelected), true);
        public Signal1<Achievement> AchievementUnlocked = new(nameof(AchievementUnlocked), true);
        public Signal0 AchievementsLoadedLocal = new(nameof(AchievementsLoadedLocal), true);
        public Signal0 VirtualKeyboardDismissed = new(nameof(VirtualKeyboardDismissed), true);
        public Signal2<float, float> PanCamera = new(nameof(PanCamera), true);
        public Signal3<float, float, float> GamepadRumble = new(nameof(GamepadRumble), true);
        public Signal0 MouseSwitched = new(nameof(MouseSwitched), true);
        public Signal1<PresenceInfo> SetRichPresence = new(nameof(SetRichPresence), true);
        [Obsolete("Remove")]
        public Signal1<GameStateMeta> SaveSelected = new(nameof(SaveSelected), true);
        public Signal1<Scenario> ScenarioSelected = new(nameof(ScenarioSelected), true);
        public Signal0 GameSavesChanged = new(nameof(GameSavesChanged), true);
        public Signal1<bool> StarfieldCapture = new(nameof(StarfieldCapture));
        public Signal1<ShipFactoryReport> OnBuildShip = new(nameof(OnBuildShip), true);
        public Signal1<string> ModSelected = new(nameof(ModSelected), true);
        public Signal2<string, SharedSaveInfo> SharedSaveSelected = new(nameof(SharedSaveSelected), true);
        public Signal1<SpaceLooks> SetSpaceLooks = new(nameof(SetSpaceLooks), true);
        public Signal0 ToggleUI = new(nameof(ToggleUI), true);
        public Signal0 ToggleWatermark = new(nameof(ToggleWatermark), true);
        public Signal0 HideUI = new(nameof(HideUI), true);
        public Signal0 ShowUI = new(nameof(ShowUI), true);
        public Signal1<bool> TakeScreenshot = new(nameof(TakeScreenshot), true);
        public Signal1<int> GameIdChanged = new(nameof(GameIdChanged), true);
        public Signal1<string> SettingChanged = new(nameof(SettingChanged), true);
        public Signal1<AmbientLightDef> AmbientColorChanged = new(nameof(AmbientColorChanged), true);
        public Signal0 AfterPanelLoaded = new(nameof(AfterPanelLoaded));
        ///<summary>Never call this from the game!</summary>
        public Signal1<GameState> AfterLoadSaveState = new(nameof(AfterLoadSaveState), true);
        /// <summary>
        /// Int: CursorTypeH
        /// </summary>
        public Signal1<int> SetCursor = new(nameof(SetCursor), true);
        /// <summary>
        /// Int: CursorTypeH
        /// For changing cursor to finger while hovering links
        /// </summary>
        public Signal1<int> SetCursorOverride = new(nameof(SetCursorOverride), true);
        /// <summary>
        /// Params: SoundtrackMood.Something, bool "change track now"
        /// </summary>
        public Signal2<string, bool> SetMusicMood = new(nameof(SetMusicMood), true);
        public Signal0 StopMusic = new(nameof(StopMusic), true);
        public Signal0 FadeOutForIntro = new(nameof(FadeOutForIntro), true);
        public Signal0 PlayIntroTrack = new(nameof(PlayIntroTrack), true);
        /// <summary>
        /// Bool param is to tell if tooltip is for ui (true = for ui, false = for entity)
        /// </summary>
        public Signal2<string, bool> ShowTooltip = new(nameof(ShowTooltip), true);
        public Signal3<string, bool, float> ShowTooltipWithTimeout = new(nameof(ShowTooltipWithTimeout), true);
        /// <summary>
        /// Bool param is to tell if tooltip is for ui (true = for ui, false = for entity)
        /// </summary>
        public Signal2<Func<string>, bool> ShowDynamicTooltip = new(nameof(ShowDynamicTooltip), true);
        public Signal0 HideTooltip = new(nameof(HideTooltip), true);
        public Signal2<string, float> PlayUISound = new(nameof(PlayUISound), true);
        /// <summary>
        /// bool is "use global pitch"
        /// </summary>
        public Signal3<string, Vector2, bool> PlaySound = new(nameof(PlaySound), true);
        /// <summary>
        /// bool is "use global pitch"
        /// </summary>
        public Signal4<string, Vector2, bool, float> PlaySoundLoud = new(nameof(PlaySoundLoud), true);
        public Signal0 PlayNextTrack = new(nameof(PlayNextTrack), true);
        /// <summary>
        /// string id, Vector2 pos, bool useGlobalPitch, float volume
        /// </summary>
        public Signal4<string, Vector2, bool, float> PlaySoundVol = new(nameof(PlaySoundVol), true);
        public Signal0 ResolutionChanged = new(nameof(ResolutionChanged), true);
        public Signal0 MainMenuLoaded = new(nameof(MainMenuLoaded), true);
        public Signal2<GameSetupWidget, GameSetupData> GameSetupWidgetReady = new(nameof(GameSetupWidgetReady), true);
        public Signal2<GameSetupWidget, GameSetupData> GameSetupPhaseChanged = new(nameof(GameSetupPhaseChanged), true);
        public Signal0 TextLinkClicked = new(nameof(TextLinkClicked), true);
        public Signal0 GameCloned = new(nameof(GameCloned), true);
        /// <summary>
        /// Bool = show / hide
        /// </summary>
        public Signal2<string, bool> ShowLoadingScreen = new(nameof(ShowLoadingScreen), true);
        public Signal1<string> LoadingMsg = new(nameof(LoadingMsg), true);
        public Signal0 AdvanceLoadingStage = new(nameof(AdvanceLoadingStage), true);
        public Signal1<int> AddLoadingStages = new(nameof(AddLoadingStages), true);
        public Signal0 UIThemeReloaded = new(nameof(UIThemeReloaded), true);
    }
}
