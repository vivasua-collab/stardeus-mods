using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDrawLine : Hint {
        public const string Id = "draw_line";
        protected override string id => Id;
        protected override int hintAfterActions => 50;
        protected override bool HasCooldown => false;
        public override string Text => T.HintDraw_Line_V1.F(
            The.Bindings.GetControlName(ActionType.CtrlModifier));
        protected override void Validate() {
            if (!HasBinding(ActionType.CtrlModifier)) {
                Deactivate();
            }
        }

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDrawLine());
        }
    }
}
