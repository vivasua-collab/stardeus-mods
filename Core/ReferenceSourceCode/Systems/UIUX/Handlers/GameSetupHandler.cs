using System;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Scenarios;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class GameSetupHandler : BaseWidgetHandler<GameSetupWidget> {
        public GameSetupHandler(GameState s, GameSetupWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowGameSetup.SetResponder(OnShowSetup);
        }

        private GameSetupData data;
        private Scenario scenario;

        private bool OnShowSetup(Scenario scenario, bool show) {
            if (show) {
                this.scenario = scenario;
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    DoBeginSetup();
                    return true;
                }
                #endif
                if (G.HasEnteredGame) {
                    // Unique: a double click spawned two, and the one left behind later
                    // started a new game through this handler of an abandoned game state
                    WPopup.Create(true, IconId.CWarning, T.GameNew, T.UnsavedProgressWarning)
                        .WithAction(T.Confirm, DoBeginSetup)
                        .WithAction2(T.Cancel, null)
                        .Unique("NewGameUnsavedProgress")
                            .Spawn(UIPoolRoot.SystemPopupContainer);
                    return false;
                } else {
                    DoBeginSetup();
                }
            } else {
                widget.CancelSetup();
            }
            return true;
        }

        private void DoBeginSetup() {
            // The old game is abandoned right here, long before CmdNewGame
            if (!Permadeath.TryCommitBeforeLeaving()) { return; }
            SandboxBlock_Camera.StopDemoFlyThrough();
            G.HasEnteredGame = false;
            G.IsDemoMode = true;
            S.Sys?.Ship?.DestroyAllVisuals();
            // Old game is abandoned, but its storm VFX (e.g. CometShower on the starfield
            // layer) would keep rendering behind the setup screen
            S.Sys?.Particles?.DestroyCustomParticles();
            data = new GameSetupData(scenario);
            widget.BeginSetup(data);
        }
    }
}
