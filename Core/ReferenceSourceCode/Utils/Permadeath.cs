using System.Collections;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using KL.Utils;
using UnityEngine;

namespace Game.Utils {
    // No Reload (commitment) keeps one save per colony and no way back to an older state.
    // The main menu is drawn over the live game, so every way out of it (load, new game)
    // has to write the colony first, the same as Exit does. Otherwise the player returns
    // to the older save later, which is a reload.
    public static class Permadeath {
        // A permadeath colony that is only the menu background is not running: nothing
        // happened since it was loaded, so memory and disk still match.
        public static bool IsRunning => G.HasEnteredGame && !G.IsDemoMode
            && !A.IsSaveTemplate && GameState.IsCurrent(A.S) && A.S.IsPermadeath;

        // Saves the running permadeath colony into its one save before the caller leaves
        // it. Sync and without a screenshot: the async save closes the main menu to capture
        // the screen, and Custom Start runs inside the menu. False means the caller must
        // not leave, because the colony was not written.
        public static bool TryCommitBeforeLeaving() {
            if (!IsRunning) { return true; }
            if (CmdSaveGame.IsRunningNow != null || !Ready.Save) {
                WPopup.SystemWarning(T.OperationAborted, $"{T.MenuSave}: {T.Waiting}");
                return false;
            }
            if (!TrySave(A.S, out var cmd)) {
                WPopup.SystemWarning(IconId.CWarning, T.SaveFailed, cmd.Result.FilePath,
                    cmd.Result.Exception);
                return false;
            }
            return true;
        }

        // Saves on window close and Alt+F4 as well, otherwise
        // quitting would be a way back to the last periodic save. A killed process still is.
        public static bool OnWantsToQuit() {
            #if !PRODUCTION_BUILD
            // The test player quits by itself when its run ends
            if (The.TestsAreRunning) { return true; }
            #endif
            return CommitBeforeQuit();
        }

        // True when the app may quit now
        public static bool CommitBeforeQuit() {
            if (!IsRunning || A.S.IsSavedForExit) { return true; }
            // A periodic save is still writing the one file on another thread, and there
            // is no backup copy of it in permadeath. Quit again once it is done.
            if (CmdSaveGame.IsRunningNow != null) {
                Coroutines.StartDirect(QuitAfterSave());
                return false;
            }
            // A failed save must not keep the window from closing
            if (!TrySave(A.S, out var cmd)) {
                D.Err("Failed saving No Reload game on quit: {0} {1}", cmd.Result.FilePath,
                    cmd.Result.Exception);
            }
            return true;
        }

        private static IEnumerator QuitAfterSave() {
            var deadline = Time.realtimeSinceStartupAsDouble + 30d;
            while (CmdSaveGame.IsRunningNow != null
                    && Time.realtimeSinceStartupAsDouble < deadline) {
                yield return null;
            }
            // A save that never cleared its marker would otherwise defer every quit forever
            if (CmdSaveGame.IsRunningNow != null) {
                D.Err("Save still running after 30 s, quitting anyway: {0}",
                    CmdSaveGame.IsRunningNow);
                CmdSaveGame.AbortSave();
            }
            Application.Quit();
        }

        private static bool TrySave(GameState s, out CmdSaveGame cmd) {
            cmd = new CmdSaveGame(s, s.SaveName, false, willExit: true);
            cmd.ExecuteSync(false);
            return cmd.IsCompleted;
        }
    }
}
