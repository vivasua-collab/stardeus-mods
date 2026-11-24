using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using Unity.Mathematics;
using UnityEngine;
using KL.Utils;

namespace Game.Tools {
    public sealed class ToolOxygenate : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolOxygenate());
        }
        public const string ToolId = "oxygenate";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;

        public override string MenuIcon => IconId.WOverlayOxygen;
        private int lastPos;
        public override void Activate() {
            base.Activate();
            S.Sig.ShowOverlay.Send(OverlayId.AtmoO2);
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var pos = EntityUtils.ToPos(end);
            if (lastPos == pos) { return; }
            lastPos = pos;
            SetOxygen(pos);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            isDragging = false;
            if (EntityUtils.IsWithinBounds(position)) {
                SetOxygen(EntityUtils.ToPos(position));
            }
        }

        private void SetOxygen(int pos) {
            var curO2 = S.GetO2(pos);
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                S.SetO2(pos, Maths.Clamp(curO2 - 0.025f, 0f, 1f));
            } else if (The.Bindings.IsPressed(Input.ActionType.ShiftModifier)) {
                S.SetO2(pos, 0.21f);
            } else {
                S.SetO2(pos, Maths.Clamp(curO2 + 0.025f, 0f, 1f));
            }
            S.Sig.RecalculateAtmosphere.Send(pos);
        }

        public override string ToString() {
            return Id;
        }
    }
}
