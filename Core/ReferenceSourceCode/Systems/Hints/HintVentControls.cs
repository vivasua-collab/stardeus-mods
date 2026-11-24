using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintVentControls : Hint {
        public const string Id = "vent_controls";
        protected override string id => Id;

        [SelfInit]
        private static void AutoRegister() {
            Register(new HintVentControls());
        }
    }
}
