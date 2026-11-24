using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using UnityEngine;

namespace Game.Tools {
    public sealed class ToolAddDirt : Tool {
        public const string ToolId = "add_dirt";
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolAddDirt());
        }
        public override string Id => ToolId;

        public override bool IsConfigurable => true;
        public override bool IsDevTool => true;
        public override string MenuIcon => IconId.WDirt;
        private int lastPos;
        private List<string> availableParams;
        public override List<string> AvailableParams =>
            availableParams ??= Images.Matching(
                GraphicsId.DirtDirtMark01.Replace("DirtMark01", ""))
                    .Where(s => s.EndsWith("01")).ToList();
        private string currentParam;
        public override string CurrentParam => currentParam;
        public override bool Configure(string param) {
            currentParam = param;
            return true;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var pos = EntityUtils.ToPos(end);
            if (lastPos == pos) { return; }
            lastPos = pos;
            PlaceDirt(end);
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            isDragging = false;
            PlaceDirt(position);
        }

        private void PlaceDirt(Vector2 pos) {
            var param = Images.RandomMatching(currentParam, 2);
            S.Sys.Dirt.Add(DirtData.Create(pos, param));
        }

        public override string MenuNameFor(string param) {
            return "dirt";
        }

        public override string MenuIconFor(string param) {
            return param;
        }

        public override string ToString() {
            return Id;
        }
    }
}
