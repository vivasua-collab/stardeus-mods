#if !PRODUCTION_BUILD
#define SKIP_NO_PAUSE
#endif
using Game.Constants;
using Game.Data;
using KL.Clock;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Modifiers {
    public sealed class ModifierNoPause : Modifier {
        public const string ModifierId = "NoPause";
        public override string Id => ModifierId;
        public override bool PreventsAchievements => false;
        public override int SortOrder => 72;
        public override string GroupName => T.NounExtraChallenge;

        [SelfInit]
        private static void Register() {
            Register(new ModifierNoPause());
        }

        public override void Apply(GameState s, bool isActive, bool _) {
            if (isActive) {
                s.Clock.SetMinSpeed(ClockSpeed.Normal);
                #if SKIP_NO_PAUSE
                D.Err("No pause is supposed to be set for this save!");
                s.Clock.SetMinSpeed(ClockSpeed.Stopped);
                #endif
            } else {
                // Turning it off again has to lower the floor it raised, or pausing stays
                // broken for the rest of the session
                s.Clock.SetMinSpeed(ClockSpeed.Stopped);
            }
        }
    }
}
