#if !PRODUCTION_BUILD
// #define FAST_GAME_OVER
#endif
using System.Collections;
using Game.Constants;
using Game.Data;
using KL.Utils;
using Game.Achievements;
using Game.Utils;
using Game.Commands;

namespace Game.Story.Events {
    public sealed class GameOverCutscene {

        // beforeSave runs before anything is written, so a No Reload save captures its
        // outcome (the nuke destroying the ship)
        public GameOverCutscene(GameState s, IEnumerator beforeSave = null) {
            S = s;
            this.beforeSave = beforeSave;
            // The lost game's own flag is the guard, set before anything else runs. It keeps
            // ticking until the main menu replaces it, and a session-wide flag lowered before
            // that let it start a second game over, which then stayed raised for every later
            // game once its coroutine was killed
            if (s.IsGameOver) {
                D.Err("Trying to start game over coroutine more than once!");
                return;
            }
            s.IsGameOver = true;
            D.Warn("Staring game over coroutine");
            Coroutines.Start(ShowGameOver(), this, "ShowGameOver", false, false);
        }

        private readonly GameState S;
        private readonly IEnumerator beforeSave;

        private IEnumerator ShowGameOver() {
            bool isFastGameOver = false;
            #if FAST_GAME_OVER
            isFastGameOver = true;
            #elif !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                isFastGameOver = true;
            }
            #endif
            Ready.SetUserInput(false, "Game Over");
            var gameId = S.GameId;
            if (beforeSave != null) {
                yield return beforeSave;
            }
            if (Permadeath.IsRunning) {
                // Like any other event, the loss goes into the one save, so loading it
                // replays the loss instead of going back to before it
                yield return new CmdSaveGame(S, S.SaveName, false, false).ExecuteAsync();
            }
            The.AchievementTracker.Unlock(AchievementCPUDestroyed.AchievementId);
            if (isFastGameOver) {
                yield return null;
            } else {
                yield return Consts.Wait5Sec;
            }
            if (!GameState.IsCurrent(S) || S.GameId != gameId) {
                // Player already moved on
                yield break;
            }
            if (The.ScreenBlock == null) {
                yield break;
            }
            The.ScreenBlock.SetCenterText("", 0);
            The.ScreenBlock.SetSideText("");
            The.ScreenBlock.Show(5f, () => {
                The.ScreenBlock.SetCenterText(T.ShipComputerDestroyed, 3f);
            });
            for (int i = 0; i < 8; i++) {
                if (isFastGameOver) {
                    yield return null;
                } else {
                    yield return Consts.Wait1Sec;
                }
                // Another precaution against unexpected state change
                if (!GameState.IsCurrent(S) || S.GameId != gameId) {
                    yield break;
                }
            }
            if (The.ScreenBlock == null) {
                yield break;
            }
            if (isFastGameOver) {
                yield return The.ScreenBlock.HideCenterText(S, 1f, 1f);
            } else {
                yield return The.ScreenBlock.HideCenterText(S, 2f, 5f);
            }
            Ready.SetUserInput(true, "Game Over");
            yield return null;
            SaveLoadUtils.ReturnToDemo();
        }
    }
}
