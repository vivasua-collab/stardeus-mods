using Game.CodeGen;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Tools {
    public sealed class FreeSelectBeingAvatarHandler : FreeSelectHandler {
        public const string HandlerId = "FreeSelectBeingAvatar";

        [SelfInit]
        private static void Register() {
            Register(HandlerId, new FreeSelectBeingAvatarHandler());
        }

        public override int Order => 1;

        public override void Initialize(FreeSelectContext ctx) {
            base.Initialize(ctx);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 pos, float dragDistance) {
            if (dragDistance < 0.001f) { return; }
            var rect = ScreenRect(startPos, pos);

            var result = Caches.BeingDataList;
            result.Clear();
            if (S.UIQuery.TrySelectBeingAvatars.Ask(rect, result)) {
                ctx.DeselectAny();
                ctx.ClickUpConsumed = true;
                foreach (var b in result) {
                    if (b.Being != null) {
                        ctx.SelectBeing(b.Being);
                    }
                }
                The.SysSig.SetCursorOverride.Send(0);
            }
        }

        public override void OnDrag(Vector2 startPos, Vector2 pos, float dragDistance) {
            S.UISig.PreviewBeingAvatarSelection.Send(ScreenRect(startPos, pos));
        }

        private static Rect ScreenRect(Vector2 startPos, Vector2 pos) {
            startPos = RenderUtils.Camera.WorldToScreenPoint(startPos);
            pos = RenderUtils.Camera.WorldToScreenPoint(pos);

            float minX = Maths.Min(startPos.x, pos.x);
            float minY = Maths.Min(startPos.y, pos.y);
            float maxX = Maths.Max(startPos.x, pos.x);
            float maxY = Maths.Max(startPos.y, pos.y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
