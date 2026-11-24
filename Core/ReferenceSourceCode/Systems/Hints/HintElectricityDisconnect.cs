using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintElectricityDisconnect : Hint {
        public const string Id = "el_disconnect";
        protected override string id => Id;
        protected override bool HasCooldown => false;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintElectricityDisconnect());
        }
    }
}
