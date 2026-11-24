using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDrawDelete : Hint {
        public const string Id = "draw_delete";
        protected override string id => Id;
        protected override int hintAfterActions => 500;
        protected override bool HasCooldown => false;
        public override string Text => T.HintDraw_Delete_V2.F(
            The.Bindings.GetControlName(ActionType.CtrlModifier),
            The.Bindings.GetControlName(ActionType.AltModifier));
        protected override void Validate() {
            if (!HasBinding(ActionType.CtrlModifier)
                    || !HasBinding(ActionType.AltModifier)) {
                Deactivate();
            }
        }

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDrawDelete());
        }
    }
}
