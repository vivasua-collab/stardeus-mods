using System.Collections.Generic;
using Game.Constants;
using UnityEngine;

namespace Game.Procgen {
    public class RectangleShapeGenStage : PreviewBaseShipGenStage {
        private readonly List<int> floor = new();
        public override void Apply() {
            for (int x = 0; x < spec.TemplateWidth; x++) {
                for (int y = 0; y < spec.TemplateHeight; y++) {
                    spec.SetCell(x, y, CellType.BodySolid);
                }
            }

            for (int x = 0; x < spec.TemplateWidth; x++) {
                for (int y = 0; y < spec.TemplateHeight * 2; y++) {
                    AppendAdjustedPosition(x, y, floor);
                }
            }

            BuildTiles(floor, DefIds.StructureFloorsFloor01);
            S?.Sig.CameraFocus.Send(spec.Bounds.Center(),
                Vector2.zero, spec.Bounds.MaxEdgeLength * 0.8f);
        }
    }
}
