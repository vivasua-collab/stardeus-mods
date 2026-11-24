using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;
using UnityEngine.CrashReportHandler;

namespace Game.Utils {
    internal static class SaveLoadValidation {
        public static bool IsOldGameVersion(GameStateMeta meta) {
            if (meta == null
                    || !VersionInfo.TryParse(Consts.Version, out var gameVersion)
                    || !VersionInfo.TryParse(meta.GameVersion, out var saveVersion)) {
                return false;
            }
            return gameVersion.Major > saveVersion.Major
                || gameVersion.Major == saveVersion.Major
                    && gameVersion.Minor > saveVersion.Minor;
        }

        // Old-save escape hatch is dev-only: release builds always block old saves.
        private static bool AllowOldSaves {
            get {
                #if PRODUCTION_BUILD
                return false;
                #else
                return The.Prefs.GetBool(Pref.GAllowOldSaves,
                    PrefH.GAllowOldSaves, false);
                #endif
            }
        }

        public static void LoadWithValidation(GameStateMeta meta,
                bool templateMode = false) {
            if (meta.SaveVersion != Consts.CurrentSaveVersion) {
                WPopup.Create(true, IconId.CCross, T.LoadFailed,
                    T.LoadFailedIncompatible_V1.F(meta.SaveName))
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                UISounds.PlayActionDenied();
                return;
            }

            if (IsOldGameVersion(meta)) {
                var displaySaveVersion =
                    VersionInfo.CleanVersion(meta.GameVersion);
                var displayGameVersion =
                    VersionInfo.CleanVersion(Consts.FullVersion);
                if (!AllowOldSaves) {
                    VersionInfo.TryParse(meta.GameVersion, out var saveVersion);
                    var switchBranchInfo = T.MajorUpdateOldBranch_V2.F(
                        Consts.LoadSaveUpdateVersion,
                        $"v{saveVersion.Major}_{saveVersion.Minor}_x");
                    var incompatibleInfo = T.LoadFailedIncompatible_V1.F(
                        Texts.Red(displaySaveVersion));
                    var gameVersionInfo = T.GameVersion_V1.F(
                        Texts.Green(displayGameVersion));
                    WPopup.Create(true, IconId.CWarning, T.LoadFailed, null,
                        $"{incompatibleInfo}<br><br>{gameVersionInfo}"
                            + $"{GlyphMap.BRHR}{switchBranchInfo}")
                        .Spawn(UIPoolRoot.SystemPopupContainer);
                    UISounds.PlayActionDenied();
                    return;
                }

                WPopup.Create(true, IconId.CWarning,
                    T.MajorUpdateWarning, null,
                    T.MajorUpdateWarningDesc_V2.F(
                        VersionInfo.CleanVersion(Consts.Version),
                        displaySaveVersion))
                    .WithAction(T.LoadSave, () =>
                        ValidateModsAndLoad(meta, templateMode,
                            backupOldSave: true,
                            disableExceptionCapture: true))
                    .WithAction2(T.Cancel, null)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                UISounds.PlayActionDenied();
                return;
            }

            ValidateModsAndLoad(meta, templateMode,
                backupOldSave: false, disableExceptionCapture: false);
        }

        private static void ValidateModsAndLoad(GameStateMeta meta,
                bool templateMode, bool backupOldSave,
                bool disableExceptionCapture) {
            if (!The.ModLoader.ValidateMods(meta, out var backupFile,
                    out var missingModsCount, out var missingModsList)) {
                WPopup.Create(true, IconId.CWarning,
                    T.MissingModsWarning_V1.F(missingModsCount), null,
                    T.MissingModsLoadWarning_V2.F(
                        backupFile, missingModsList))
                    .WithAction(T.LoadSave, () =>
                        Load(meta, templateMode,
                            disableExceptionCapture: true))
                    .WithAction2(T.Cancel, null)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                UISounds.PlayActionDenied();
                return;
            }

            if (backupOldSave) {
                var backup = SaveLoadUtils.CreateBackupFor(meta);
                D.Warn("Backup file created: {0}", backup);
            }
            Load(meta, templateMode, disableExceptionCapture);
        }

        private static void Load(GameStateMeta meta, bool templateMode,
                bool disableExceptionCapture) {
            if (disableExceptionCapture) {
                CrashReportHandler.enableCaptureExceptions = false;
            } else {
                Feedback.ApplyReportingChoice(The.Prefs.GetBool(Pref.GAutoReporting,
                    PrefH.GAutoReporting, false));
            }
            meta.IsTemplateMode = templateMode;
            SaveLoadUtils.LoadSave(meta);
        }
    }
}
