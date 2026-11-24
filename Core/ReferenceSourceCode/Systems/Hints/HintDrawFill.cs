using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDrawFill : Hint {
        public const string Id = "draw_fill";
        protected override string id => Id;
        protected override int hintAfterActions => 250;
        protected override bool HasCooldown => false;
        public override string Text => T.HintDraw_Fill_V2.F(
            The.Bindings.GetControlName(ActionType.CtrlModifier),
            The.Bindings.GetControlName(ActionType.ShiftModifier));
        protected override void Validate() {
            if (!HasBinding(ActionType.CtrlModifier)
                    || !HasBinding(ActionType.ShiftModifier)) {
                Deactivate();
            }
        }

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDrawFill());
        }
    }
}
