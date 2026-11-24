using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using UnityEngine;

namespace Game.Tools {
    public sealed class ToolStartFire : Tool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolStartFire());
        }
        public const string ToolId = "start_fire";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        public override bool IsDevTool => true;

        public override string MenuIcon => IconId.WFire;
        private int lastPos;

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var pos = EntityUtils.ToPos(end);
            if (lastPos == pos) { return; }
            lastPos = pos;
            EnqueueFire(pos);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            isDragging = false;
            if (EntityUtils.IsWithinBounds(position)) {
                EnqueueFire(EntityUtils.ToPos(position));
            }
        }

        private void EnqueueFire(int pos) {
            var fuel = 0f;
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                fuel = 1f;
            }
            var cmd = new CmdStartFire(pos, fuel: fuel);
            S.CmdQ.Enqueue(cmd);
        }

        public override string ToString() {
            return Id;
        }
    }
}
