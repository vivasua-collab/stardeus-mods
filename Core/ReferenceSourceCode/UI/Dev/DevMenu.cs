using Game.Constants;
using Game.Input;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.UI {
    public static class DevMenu {
        private static bool hasWarnedAboutAchievements;

        [SelfInit]
        private static void Reset() {
            hasWarnedAboutAchievements = false;
        }

        public static void ShowMenu() {
            var s = The.R.State;
            #if !PRODUCTION_BUILD
            hasWarnedAboutAchievements = true;
            #endif
            if (!hasWarnedAboutAchievements && !s.IsAchievementsBlocked) {
                WPopup.Create(true, IconId.WDev, T.Warning, null, T.DevMenuAchievementsWarning)
                    .WithAction(T.Confirm, () => {
                        hasWarnedAboutAchievements = true;
                        s.UIQuery.ToggleDevMenu.Ask(true);
                    })
                    .WithAction2(T.Cancel, null)
                    .Spawn(center: true);
            } else {
                s.UIQuery.ToggleDevMenu.Ask(true);
            }
        }

        public static void ToggleInstantBuild(bool on) {
            if (A.InstantBuild == on) { return; }
            The.R.State.IsAchievementsBlocked = true;
            The.R.State.Prefs.SetBool(Pref.DevInstantBuild, on);
            The.R.State.Research.UpdateTierCounts();
            The.R.State.Sig.ResearchChanged.Send(null);
            if (on) {
                ShipComputerApp_DevMenu.WasAvailable = true;
                // Nothing ever calls Unlock() here, so the unlock effects behind the
                // "everything is researched" fiction have to be applied by hand.
                The.R.State.Research.ApplyAllUnlockEffects();
            }
            if (!A.InstantBuild) {
                UISounds.PlayDigitalCancel();
            }
            The.R.Sys.Electricity.RebuildGrid(true);
        }
    }
}
