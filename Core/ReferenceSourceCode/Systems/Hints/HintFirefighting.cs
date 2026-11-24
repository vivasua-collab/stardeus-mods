using Game.Constants;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintFirefighting : Hint {
        public const string Id = "firefighting";
        protected override string id => Id;
        protected override bool HasCooldown => false;
        public override string Text => T.HintFirefighting;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintFirefighting());
        }
    }
}
