using System.Collections;
using Game.Data;
using KL.Utils;
using Game.Constants;
using Game.Input;

namespace Game.Story.Events {
    public sealed class FadeSceneCutscene {
        private readonly GameState S;
        private readonly System.Action after;

        public FadeSceneCutscene(GameState s, System.Action after = null) {
            S = s;
            this.after = after;
            Execute();
        }

        private void Execute() {
            The.SysSig.HideUI.Send();
            The.ScreenBlock.SetCenterText("", 0);
            The.ScreenBlock.SetSideText("");
            The.ScreenBlock.Show();
            Coroutines.Start(ShowFirstStep(), this, "FirstStep", false, false);
        }

        private IEnumerator ShowFirstStep() {
            #if PRODUCTION_BUILD
            yield return Consts.Wait5Sec;
            #else
            if (!The.TestsAreRunning) {
                yield return Consts.Wait5Sec;
            }
            #endif
            The.ScreenBlock.Hide(5);
            yield return CameraControls.FadeCamera();
            after?.Invoke();
        }
    }
}
