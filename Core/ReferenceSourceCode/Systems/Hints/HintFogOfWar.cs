using Game.Constants;
using Game.Input;
using Game.Modifiers;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintFogOfWar : Hint {
        public const string Id = "fog_of_war";
        protected override string id => Id;
        protected override bool IsRecurring => true;
        public override string Text => T.FogOfWarWarning_V1.F(Texts.Accented(T.AmbColorDark));

        [SelfInit]
        static void AutoRegister() {
            Register(new HintFogOfWar());
        }
    }
}
