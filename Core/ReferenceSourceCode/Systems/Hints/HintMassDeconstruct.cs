using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintMassDeconstruct : Hint {
        public const string Id = "mass_deconstruct";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintMassDeconstruct());
        }
    }
}
