#if false
using System;
using System.Collections.Generic;
using Game.Data;
using Game.Input;
using Game.Systems;
using KL.Collections;
using KL.Grid;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Tools {
    public sealed class FreeSelectHoverHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectHover";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectHoverHandler());
        }

        public override int Order => 1;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }

        public override void OnHover(Vector2 pos) {

        }
    }
}
#endif
