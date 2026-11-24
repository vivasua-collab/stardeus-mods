using Game.Constants;
using Game.Data;
using KL.Clock;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierNoFireSpread : Modifier {
        public const string ModifierId = "NoFireSpread";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;

        [SelfInit]
        private static void Register() {
            Register(new ModifierNoFireSpread());
        }

        public override void Apply(GameState s, bool isActive, bool _) {
            s.Prefs.SetBool(Pref.NoFireSpread, isActive);
        }
    }
}
