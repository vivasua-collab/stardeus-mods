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
    public sealed class ToolHeat : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolHeat());
        }
        public const string ToolId = "heat";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;

        public override string MenuIcon => IconId.WOverlayHeat;
        private int lastPos;

        public override void Activate() {
            base.Activate();
            S.Sig.ShowOverlay.Send(OverlayId.AtmoTemp);
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var pos = EntityUtils.ToPos(end);
            if (lastPos == pos) { return; }
            lastPos = pos;
            AddHeat(pos);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            isDragging = false;
            if (EntityUtils.IsWithinBounds(position)) {
                AddHeat(EntityUtils.ToPos(position));
            }
        }

        private void AddHeat(int pos) {
            var curHeat = S.GetTemp(pos);
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                S.SetTemp(pos, Maths.Clamp(curHeat - 15f, 0f, 5000f));
            } else if (The.Bindings.IsPressed(Input.ActionType.ShiftModifier)) {
                S.SetTemp(pos, Units.CelsiusToKelvin(21f));
            } else {
                S.SetTemp(pos, Maths.Clamp(curHeat + 15f, 0f, 5000f));
            }
            S.Sig.RecalculateAtmosphere.Send(pos);
        }

        public override string ToString() {
            return Id;
        }
    }
}
