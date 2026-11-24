using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System;
using KL.Clock;
using Game.Systems.Mental;
using Game.Systems.Naming;
using Game.Systems.Story;
using Game.Data.Factions;
using System.Globalization;
using System.Linq;

namespace Game.Systems.Story {
    public sealed class StasisWakeUpStoryEvent : StoryEvent {
        public const string EventId = "StasisWakeUp";
        public override string Id => EventId;
        // More only while there are not enough people left to fill the choice
        private const int MaxPetsInChoice = 1;
        // The first wake-ups of a run are the colony's first new hands: people only, with
        // pets still filling the slots people cannot
        private const int PetFreeWakeUps = 2;
        #if !PRODUCTION_BUILD
        public static bool SkipInTests;
        #endif

        [SelfInit]
        private static void Register() {
            Register(new StasisWakeUpStoryEvent());
        }

        private StoryEventData currentEvent;
        private List<BeingData> currentChoice;
        // An array can wake somebody if somebody is in it and either the player has not told
        // it to hold on to them, or it lost power and cannot hold on to them anyway.
        private static readonly Func<StasisArrayComp, bool> canWake =
            (s) => s.BeingCount > 0 && (!s.IsPowerOK || !s.WakeUpsPrevented);
        private bool delayChoice => UIShowing.ScreenBlock || UIShowing.MainMenu
            || UIShowing.FullScreenWidget || !The.Toolbox.IsFreeSelect;

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            #if !PRODUCTION_BUILD
            if (SkipInTests) {
                if (The.TestsAreRunning) {
                    return false;
                }
            }
            #endif
            if (currentChoice != null) {
                D.Err("BeingChoiceUI is active while trying check conditions for another wake-up event!");
                return false;
            }
            return ctx.S.Components.FindAllMatching(Caches.StasisArrays, canWake) > 0;
        }

        private static Tile PickArray(StoryContext ctx, Rng rng) {
            var found = ctx.S.Components.FindAllMatching(Caches.StasisArrays, canWake);
            if (found == 0) { return null; }
            return Caches.StasisArrays[rng.Range(0, found)]?.Tile;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            if (currentChoice != null || currentEvent != null) {
                D.Err("BeingChoiceUI is active while trying to start another wake-up event!: {0} / {1}",
                    D.Dump(currentChoice), currentEvent);
                data.FailEvent(ctx.S, "Trying to start a second wake up while first one not ended!");
                return;
            }

            #if !PRODUCTION_BUILD
            if (SkipInTests && The.TestsAreRunning) {
                data.FailEvent(ctx.S, "Skipping in tests");
                return;
            }
            #endif

            data.SetState(ctx.S, StoryEventState.Ongoing);

            if (StasisWakeUpWarning.IsArmed(data)) {
                // The array is picked half a day early so that the warning can point at the
                // one that is about to open. SetupEvent picks again if it stops being able
                // to wake anyone meanwhile, which is exactly what the player now has time
                // to make happen.
                data.TargetTile = PickArray(ctx, new Rng(data.Seed));
                if (data.TargetTile == null) {
                    data.FailEvent(ctx.S, "No stasis array available",
                        isHardFailure: false);
                    return;
                }
                StasisWakeUpWarning.Announce(ctx, data);
                return;
            }

            if (delayChoice) { return; }
            if (SetupEvent(ctx, data)) {
                OnTick(ctx, data);
            }
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (StasisWakeUpWarning.IsPending(ctx, data)) {
                StasisWakeUpWarning.ShowNotification(data);
                return;
            }
            data.RemoveNotification();
            // A second wake-up can only get this far while the first one still owns the
            // choice UI, and SetupEvent would report that it is already running and let this
            // one stop the clock behind it. It waits instead - the warning window is long
            // enough for two wake-ups to overlap.
            if (currentEvent != null && currentEvent != data) { return; }
            if (delayChoice) { return; }
            if (!SetupEvent(ctx, data)) { return; }

            var s = ctx.S;

            if (Arrays.Count(currentChoice) < 1) {
                D.Err("Trying to show being choice UI with 0 options!");
                data.FailEvent(s, "Being choice empty");
                s.Clock.ForceResume(ForceStopReason.StasisWakeUp);
                Cleanup();
                return;
            }

            // Handle no-pause modifier
            s.Clock.SetSpeed(ClockSpeed.Stopped);
            s.Clock.ForceStop(ForceStopReason.StasisWakeUp);
        }

        private bool SetupEvent(StoryContext ctx, StoryEventData data) {
            if (data.State != StoryEventState.Ongoing) {
                D.Err("Refusing to setup a non-ongoing event: {0}", data);
                return false;
            }
            if (currentChoice != null) {
                // already running
                return true;
            }
            var rng = new Rng(data.Seed);
            // An announced wake-up picked its array when it warned the player. If that array
            // cannot wake anyone now, forget it and pick again below. Callers that set
            // TargetTile themselves keep the hard failure that follows.
            if (StasisWakeUpWarning.IsArmed(data) && data.TargetTile != null
                    && (!data.TargetTile.TryGetComponent<StasisArrayComp>(out var announced)
                        || !canWake(announced))) {
                data.TargetTile = null;
            }
            // figure out which stasis array we will use
            if (data.TargetTile == null) {
                data.TargetTile = PickArray(ctx, rng);
                if (data.TargetTile == null) {
                    data.FailEvent(ctx.S, "No stasis array available",
                        isHardFailure: false);
                    return false;
                }
            }

            if (data.TargetTile?.IsConstructed != true) {
                data.FailEvent(ctx.S, "No stasis array constructed");
                return false;
            }

            var bdatas = Caches.BeingDataList;
            bdatas.Clear();

            if (!data.TargetTile.TryGetComponent<StasisArrayComp>(out var targetArray)) {
                data.FailEvent(ctx.S, "Target tile is not a stasis array");
                return false;
            }
            var manifest = ManifestSys.For(ctx.S);
            var maxPets = manifest.WokenCount < PetFreeWakeUps ? 0 : MaxPetsInChoice;

            bool isFirst = FirstWakeUpSequence.ShouldPlay(ctx.S);

            if (ctx.S.Prefs.GetBool(Pref.SkipWakeUpChoice, PrefH.SkipWakeUpChoice, false)) {
                manifest.DrawCandidates(targetArray, rng, 1, bdatas, maxPets);
                if (bdatas.Count > 0 && isFirst) {
                    // No choice to make, but the first one is still the first one. Holding
                    // currentChoice keeps OnTick from setting the event up again meanwhile.
                    var chosen = bdatas[0];
                    currentEvent = data;
                    currentChoice = bdatas.ToList();
                    bdatas.Clear();
                    Coroutines.Start(FirstWakeUpSequence.Play(ctx.S, data.TargetTile, () => {
                        data.SetState(ctx.S, StoryEventState.Ending);
                        data.TargetBeing = chosen;
                        WakeUpBeing(ctx, data, chosen);
                        ctx.S.Clock.ForceResume(ForceStopReason.StasisWakeUp);
                        ctx.S.Clock.SetSpeed(ClockSpeed.Normal);
                        Cleanup();
                    }), this, "FirstWakeUp", false, false);
                    return true;
                }
                if (bdatas.Count > 0) {
                    data.SetState(ctx.S, StoryEventState.Ending);
                    WakeUpBeing(ctx, data, bdatas[0]);
                } else {
                    data.FailEvent(ctx.S, "Failed generating being for wakeup!");
                }
                bdatas.Clear();
                // we always want to return false since we don't show the UI later
                return false;
            }

            currentEvent = data;
            var numBeings = data.Def.Vars.Get(PropertyIdH.Items, 3);
            if (numBeings < 1) {
                D.Err("StasisWakeupStoryEvent has Items set to < 1! Can't have that: {0}", numBeings);
                numBeings = 3;
            }

            manifest.DrawCandidates(targetArray, rng, numBeings, bdatas, maxPets);

            if (bdatas.Count == 0) {
                data.FailEvent(ctx.S, "Nobody is asleep in the stasis array!");
                return false;
            }


            currentChoice = bdatas.ToList();
            bdatas.Clear();

            if (isFirst) {
                Coroutines.Start(FirstWakeUpSequence.Play(ctx.S, data.TargetTile, () => {
                    if (!ShowChoice(ctx, data)) {
                        ctx.S.Clock.ForceResume(ForceStopReason.StasisWakeUp);
                    }
                }), this, "FirstWakeUp", false, false);
                return true;
            }
            return ShowChoice(ctx, data);
        }

        private bool ShowChoice(StoryContext ctx, StoryEventData data) {
            var shown = ctx.S.UIQuery.ShowWakeUpChoice.Ask(currentChoice, (being) => {
                data.SetState(ctx.S, StoryEventState.Ending);
                data.TargetBeing = being;
                WakeUpBeing(ctx, data, being);
                ctx.S.Clock.ForceResume(ForceStopReason.StasisWakeUp);
                ctx.S.Clock.SetSpeed(ClockSpeed.Normal);
                Cleanup();
            });

            if (!shown) {
                data.FailEvent(ctx.S, "Failed showing stasis wakeup choice!");
                Cleanup();
                return false;
            }

            UISounds.PlayAchievement();
            if (A.FocusOnEvents) {
                EntityUtils.CameraFocusOn(data.TargetTile, true);
            }
            return true;
        }

        private void WakeUpBeing(StoryContext ctx, StoryEventData data, BeingData being) {
            if (data.TargetTile == null) {
                D.Err("OnChoose has no stasis array as target!");
                data.FailEvent(ctx.S, "OnChoose has no data.TargetTile");
                return;
            }
            if (!data.TargetTile.TryGetComponent<StasisArrayComp>(out var stasis)) {
                D.Err("StasisWakeUp Target Tile has no StasisArrayComp! {0}",
                    data.TargetTile);
                data.FailEvent(ctx.S, "OnChoose data.TargetTile is not StasisArrayComp");
                return;
            }
            data.TargetTile = stasis.Tile;
            // A colonist previously sent back into stasis (see StasisUtils) keeps their
            // mind and gets a lighter welcome. Someone on the manifest who was never woken
            // is still a stranger to the colony: sanity/AI alignment come from the array's
            // stability and a badly kept pod can turn them hostile.
            bool isReturning = !StasisUtils.IsUnrevealed(being);
            var b = StasisUtils.Wake(ctx.S, being, stasis,
                ctx.S.Difficulty.Level > 0.3f, ctx.Ticks);
            if (b != null) {
                // S.Sig.FollowEntity.Send(cmd.SpawnedBeing);
                if (A.FocusOnEvents) {
                    ctx.S.Sig.CameraFocus.Send(b.SelectPosition,
                        Consts.V2Half, Consts.CameraFocusZoomLevel);
                    ctx.S.Sig.SelectEntity.Send(b);
                }
                if (isReturning) {
                    var msg = T.StasisReturnedLog_V1.F(Texts.LinkToBeing(b.Data));
                    ctx.S.Sig.ShowFanfare.Send(msg);
                    ctx.S.Sys.Log.AddLine(b, msg);
                } else if (b.Data.IsPlayer) {
                    // A stranger from a badly kept array can wake up hostile instead
                    // (StasisArrayComp.ApplyWakeUpEffects). They did not join, and the raid
                    // announces itself.
                    BeingUtils.ShowColonyGreetingFor(ctx.S, b, data);
                    BeingUtils.AddInitialMentalBoostFor(b.Data, 7);
                }
            } else {
                D.Err("Failed to spawn a being from stasis: {0}", data.TargetTile);
                data.FailEvent(ctx.S, "Failed to spawn a being");
            }
        }

        public override void OnFinalize(StoryContext ctx, StoryEventData data) {
            data.RemoveNotification();
            // Announced, then cancelled: the array stopped being able to wake anyone during
            // the warning window, or the event timed out waiting. The aspect has already
            // spent this interval on it, so hand the interval back and let it ask again
            // tomorrow, the same as a wake-up that fails the moment it starts.
            if (data.State == StoryEventState.Failed && StasisWakeUpWarning.IsArmed(data)) {
                StasisWakeUpStoryAspect.For(ctx)?.RetryAnnouncedWakeUp(ctx);
            }
            base.OnFinalize(ctx, data);
        }

        protected override void Cleanup() {
            currentEvent = null;
            currentChoice = null;
        }
    }
}
