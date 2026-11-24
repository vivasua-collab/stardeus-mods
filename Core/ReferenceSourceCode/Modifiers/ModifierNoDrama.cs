using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using KL.Clock;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierNoDrama : Modifier {
        public const string ModifierId = "NoDrama";
        public override int SortOrder => 66;
        public override string GroupName => T.SituationStoryevents;
        public override string Id => ModifierId;

        [SelfInit]
        private static void Register() {
            Register(new ModifierNoDrama());
        }

        public override void Reset(GameSetupData data) {
            base.Reset(data);
            if (GameState.IsCurrent(The.R?.State)) {
                The.R.State.Prefs.UnsetBool(Pref.DisableDrama);
            }
        }

        public override void Apply(GameState s, bool isActive, bool _) {
            s.Prefs.SetBool(Pref.DisableDrama, isActive);
        }
    }
}
