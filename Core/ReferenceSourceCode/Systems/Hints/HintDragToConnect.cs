using UnityEngine;

using Game.Utils;
namespace Game.Systems.Hints {
    public class HintDragConnect : Hint {
        public const string Id = "el_drag_connect";
        protected override string id => Id;
        protected override bool HasCooldown => false;

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDragConnect());
        }
    }
}
