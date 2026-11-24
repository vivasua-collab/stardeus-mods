using System;
using System.Collections;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using KL.Clock;
using UnityEngine;

namespace Game.Systems.Story {
    // The first time somebody can be woken is the moment the game tells the player what
    // they are carrying: people, a number of them, and that waking one is their call. It
    // happens once per game and is deliberately short - two lines on a dark screen in
    // front of the array, then the usual choice.
    public static class FirstWakeUpSequence {
        #if !PRODUCTION_BUILD
        // Tests run without it (it is seconds of real time per wake-up test) unless they
        // are about the sequence itself
        public static bool PlayInTests;
        #endif

        public static bool ShouldPlay(GameState s) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning && !PlayInTests) { return false; }
            #endif
            return ManifestSys.For(s).WokenCount == 0;
        }

        public static IEnumerator Play(GameState s, Tile array, Action onDone) {
            // The wake-up event stops the clock from its own tick, which it skips while a
            // screen block is up, so it has to be stopped here
            s.Clock.SetSpeed(ClockSpeed.Stopped);
            s.Clock.ForceStop(ForceStopReason.StasisWakeUp);
            // Only with "focus on events", like every other wake-up: players turn it off on
            // purpose, and the two lines play on a dark screen that needs no array in view
            EntityUtils.CameraFocusOn(array, true);

            var block = The.ScreenBlock;
            block.SetCenterText("", 0);
            block.SetSideText(null);
            block.Show(1.5f);
            // Typed in like the scenario intro, once the dark screen is mostly in
            block.RevealCenterText(
                T.StasisFirstWakeupCare_V1.F(StasisUtils.TotalStoredBeingCount(s)), 1f);
            yield return block.WaitForCenterTextRead(s, 2.5f);
            if (!GameState.IsCurrent(s)) { yield break; }
            block.RevealCenterText(T.StasisFirstWakeupNow, 0.5f);
            yield return block.WaitForCenterTextRead(s, 2.5f);
            if (!GameState.IsCurrent(s)) { yield break; }
            yield return block.HideCenterText(s, 0f, 1f);
            if (!GameState.IsCurrent(s)) { yield break; }
            block.Hide(1f);
            yield return Beat(s, 1f);
            if (!GameState.IsCurrent(s)) { yield break; }
            onDone();
        }

        // Real time, because the clock is stopped for the whole sequence. Tests get the
        // same steps in the same order without the reading time, like the screen block
        // itself does.
        private static IEnumerator Beat(GameState s, float seconds) {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                seconds = 0.05f;
            }
            #endif
            var end = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < end) {
                yield return null;
                if (!GameState.IsCurrent(s)) { yield break; }
            }
        }
    }
}
