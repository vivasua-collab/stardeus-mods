using Game.Constants;
using Game.Input;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintCameraMemory : Hint {
        public const string Id = "camera_memory";
        protected override string id => Id;
        public override string Text => T.CodexMUiCameramemoryDesc_V1.F(
            The.Bindings.GetControlName(ActionType.CtrlModifier));

        [SelfInit]
        static void AutoRegister() {
            Register(new HintCameraMemory());
        }
    }
}
