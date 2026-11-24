using Game.Constants;
using Game.Input;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Systems.Hints {
    public class HintDrawPencil : Hint {
        public const string Id = "draw_pencil";
        protected override string id => Id;
        protected override int hintAfterActions => 200;
        protected override bool HasCooldown => false;
        public override string Text => T.HintDraw_Pencil_V1.F(
            The.Bindings.GetControlName(ActionType.AltModifier));

        protected override void Validate() {
            if (!HasBinding(ActionType.AltModifier)) {
                Deactivate();
            }
        }

        [SelfInit]
        static void AutoRegister() {
            Register(new HintDrawPencil());
        }
    }
}
