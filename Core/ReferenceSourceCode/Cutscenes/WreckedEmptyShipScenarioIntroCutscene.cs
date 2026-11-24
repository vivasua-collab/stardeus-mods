using System.Collections;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using System;
using Game.Systems.Story;
using Game.Systems.Quests;
using Game.CodeGen;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Story.Events {
    public sealed class WreckedEmptyShipScenarioIntroCutscene {
        public WreckedEmptyShipScenarioIntroCutscene(GameState s, SpaceObject derelict,
                Action after) {
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
            Uploading = 1,
            Greetings = 2,
            ShipComputer = 3,
            Survivors = 4,
            StasisArray = 5,
            Derelict = 8,
            Tutorial = 7,
        }

        private EventStep NextStep(EventStep current) {
            switch (current) {
                case EventStep.Uploading: return EventStep.Greetings;
                case EventStep.Greetings: return EventStep.ShipComputer;
                case EventStep.ShipComputer: return EventStep.Survivors;
                case EventStep.Survivors: return EventStep.StasisArray;
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
            string text = null;
            // var text = $"story.wreck_empty_ship.intro.{(int) step}";
            switch ((int) step) {
                case 1: text = T.StoryWreck_Empty_ShipIntro1; break;
                case 2: text = T.StoryWreck_Empty_ShipIntro2_V1.F(Texts.Accented(S.ShipComputerName)); break;
                case 3: text = T.StoryWreck_Empty_ShipIntro3; break;
                case 4: text = T.StoryWreck_Empty_ShipIntro4; break;
                case 5: {
                    var stasisArrays = S.Components.GenFind<StasisArrayComp>();
                    int survivors = 0;
                    if (!Arrays.IsEmpty(stasisArrays)) {
                        foreach (var stasis in stasisArrays) {
                            survivors += stasis.BeingCount;
                        }
                    }
                    text = T.StoryWreck_Empty_ShipIntro5_V1.F(Texts.Accented(survivors));
                    break;
                }
                case 7: text = T.StoryWreck_Empty_ShipIntro7; break;
                case 8: text = T.StoryWreck_Empty_ShipIntro8; break;
                default: {
                    text = "...";
                    D.Err("Unhandled story wrecked empty ship intro step translation! {0} ({1})", step, (int) step);
                    break;
                }
            }

            // Final step with choice
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

            S.Query.SetQuestBool.Ask(QuestBoolIds.ReadyForTutorialQuests, true);
            S.Query.SetQuestBool.Ask(QuestBoolIds.DisallowApproachDerelict, true);

            // A.Tools.ClearNotificationDots(S);
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

        public static bool HintSurvivorArea(GameState S, bool focusCamera) {
            var aliveHuman = S.Beings.FindFirstMatching(
                b => b.Species.IsBiological && b.IsAlive);
            if (aliveHuman == null) {
                return false;
            }

            var area = S.Sys.Areas.AreaAt(aliveHuman.PosIdx, out var areaId);
            if (areaId < 0) {
                area = S.Sys.Areas.OuterAreaById(areaId);
            }
            if (areaId == 0 || Arrays.IsEmpty(area)) {
                S.Sig.CameraShowLink.Send(null, aliveHuman,
                    CameraFocus.FlashColor());
                S.Sig.CameraFocus.Send(aliveHuman.Position, new Vector2(10f, 10f),
                    Consts.CameraFocusZoomLevel);
            } else {
                S.Sig.CameraFocusTiles.Send(area);
            }
            return true;
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
                    #if !PRODUCTION_BUILD
                    // S.Clock.SetSpeed(ClockSpeed.Normal);
                    #endif
                    EntityUtils.CameraFocusOn(S.Components
                        .FindFirstByType<ShipComputerComp>()?.Entity);
                    break;
                }
                case EventStep.Survivors: {
                    if (!HintSurvivorArea(S, true)) {
                        step = EventStep.StasisArray;
                        SetStepConditions();
                    }
                    break;
                }
                case EventStep.StasisArray: {
                    var stasisArray = S.Components.FindFirstByType<StasisArrayComp>();
                    if (stasisArray == null) {
                        D.Err("Failed generating a stasis array for wrecked empty ship with seed: {0}", S.SeedStr);
                    } else {
                        EntityUtils.CameraFocusOn(stasisArray.Entity);
                    }
                    break;
                }
                case EventStep.Derelict: {
                    if (!StarfieldSpotlight.Begin(S, derelict)) {
                        S.Sig.CameraFocus.Send(
                            S.Grid.Center, S.Grid.Size, 300f);
                            // new Vector2(Consts.CornerMargin * 1.2f, Consts.CornerMargin * 1.2f),
                            // new Vector2(Consts.CornerMargin * 0.5f, Consts.CornerMargin), 240f);
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
