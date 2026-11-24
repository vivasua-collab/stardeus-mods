using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using KL.Clock;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierNoCombat : Modifier {
        public const string ModifierId = "NoCombat";
        public override int SortOrder => 70;
        public override string GroupName => T.GameDifficulty;
        public override string Id => ModifierId;

        [SelfInit]
        private static void Register() {
            Register(new ModifierNoCombat());
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetBool(Pref.Combat);
            }
        }

        public override void Apply(GameState s, bool isActive, bool _) {
            s.Prefs.SetBool(Pref.Combat, !isActive);
        }
    }
}
