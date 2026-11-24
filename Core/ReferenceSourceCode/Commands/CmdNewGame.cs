using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.Story;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using Game.Commands;
using KL.Signals;
using KL.Utils;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Systems.Story;
using Game.Systems.UIUX;
using System;
using Game.Modifiers;
using KL.Randomness;

namespace Game.Commands {
    // 689574313 has a gen failure
    public class CmdNewGame : AsyncCommand {
        private readonly GameSetupData config;
        public override bool TrySameFrame => false;
        public CmdNewGame(GameSetupData config) : base("new_game", system: true) {
            if (!config.IsConfigured) {
                D.Throw("Can't create new game with unconfigured GameSetupData! {0}", config);
            }
            this.config = config;
        }
        private void AdvanceStage() {
            The.SysSig.AdvanceLoadingStage.Send();
        }
        // Counted from the start of the loading screen fade: 1 s after it is gone
        public const float IntroTrackDelaySeconds = LoadingScreen.FadeOutSeconds + 1f;

        public override IEnumerator ExecuteAsync() {
            // The music goes quiet as soon as the player commits to a new game, and the
            // intro track starts once the loading screen is gone
            Sounds.FadeOutForIntro();
            // Every attempt, including the retry after a gen failure below, starts from the
            // crew the player picked
            config.RestoreStartingBeings();
            Application.runInBackground = true;
            var curScene = SceneManager.GetActiveScene();
            The.BB.Set(BBKeyH.GlobalKeepBB, true);
            if (curScene.name == "Game") {
                The.SysSig.ShowLoadingScreen.Send("New game", true);
                A.S.UIQuery.ShowMainMenu.Ask(false);
                A.S.Clock.ForceStop(KL.Clock.ForceStopReason.MenuPanel);
                yield return null;
                // Cleanup in case we quit with material being set
                FullScreenPassVFXRendererFeature.RemovePass();

                The.CleanupAfterGame();
                yield return MiscUtils.LoadSceneTimedAsync("Empty");
            } else {
                yield return null;
            }
            AdvanceStage();

            The.PrepareForGame();
            S = The.R.State;

            yield return MiscUtils.LoadSceneTimedAsync("Game");
            AdvanceStage();

            Scenario scenario = config.Scenario;
            S.Scenario = config.Scenario;
            ModifierDifficulty.SetDifficulty(config.Difficulty);
            S.Difficulty = config.Difficulty;
            // The setup screen only drafts the No Reload choice, the modifier is what
            // the game applies and saves
            ModifierNoReload.Instance.IsActive = config.IsPermadeath;

            The.R.Sys.Initialize(The.R, null);

            S.UIQuery.HideMainMenuInstant.Ask();
            The.ScreenBlock.Show();

            #if !PRODUCTION_BUILD
            //S.IsPermadeath = false;
            The.Prefs.SetBool(Pref.GAutoSavePeriodic, false);
            S.Prefs.SetBool(Pref.AutoSaveOnExit, false);
            #endif

            // S.Sig.BeforeInitState.Send();
            yield return S.New(config);
            AdvanceStage();
            yield return scenario.Setup(S, config);
            AdvanceStage();

            Ready.SetGame(true, "Done new game");

            int attempts = The.BB.Get(BBKeyH.GlobalGenAttempt, 0);
            if (scenario?.IsGenFailure == true) {
                if (attempts > 10) {
                    try {
                        A.S.UIQuery.ShowMainMenu.Ask(true);
                        The.SysSig.ShowLoadingScreen.Send("Error", false);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed showing main menu after gen failure");
                    }
                    WPopup.Create(true, IconId.CWarning, T.GenFailed,
                        T.GenFailedInfo).Spawn(true, UIPoolRoot.SystemPopupContainer);
                    The.BB.Set(BBKeyH.GlobalKeepBB, false);
                    RestoreRunInBackgroundChoice();
                    yield break;
                }

                The.SysSig.ShowLoadingScreen.Send("New game", true);
                Coroutines.Start(new CmdNewGame(config).ExecuteAsync(),
                    "GameSetup", "NewGame", false, true);
                // // D.Warn("Abandoning new game due to gen failure #{0}", attempts);
                // MiscUtils.LoadSceneTimed("Empty");
                // MiscUtils.LoadSceneTimed("Game");
            } else {
                // The.BB.Remove(BBKeyH.GlobalScenario, null);
                // The.BB.Remove(BBKeyH.GlobalStoryGen, null);
                // var mainMenu Registries.UIWidgets.Get(UIWidgetId.MainMenu)
                yield return null;
                The.SysSig.ShowLoadingScreen.Send("Loading complete", false);
                // It checks for the game it was started for itself: another new game started
                // within the delay (a manifest retry) only skips the track, it is no leak
                var introCrt = Coroutines.Start(PlayIntroTrackAfterLoading(S), this,
                    "IntroTrack", false, false);
                introCrt.NotImportant = true;
                introCrt.CanRunOverMultipleGameIds = true;
                The.BB.Set(BBKeyH.GlobalKeepBB, false);
                RestoreRunInBackgroundChoice();
            }
        }

        private static IEnumerator PlayIntroTrackAfterLoading(GameState state) {
            yield return new WaitForSecondsRealtime(IntroTrackDelaySeconds);
            if (!GameState.IsCurrent(state)) { yield break; }
            Sounds.PlayIntroTrack();
        }

        private void RestoreRunInBackgroundChoice() {
            Application.runInBackground = The.Prefs.GetBool(
                Pref.GRunInBG, PrefH.GRunInBG, Consts.DefaultRunInBG);
        }

        // difficultyId null keeps the scenario's own Quick Start difficulty
        public static void QuickStartFor(Scenario scenario, string difficultyId = null) {
            if (!Permadeath.TryCommitBeforeLeaving()) { return; }
            var data = GameSetupData.CreateNewFor(scenario, difficultyId);
            data.IsQuickStart = true;

            var seeds = scenario.QuickStartSeeds;
            if (!Arrays.IsEmpty(seeds)) {
                var seed = Rng.UFrom(seeds);
                D.Warn("Quick start seed: {0}", seed);
                data.SetShipType(seed);
            }

            var cmd = new CmdNewGame(data);
            Coroutines.Start(cmd.ExecuteAsync(),
                "GameSetup", "NewGame", false, true);
        }
    }
}
