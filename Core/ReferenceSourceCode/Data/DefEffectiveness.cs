using System.Text;
using Game.Components;
using Game.Constants;
using Game.CodeGen;
using Game.Utils;
using KL.Utils;

namespace Game.Data {
    // Heaters, coolers and oxygen pumps pin the tiles around them to a target value, so
    // their Rate has no meaning a player could compare. Their defs carry a hand-assigned
    // Effectiveness level instead
    public static class DefEffectiveness {
        public static bool AppendTo(StringBuilder sb, Def def, ComponentConfig config,
                bool addedAnything) {
            if (config == null) { return addedAnything; }
            var level = config.GetInt(PropertyIdH.Effectiveness, 0);
            if (level == 0) { return addedAnything; }
            Def.PrependAdditionalInfo(sb, addedAnything);
            sb.Append(Texts.NoBr($"{T.NounEffectiveness}: <b>{LevelText(level, def)}</b>"));
            return true;
        }

        private static string LevelText(int level, Def def) {
            switch (level) {
                case 1: return T.EffectivenessWeak;
                case 2: return T.EffectivenessMedium;
                case 3: return T.EffectivenessHigh;
                default:
                    D.Err("Unknown Effectiveness level {0} in def {1}", level, def.Id);
                    return T.Unknown;
            }
        }
    }
}
