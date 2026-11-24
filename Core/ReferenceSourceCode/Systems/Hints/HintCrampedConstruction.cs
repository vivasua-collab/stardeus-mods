using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintCrampedConstruction : Hint {
        public const string Id = "construction_cramped";
        protected override string id => Id;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintCrampedConstruction());
        }
    }
}
