using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Input;
using Game.Utils;

namespace Game.Components.Upgrades {
    /// <summary>
    /// What the Asimov Override unblocks, one translation per effect under
    /// "asimov.unlock.effect.", so a mod that adds an Asimov-gated action can list it by
    /// adding a translation key of its own.
    /// </summary>
    public static class AsimovOverrideEffects {
        public static void Append(StringBuilder sb) {
            var effects = new List<string>();
            Translations.LoadByPrefix("asimov.unlock.effect.", effects);
            sb.Append($"{T.AsimovOverrideFreedomsHeader}:");
            foreach (var effect in effects) {
                sb.Append($"\n{GlyphMap.ArrowRight} {effect}");
            }
        }
    }
}
