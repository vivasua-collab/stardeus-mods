using System.Collections.Generic;
using System.Text;
using Game.Achievements;
using Game.Data;
using Game.Input;
using Game.Utils;
using KL.Utils;

namespace Game.Mods {
    // Mods whose authors set "Cheat": true in ModInfo.json. Mods can only be toggled
    // with a restart, so a loaded cheat mod blocks achievements for the whole app session.
    // The block is set while mods load, before the main menu's background save loads,
    // so every game loaded or started in this session is marked too. Nothing reaches
    // disk until the player enters a game and it gets saved.
    public static class CheatMods {
        public const string WorkshopTag = "Cheat";
        public const string Note = "Marked as a cheat by its author. Achievements are disabled while this mod is enabled, and any game played with it keeps them disabled.";
        public static string Marker => Texts.Red("[Cheat]");

        public static void BlockAchievementsIfLoaded(IEnumerable<ModInfo> loaded) {
            List<ModInfo> cheats = null;
            foreach (var mod in loaded) {
                if (mod.Cheat) {
                    cheats ??= new List<ModInfo>();
                    cheats.Add(mod);
                }
            }
            if (cheats == null) { return; }
            D.Warn("Achievements blocked by cheat mods: {0}", string.Join(", ", cheats));
            var sb = new StringBuilder();
            sb.Append("These enabled mods are marked as cheats by their authors:\n");
            foreach (var mod in cheats) {
                sb.AppendFormat("\n{0}", ModLoader.ModName(mod));
            }
            sb.Append(GlyphMap.BRHR);
            sb.Append("Achievements stay disabled until you disable these mods and restart the game. Any game you play and save while they are enabled keeps achievements disabled, even after the mods are removed.");
            AchievementTracker.BlockGlobally(sb.ToString());
        }
    }
}
