using Game.CodeGen;
using Game.Constants;
using Game.Rendering;
using KL.Utils;
using UnityEngine;

namespace Game.Utils {
    // Unity raises no event when the window is resized, toggled fullscreen, or
    // dragged to another display, so we compare a few cheap values on a timer.
    // Reactions to a live window resize lag by up to PollInterval, which is fine.
    // See Docs/Architecture/CorePlumbing/ScreenWatch.md
    public static class ScreenWatcher {
        private const float PollInterval = 0.5f;
        private static float nextPollTime;
        private static int lastW;
        private static int lastH;

        public static void Poll() {
            var now = Time.unscaledTime;
            if (now < nextPollTime) { return; }
            nextPollTime = now + PollInterval;
            PollSize();
            PollRefreshRate();
        }

        private static void PollSize() {
            // RenderUtils.UpdateResolution runs earlier in the same Bootstrap.Update
            var w = RenderUtils.ScreenWidth;
            var h = RenderUtils.ScreenHeight;
            if (w == lastW && h == lastH) { return; }
            var isFirst = lastW == 0;
            lastW = w;
            lastH = h;
            if (isFirst) { return; }
            Consts.AutoExpandTipsDefault = h > 1000;
            The.Prefs.Cache.UnsetBool(PrefH.GAutoExpandTooltips);
            The.SysSig.ResolutionChanged.Send();
        }

        private static void PollRefreshRate() {
            // QualitySettings.vSyncCount is a divisor of the current display's
            // refresh rate, not an FPS cap. A window dragged from a 60 Hz display
            // to a 240 Hz one keeps vSyncCount = 1 and silently runs at 240 FPS.
            var rate = Video.CurrentRefreshRate;
            if (rate == 0 || rate == Video.AppliedRefreshRate) { return; }
            D.Warn("Display refresh rate changed: {0} -> {1} Hz, reapplying VSync",
                Video.AppliedRefreshRate, rate);
            Video.ApplyVsync(false);
            GameTimer.ResetFPS();
        }
    }
}
