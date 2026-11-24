using System;
using System.Collections;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Quests;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Story.Events {
    // Intro for the New Beginning scenario. Same opening as the wrecked one, minus the
    // survivor beat (there is nobody awake aboard), and with the stasis beat rewritten:
    // the pods are stable, so the mandate is not "hurry before they die" but "build them
    // somewhere to live before any of them wakes up". That framing is the whole reason
    // the scenario exists, so it has to be said out loud in the first two minutes.
    public sealed class NewBeginningIntroCutscene {
        public NewBeginningIntroCutscene(GameState s, SpaceObject derelict, Action after) {
            S = s;
            this.derelict = derelict;
            Execute();
            this.after = after;
        }
        private readonly GameState S;
        private readonly SpaceObject derelict;
        private readonly Action after;

        private EventStep step;
        private enum EventStep {
            Uploading,
            Greetings,
            ShipComputer,
            Crew,
            StasisArray,
            Derelict,
            Tutorial,
        }

        private EventStep NextStep(EventStep current) {
            switch (current) {
                case EventStep.Uploading: return EventStep.Greetings;
                case EventStep.Greetings: return EventStep.ShipComputer;
                case EventStep.ShipComputer: return EventStep.Crew;
                case EventStep.Crew: return EventStep.StasisArray;
                case EventStep.StasisArray: return EventStep.Derelict;
                case EventStep.Derelict: return EventStep.Tutorial;
                default: {
                    D.Err("Unknown step transition from {0}", current);
                    break;
                }
            }
            return EventStep.Tutorial;
        }

        public void Execute() {
            Ready.SetUserInput(false, "Story");
            S.Vars.IsShowingCutscene = true;
            S.Clock.SetSpeed(ClockSpeed.Normal);
            step = EventStep.Uploading;
            SetStepConditions();

            // for trailer
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
            // The loading text is the first thing a new player reads, and the first
            // dialog used to cover it a second after loading. It is held for a few
            // seconds after it has been typed in, then slowly faded, and the story
            // starts once it is half gone. A click moves on sooner.
            yield return The.ScreenBlock.ReadAndFadeCenterText(S, 10f, 8f);
            if (!GameState.IsCurrent(S)) { yield break; }
            S.UIQuery.ToggleGameUI.Ask(false, false);
            The.ScreenBlock.ShowBlockedCanvas();
            var data = The.Dialog.ShowChoice(SpriteForCurrentStep(),
                T.StoryWreck_Empty_ShipIntro1,
                    OnProgressDialog, OnSkipDialog,
                        T.Ok, T.Skip, canCloseWithEsc: true);
            // break through the screen block
            data.OverrideSortOrder = 30;
        }

        private void ShowNextDialog() {
            SetStepConditions();
            if (step == EventStep.Tutorial) {
                // Queued before OnEndIntro sets ReadyForTutorialQuests: the first tutorial
                // quest waits for this popup to be read (QuestActivator), so the two never
                // compete for attention
                A.S.UIQuery.ShowTutorialPopup.Ask(OpeningCodexPage.PageId, 3f);
                OnEndIntro();
                The.Dialog.ShowSimple(SpriteForCurrentStep(),
                    T.TutorialEnd_V1.F(Texts.Accented(T.Codex)));
                return;
            }

            var text = TextForCurrentStep();
            if (IsStepDelayed()) {
                The.Dialog.HideAndShowWithActionDelayed(
                    2f, SpriteForCurrentStep(),
                        text, OnProgressDialog, T.Ok);
            } else {
                var dd = The.Dialog.ShowWithAction(SpriteForCurrentStep(),
                    text, OnProgressDialog);
                dd.OverrideSortOrder = 30;
            }
        }

        private string TextForCurrentStep() {
            switch (step) {
                case EventStep.Uploading:
                    return T.StoryWreck_Empty_ShipIntro1;
                case EventStep.Greetings:
                    return T.StoryWreck_Empty_ShipIntro2_V1.F(
                        Texts.Accented(S.ShipComputerName));
                case EventStep.ShipComputer:
                    return T.StoryWreck_Empty_ShipIntro3;
                case EventStep.Crew:
                    return T.StoryNew_BeginningIntroCrew;
                case EventStep.StasisArray:
                    return T.StoryNew_BeginningIntroStasis_V2.F(
                        Texts.Accented(CountSleepers()),
                        Texts.NoBr(Texts.Accented(T.ObjectsDevicesStasisarray)));
                case EventStep.Derelict:
                    return T.StoryWreck_Empty_ShipIntro8;
                default: {
                    D.Err("Unhandled New Beginning intro step translation! {0}", step);
                    return "...";
                }
            }
        }

        private int CountSleepers() {
            var stasisArrays = S.Components.GenFind<StasisArrayComp>();
            int sleepers = 0;
            if (!Arrays.IsEmpty(stasisArrays)) {
                foreach (var stasis in stasisArrays) {
                    sleepers += stasis.BeingCount;
                }
            }
            return sleepers;
        }

        private void OnSkipDialog() {
            S.UIQuery.ToggleGameUI.Ask(false, false);
            Ready.SetUserInput(false, "Story");
            The.ScreenBlock.Hide();
            Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0f, 1f),
                this, "HideCenterText3", false, false);
            EntityUtils.CameraFocusOn(S.Components
                .FindFirstByType<ShipComputerComp>().Entity);
            step = EventStep.Tutorial;
            ShowNextDialog();
            if (G.ClockSpeed < ClockSpeed.Unlimited) {
                Coroutines.Start(
                    CameraControls.FadeCamera(), this, "FadeCamera", false, true);
            }
        }

        private void EndEvent(bool unlock) {
            StarfieldSpotlight.End(S);
            The.Dialog.Hide();
            if (unlock) {
                S.UIQuery.ToggleGameUI.Ask(true, false);
                Ready.SetUserInput(true, "Story");
            }
            after?.Invoke();
        }

        private void OnEndIntro() {
            S.Vars.IsShowingCutscene = false;
            S.Sys.Scan.ScanDeepProgress.Set(0f);

            // Robot-only crew until the first stasis wake-up. Whether the Crafting Station
            // tutorial or its Assembler fallback is offered depends on whether any of these
            // bots can actually operate the station (the stock robot can). Must be decided
            // before ReadyForTutorialQuests, which is what lets the quest activator start
            // looking.
            QuestUtils.MarkCrewCannotCraftIfNeeded(S);
            S.Query.SetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests, true);
            S.Query.SetQuestBool.Ask(QuestBoolIds.DisallowApproachDerelict, true);

            EndEvent(true);
            var data = StoryEventData.CreateCustom(IconId.CComputing,
                T.StoryWreck_Empty_ShipLogTitle,
                T.StoryWreck_Empty_ShipLogDesc);
            S.Sig.AddCustomStoryEvent.Send(data, false);
        }

        private bool IsStepDelayed() {
            switch (step) {
                case EventStep.Uploading:
                case EventStep.ShipComputer:
                case EventStep.Tutorial:
                    return true;
            }
            return false;
        }

        private void SetStepConditions() {
            switch (step) {
                case EventStep.Greetings: {
                    S.UIQuery.ToggleGameUI.Ask(false, false);
                    Coroutines.Start(The.ScreenBlock.HideCenterText(S, 0, 1),
                        this, "HideCenterText4", false, false);
                    if (G.ClockSpeed < ClockSpeed.Unlimited) {
                        Coroutines.Start(CameraControls.FadeCamera(), this,
                            "FadeCamera", false, false);
                    }
                    The.ScreenBlock.Hide();
                    break;
                }
                case EventStep.ShipComputer:
                case EventStep.Tutorial: {
                    EntityUtils.CameraFocusOn(S.Components
                        .FindFirstByType<ShipComputerComp>()?.Entity);
                    break;
                }
                case EventStep.Crew: {
                    // Introduce an actual worker rather than the computer again - the
                    // first thing the player does is get these three powered up
                    var worker = S.Beings.FindFirstMatching(
                        b => b.IsAlive && !b.Species.IsBiological);
                    if (worker != null) {
                        EntityUtils.CameraFocusOn(worker);
                    }
                    break;
                }
                case EventStep.StasisArray: {
                    var stasisArray = S.Components.FindFirstByType<StasisArrayComp>();
                    if (stasisArray == null) {
                        D.Err("Failed generating a stasis array for new beginning with seed: {0}", S.SeedStr);
                    } else {
                        EntityUtils.CameraFocusOn(stasisArray.Entity);
                    }
                    break;
                }
                case EventStep.Derelict: {
                    if (!StarfieldSpotlight.Begin(S, derelict)) {
                        S.Sig.CameraFocus.Send(
                            S.Grid.Center, S.Grid.Size, 300f);
                    }
                    break;
                }
            }
        }

        private string SpriteForCurrentStep() {
            return The.Defs.Get(DefIds.ObjectsDevicesShipComputer).Preview;
        }

        private void OnProgressDialog() {
            if (step == EventStep.Derelict) {
                StarfieldSpotlight.End(S);
            }
            step = NextStep(step);
            ShowNextDialog();
        }
    }
}
