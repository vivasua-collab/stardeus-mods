using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDrawCircle : Hint {
        public const string Id = "draw_circle";
        protected override string id => Id;
        protected override int hintAfterActions => 300;
        protected override bool HasCooldown => false;
        public override string Text => T.HintDraw_Circle_V1.F(
            The.Bindings.GetControlName(ActionType.ShiftModifier));
        protected override void Validate() {
            if (!HasBinding(ActionType.ShiftModifier)) {
                Deactivate();
            }
        }

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDrawCircle());
        }
    }
}
