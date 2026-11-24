using System.IO;
using KL.Utils;

namespace Game.Utils {
    // Backup files are a local crash recovery artefact for a save that still
    // exists. Once the save they belong to is gone, they are dead weight that
    // used to accumulate forever and get pushed to Steam Cloud on every sync.
    public static class SaveBackupCleanup {
        // Every suffix that can end up next to a "<base>.save":
        // Persist.WriteSafe renames the previous file to "<path>.backup",
        // CmdSaveGame.AddSaveBackup copies that to "<path>.backup2", and
        // SaveLoadUtils.CreateBackupFor writes the ".autobackup" pair.
        public static readonly string[] BackupSuffixes = {
            ".save.backup",
            ".save.backup2",
            ".save.autobackup",
            ".save.autobackup.backup",
            ".meta.autobackup",
            ".meta.autobackup.backup",
        };

        // saveFileNoExt is a save path without extension,
        // e.g. "<dir>/Autosave_2026-09-17-0302".
        // Deliberately silent: RotateAutoSaves runs inside the save path where
        // logging can break saving.
        public static void DeleteBackupsFor(string saveFileNoExt) {
            if (string.IsNullOrWhiteSpace(saveFileNoExt)) {
                return;
            }
            for (var i = 0; i < BackupSuffixes.Length; i++) {
                var path = $"{saveFileNoExt}{BackupSuffixes[i]}";
                try {
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                } catch (System.Exception) {
                    // A locked or missing backup is not worth failing a save over
                }
            }
        }

        // Removes backups whose "<base>.save" is gone from saveRootDir and its
        // per-game subdirectories. Takes the root explicitly so tests can point
        // it at a scratch folder. Returns how many files were removed.
        public static int SweepOrphanBackups(string saveRootDir) {
            if (!Directory.Exists(saveRootDir)) {
                return 0;
            }
            var removed = SweepDirectory(saveRootDir);
            foreach (var dir in Directory.GetDirectories(saveRootDir)) {
                removed += SweepDirectory(dir);
            }
            return removed;
        }

        private static int SweepDirectory(string dir) {
            var removed = 0;
            foreach (var path in Directory.GetFiles(dir)) {
                if (!TryGetSaveBase(path, out var saveBase)) {
                    continue;
                }
                if (File.Exists($"{saveBase}.save")) {
                    continue;
                }
                try {
                    File.Delete(path);
                    removed++;
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed removing orphan save backup: {0}", path);
                }
            }
            return removed;
        }

        private static bool TryGetSaveBase(string path, out string saveBase) {
            for (var i = 0; i < BackupSuffixes.Length; i++) {
                var suffix = BackupSuffixes[i];
                if (path.EndsWith(suffix, System.StringComparison.Ordinal)) {
                    saveBase = path.Remove(path.Length - suffix.Length);
                    return true;
                }
            }
            saveBase = null;
            return false;
        }
    }
}
