using System.Diagnostics;
using System.Runtime.CompilerServices;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using UnityEngine;
using System;
using Game;
using Unity.Mathematics;
using KL.Utils;

namespace Game.Utils {
    public static class GameTimer {
        [SelfInit]
        private static void Clear() {
            IgnoreBudget = false;
            unscaledDeltaTime = 0;
            deltaTime = 0;
            time = 0;
            unscaledTime = 0;
            realtimeSinceStartupAsDouble = 0;
            #if !PRODUCTION_BUILD
            TimesHadBudget = 0;
            TimesHadNoBudget = 0;
            #endif
        }
        private static readonly FPSCounter fpsCounter = new(0.5f);
        //public static readonly Stopwatch FrameTimer = new();
        private const float minFrameBudget = 1f/75f;
        // FPS rate compared to 60FPS (for animations)
        public static float FPSRate;
        private static float frameBudget;
        public static float unscaledTime;
        public static float unscaledDeltaTime;
        public static float FPS = 60;
        public static float time;
        public static float deltaTime;
        public static int frameCount;
        public static double realtimeSinceStartupAsDouble;
        public static double budgetTimeStart;
        public static bool IgnoreBudget;
        #if !PRODUCTION_BUILD
        private static uint TimesHadBudget;
        private static uint TimesHadNoBudget;
        #endif
        public static void Update() {
            FPSRate = 1f;
            unscaledTime = Time.unscaledTime;
            unscaledDeltaTime = Time.unscaledDeltaTime;
            if (Ready.Game) {
                fpsCounter.OnUpdate(unscaledDeltaTime);
                var fpsBefore = FPS;
                FPS = fpsCounter.CurrentFPS;
                if (FPS < 20f) {
                    D.Warn("Bumping FPS up to 20 from {0}. Ready: {1}", FPS, Ready.LongStatus);
                    FPS = 20f;
                }
                // More FPS = slower speed
                FPSRate = 60f / FPS;
                #if !PRODUCTION_BUILD
                // if (math.abs(fpsBefore - FPS) > 10) {
                //     D.Warn("FPS -> {0} -> {1} ({2})", fpsBefore, FPS, Ready.LongStatus);
                // }
                #endif
            }
            deltaTime = Time.deltaTime;
            frameBudget = unscaledDeltaTime * 0.5f;
            if (frameBudget < minFrameBudget) {
                frameBudget = minFrameBudget;
            }
            // D.Log("Updating stuff: {0}/{1}", unscaledDeltaTime, frameBudget);
            D.realTimeSinceStartup = realtimeSinceStartupAsDouble = Time.realtimeSinceStartupAsDouble;
            budgetTimeStart = 0f;
            time = Time.time;
            frameCount = Time.frameCount;
            #if !PRODUCTION_BUILD
            D.FrameCount = frameCount;
            #endif
            //FrameTimer.Restart();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAvailableFrameBudget(bool verbose = false) {
            if (IgnoreBudget) {
                return true;
            } else {
                #if !PRODUCTION_BUILD
                if (frameBudget == 0f) {
                    D.Err("FrameBudget is zero! {0}/{1}",
                        unscaledDeltaTime, frameBudget);
                    return true;
                }
                #endif
                var t = Time.realtimeSinceStartupAsDouble;
                if (budgetTimeStart == 0f) {
                    budgetTimeStart = t;
                }
                var elapsed = t - budgetTimeStart;
                #if PRODUCTION_BUILD
                return elapsed < frameBudget;
                #else
                if (elapsed < frameBudget) {
                    TimesHadBudget++;
                    if (verbose) {
                        D.Log("Had frame budget {0}/{1} ({2})",
                            TimesHadNoBudget, TimesHadBudget,
                            Units.PercentageWithFraction4(
                                TimesHadNoBudget / (float) TimesHadBudget));
                    }
                    return true;
                } else {
                    TimesHadNoBudget++;
                    if (verbose) {
                        D.Warn("Had no frame budget! {0}/{1} ({2})",
                            TimesHadNoBudget, TimesHadBudget,
                            Units.PercentageWithFraction4(
                                TimesHadNoBudget / (float) TimesHadBudget));
                    }
                    return false;
                }
                #endif
            }
        }

        public static void ResetFPS(int v = 0) {
            if (v == 0) {
                v = (int) Screen.currentResolution.refreshRateRatio.value;
            }
            // D.Warn("resetting FPS to {0}", v);
            FPS = v;
            fpsCounter.ResetTo(v);
        }
    }
}
