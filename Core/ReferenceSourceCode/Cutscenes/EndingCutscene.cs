using System.Collections;
using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Story.Events {
    /// <summary>
    /// The closing beats every ending shares: an epilogue scroll, a sign off, and the
    /// credits. Extracted so the Colonize and Rogue AI endings cannot drift apart.
    /// The caller owns the screen block before and after - this only writes into it.
    /// </summary>
    public static class EndingCutscene {
        public static IEnumerator RollCredits(GameState s, string epilogue,
                float epilogueScrollSpeed = 25f) {
            // Unlocked here rather than after the scroll, so a player who quits mid-credits
            // still keeps the run. Sandbox needs no guard - Unlock refuses there already.
            AchievementBeatScenario.CheckUnlock(s.Scenario);
            if (ModifierFogOfWar.IsEnabled) {
                The.AchievementTracker.Unlock(AchievementBeatFogOfWar.AchievementId);
            }
            if (s.Difficulty.Id == Difficulty.HardestId) {
                The.AchievementTracker.Unlock(AchievementBeatHardest.AchievementId);
            }
            if (!string.IsNullOrWhiteSpace(epilogue)) {
                yield return EndCredits.Scroll(s, epilogue, epilogueScrollSpeed);
            }
            The.ScreenBlock.SetCenterText(T.ThankYouForPlaying, 2f);
            #if PRODUCTION_BUILD
            yield return Consts.Wait3Sec;
            #else
            if (!The.TestsAreRunning) {
                yield return Consts.Wait3Sec;
            }
            #endif
            yield return The.ScreenBlock.HideCenterText(s, 1f, 2f);
            var credits = Strings.ParseSteamNews(Res.LoadText("Credits"),
                Texts.TMPColorAccented);
            yield return EndCredits.Scroll(s,
                $"<size=200%>{T.Credits}</size>\n\n{credits}");
        }
    }
}
