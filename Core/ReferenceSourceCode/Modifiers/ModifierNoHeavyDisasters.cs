using Game.Constants;
using Game.Data;
using KL.Clock;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierNoHeavyDisasters : Modifier {
        public const string ModifierId = "NoHeavyDisasters";
        public override int SortOrder => 66;
        public override string GroupName => T.SituationStoryevents;
        public override string Id => ModifierId;
        public override string PairedWithId => ModifierNoDrama.ModifierId;

        [SelfInit]
        private static void Register() {
            Register(new ModifierNoHeavyDisasters());
        }

        public override void Apply(GameState s, bool isActive, bool _) {
            s.Prefs.SetBool(Pref.NoHeavyDisasters, isActive);
        }
    }
}
