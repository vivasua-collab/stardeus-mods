using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using UnityEngine;

namespace Game.Tools {
    // Shows where a Local Maneuvering rotation would land before it happens, so
    // Rotate Left / Rotate Right get a preview + confirm step like Move Section
    // already has. The preview positions come from the same SectionMoveSet.Build
    // helper CmdRotateSection itself uses, so it can never show a different
    // footprint than what actually rotates. Confirming just calls the existing,
    // unchanged ShipEngineComp.RotateShip - this tool only adds a click-to-confirm
    // step in front of it.
    public sealed class ToolRotateSectionPreview : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolRotateSectionPreview());
        }

        public const string ToolId = "rotate_section_preview";
        public override string Id => ToolId;
        public override string MenuIcon => null;
        public override bool SkipMenu => true;
        public override bool IsConfigurable => false;
        public override string NameKey => "input.Rotate";
        protected override Color markedColor => Consts.BlueRectColor;
        private ShipEngineComp engine;
        private sbyte dir;

        public static void Begin(ShipEngineComp engine, sbyte dir) {
            var t = The.Toolbox.Get<ToolRotateSectionPreview>(ToolId);
            t.engine = engine;
            t.dir = dir;
            The.Toolbox.SetActive(t);
            t.ComputePreview();
        }

        private void ComputePreview() {
            if (engine == null) { return; }
            var anchor = engine.FindAnchor();
            var islandId = S.Sys.Areas.IslandAt(anchor);
            if (islandId == 0 || !S.Sys.Areas.Islands.TryGetValue(islandId, out var island)) {
                return;
            }
            var center = Pos.Encapsulate(island).CenterPos;
            var movingSet = SectionMoveSet.Build(S, islandId, new HashSet<int>(island), true, out _);
            var rotated = new HashSet<int>();
            foreach (var p in movingSet) {
                if (Pos.TryRotateAround(center, p, true, dir, out var newPos, out _, out _)) {
                    rotated.Add(newPos);
                    AddMarkedTile(newPos);
                }
            }
            if (rotated.Count > 0) {
                var rekt = Pos.Encapsulate(rotated);
                S.Sig.CameraFocus.Send(rekt.Center(), rekt.Extents * 1.1f, 0);
            }
        }

        public override void OnClickDown(Vector2 pos) {
            base.OnClickDown(pos);
            var eng = engine;
            var d = dir;
            S.Toolbox.Clear();
            eng?.RotateShip(d);
        }

        public override string ToString() {
            return ToolId;
        }
    }
}
