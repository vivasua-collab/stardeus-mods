using System;
using System.Collections;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Quests;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Utils;
using UnityEngine;

namespace Game.Story.Events {
    public sealed class CargoShipIntroCutscene {
        private readonly GameState S;
        private readonly Action after;
        private StepId currentDialogStep;
        private object param;

        public CargoShipIntroCutscene(GameState s, Action after) {
            S = s;
            this.after = after;
            Execute();
        }

        private enum StepId {
            Start,
            Welcome,
            ShipCPU,
            Stasis,
            Bye
        }

        private string MessageForStep(StepId stepId, object param = null) {
            switch (stepId) {
                case StepId.Start: return T.StoryWreck_Empty_ShipIntro1;
                case StepId.Welcome: return T.StoryCargo_ShipIntro_V1.F(Texts.Accented(S.ShipComputerName));
                case StepId.ShipCPU: return T.StoryLeaving_EarthIntro3;
                case StepId.Stasis: return T.StoryLeaving_EarthIntro4_V1.F(param);
                case StepId.Bye: return T.StoryLeaving_EarthIntro5;
            }
            return null;
        }

        public void Execute() {
            Ready.SetUserInput(false, "Story");
            S.Vars.IsShowingCutscene = true;
            currentDialogStep = StepId.Start;
            SetStepConditions();

            if (!UIShowing.ScreenBlock) {
                The.ScreenBlock.SetCenterText("", 0);
                The.ScreenBlock.SetSideText("");
                The.ScreenBlock.Show();
            }
            Coroutines.Start(ShowFirstStep(), this, "FirstStep", false, false);
        }

        private IEnumerator ShowFirstStep() {
#if PRODUCTION_BUILD
            yield return Consts.Wait1Sec;
#else
            if (!The.TestsAreRunning) {
                yield return Consts.Wait1Sec;
            }
#endif
            S.UIQuery.ToggleGameUI.Ask(false, false);
            The.ScreenBlock.ShowBlockedCanvas();
            var data = The.Dialog.ShowChoice(SpriteForCurrentStep(),
                T.StoryLeaving_EarthIntro1,
                OnProgressDialog, OnSkipDialog,
                T.Ok, T.Skip, canCloseWithEsc: true);
            data.OverrideSortOrder = 30;
        }

        private bool IsStepDelayed() {
            switch (currentDialogStep) {
                case StepId.ShipCPU:
                case StepId.Bye:
                    return true;
            }
            return false;
        }

        private void ShowNextDialog() {
            if (!SetStepConditions()) {
                return;
            }
            var text = MessageForStep(currentDialogStep, param);
            if (text != null) {
                if (IsStepDelayed()) {
                    The.Dialog.HideAndShowWithActionDelayed(2f, SpriteForCurrentStep(),
                        text, OnProgressDialog, T.Ok);
                } else {
                    var data = The.Dialog.ShowWithAction(SpriteForCurrentStep(),
                        text, OnProgressDialog);
                    data.OverrideSortOrder = 30;
                }
            } else {
#if !PRODUCTION_BUILD
                D.Err("Premature end of dialog! Not translation for Current step: {0}", currentDialogStep);
#endif
                EndIntro();
            }
        }

        private void OnSkipDialog() {
            Ready.SetUserInput(false, "Story");
            The.ScreenBlock.Hide();
            Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0f, 1f),
                this, "HideCenterText1", false, true);
            EntityUtils.CameraFocusOn(S.Components
                .FindFirstByType<ShipComputerComp>().Entity);
            currentDialogStep = StepId.Bye;
            ShowNextDialog();
        }

        private void EndIntro() {
            The.Dialog.Hide();
            Ready.SetUserInput(true, "Story");
            // A.Tools.ClearNotificationDots(S);
            S.UIQuery.ToggleGameUI.Ask(true, false);
            S.Vars.IsShowingCutscene = false;
            S.Sys.Scan.ScanDeepProgress.Set(0f);
            var addTension = 10f / S.Difficulty.Level;
            if (addTension > 30f) {
                addTension = 30f;
            }
            // Robot-only crew. Whether the Crafting Station tutorial or its Assembler
            // fallback is offered depends on whether any of these bots can actually operate
            // the station (the stock robot can). Must be decided before
            // ReadyForTutorialQuests, which is what lets the quest activator start looking.
            QuestUtils.MarkCrewCannotCraftIfNeeded(S);
            S.Query.SetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests, true);
            after?.Invoke();
        }

        private bool SetStepConditions() {
            param = null;
            if (currentDialogStep == StepId.Start) {
                S.Clock.SetSpeed(ClockSpeed.Fast2);
                S.Clock.ExecuteAfterTicks(40, () => {
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                });
            } else if (currentDialogStep == StepId.Welcome) {
                Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0, 1),
                    this, "HideCenterText2", false, false);
                Coroutines.Start(CameraControls.FadeCamera(), this, "FadeCamera",
                    false, false);
                The.ScreenBlock.Hide();
            } else if (currentDialogStep == StepId.ShipCPU || currentDialogStep == StepId.Bye) {
                EntityUtils.CameraFocusOn(S.Components
                    .FindFirstByType<ShipComputerComp>().Entity);
            } else if (currentDialogStep == StepId.Stasis) {
                var stasisArray = S.Components.FindFirstByType<StasisArrayComp>();
                if (stasisArray == null) {
                    D.Err("No stasis array found in Cargo Ship intro!");
                } else {
                    param = stasisArray.BeingCount;
                    EntityUtils.CameraFocusOn(stasisArray.Entity);
                }
            } else {
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
