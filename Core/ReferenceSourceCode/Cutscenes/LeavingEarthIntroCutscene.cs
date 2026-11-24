using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Story.Events {
    public sealed class LeavingEarthIntroCutscene {
        public LeavingEarthIntroCutscene(GameState s, Action after) {
            S = s;
            Execute();
            this.after = after;
        }
        private readonly GameState S;
        private readonly Action after;
        private int currentDialogStep;

        public void Execute() {
            Ready.SetUserInput(false, "Story");
            S.Vars.IsShowingCutscene = true;
            currentDialogStep = 1;
            S.Clock.SetSpeed(ClockSpeed.Normal);
            SetStepConditions();

            var data = The.Dialog.ShowChoice(SpriteForCurrentStep(),
                T.StoryLeaving_EarthIntro1,
                    OnProgressDialog, OnSkipDialog,
                        T.Ok, T.Skip, canCloseWithEsc: true);
            data.OverrideSortOrder = 30;
        }
        private bool IsStepDelayed() {
            switch (currentDialogStep) {
                case 3:
                case 5:
                    return true;
            }
            return false;
        }

        private void ShowNextDialog() {
            if (!SetStepConditions()) {
                return;
            }
            // var text = $"story.leaving_earth.intro.{currentDialogStep}";
            string text = null;
            switch (currentDialogStep) {
                case 1: text = T.StoryLeaving_EarthIntro1; break;
                case 2: text = T.StoryLeaving_EarthIntro2_V1.F(Texts.Accented(S.ShipComputerName)); break;
                case 3: text = T.StoryLeaving_EarthIntro3; break;
                case 4: {
                    var stasisArrays = S.Components.GenFind<StasisArrayComp>();
                    int survivors = 0;
                    if (!Arrays.IsEmpty(stasisArrays)) {
                        foreach (var stasis in stasisArrays) {
                            survivors += stasis.BeingCount;
                        }
                    }
                    text = T.StoryLeaving_EarthIntro4_V1.F(Texts.Accented(survivors));
                    break;
                }
                case 5: text = T.StoryLeaving_EarthIntro5; break;
                default: {
                    text = "...";
                    D.Err("Unhandled story leaving earth intro step translation! {0}", currentDialogStep);
                    break;
                }
            }
            // Final step with choice
            if (IsStepDelayed()) {
                The.Dialog.HideAndShowWithActionDelayed(2f, SpriteForCurrentStep(),
                    text, OnProgressDialog, T.Ok);
            } else {
                var data = The.Dialog.ShowWithAction(SpriteForCurrentStep(),
                    text, OnProgressDialog);
                data.OverrideSortOrder = 30;
            }
        }

        private void OnSkipDialog() {
            Ready.SetUserInput(false, "Story");
            The.ScreenBlock.Hide();
            Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0f, 1f),
                this, "HideCenterText1", false, true);
            EntityUtils.CameraFocusOn(S.Components
                .FindFirstByType<ShipComputerComp>().Entity);
            currentDialogStep = 5;
            ShowNextDialog();
        }

        private void EndIntro() {
            The.Dialog.Hide();
            Ready.SetUserInput(true, "Story");
            // A.Tools.ClearNotificationDots(S);
            S.Vars.IsShowingCutscene = false;
            S.Sys.Scan.ScanDeepProgress.Set(0f);
            var addTension = 10f / S.Difficulty.Level;
            if (addTension > 30f) {
                addTension = 30f;
            }
            S.Query.SetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests, true);
            after();
        }

        private bool SetStepConditions() {
            if (currentDialogStep == 1) {
            } else if (currentDialogStep == 2) {
                Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0, 1),
                    this, "HideCenterText2", false, false);
                Coroutines.Start(CameraControls.FadeCamera(), this, "FadeCamera",
                    false, false);
                The.ScreenBlock.Hide();
            } else if (currentDialogStep == 3 || currentDialogStep == 5) {
                EntityUtils.CameraFocusOn(S.Components
                    .FindFirstByType<ShipComputerComp>().Entity);
            } else if (currentDialogStep == 4) {
                var stasisArray = S.Components.FindFirstByType<StasisArrayComp>();
                if (stasisArray == null) {
                    D.Err("No stasis array found in LeavingEarthIntro!");
                } else {
                    EntityUtils.CameraFocusOn(stasisArray.Entity);
                }
            } else if (currentDialogStep > 4) {
                EndIntro();
                return false;
            }
            return true;
        }

        private string SpriteForCurrentStep() {
            return The.Defs.Get(DefIds.ObjectsDevicesShipComputer).Preview;
        }

        private void OnProgressDialog() {
            currentDialogStep++;
            ShowNextDialog();
        }
    }
}
