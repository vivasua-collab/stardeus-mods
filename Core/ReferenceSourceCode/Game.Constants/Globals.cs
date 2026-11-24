#if !PRODUCTION_BUILD
// #define DEBUG_G_TICKS
// #define DEBUG_G_DRAG
#endif
using System;
using KL.Utils;

namespace Game.Constants {
    /// <summary>
    /// Globals
    /// </summary>
    public static class G {
        #if DEBUG_G_TICKS
        private static long ticks;
        public static long Ticks {
            get {
                if (ticks == 0) {
                    D.Err("Getting G.Ticks too early!");
                }
                return ticks;
            }
            set {
                if (ticks == 0) {
                    D.Warn("Initializing G.Ticks: {0}", value);
                }
                ticks = value;
            }
        }
        #else
        public static long Ticks;
        #endif
        public static ClockSpeed ClockSpeed;
        public static float AnimSpeedMul;
        public static bool IsDemoMode;
        public static bool HasEnteredGame;
        #if DEBUG_G_DRAG
        private static bool isClickDrag;
        public static bool IsClickDrag {
            get => isClickDrag;
            set {
                if (isClickDrag != value) {
                    D.Log("Setting G.IsClickDrag: {0}", value);
                }
                isClickDrag = value;
            }
        }
        #else
        public static bool IsClickDrag;
        #endif
        /// <summary>
        /// Hacky shit, to pass the click down fact on starmap BG to the UserInput
        /// </summary>
        public static bool HasClickedDownInStarmap;
        // A UI element that acts on the right button (job priority cells lower a priority)
        // took this press, so UserInput must not also treat its release as cancel, which
        // would close the very UI it was on
        public static bool RightClickTakenByUI;

        public static void Cleanup() {
            IsDemoMode = false;
            HasClickedDownInStarmap = false;
            RightClickTakenByUI = false;
            IsClickDrag = false;
            HasEnteredGame = false;
            Ticks = 0;
            ClockSpeed = ClockSpeed.Stopped;
            AnimSpeedMul = 1f;
        }
    }
}
