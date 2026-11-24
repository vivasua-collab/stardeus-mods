using System.Collections.Generic;
using KL.Grid;
using System.Collections;
using KL.Utils;
using Game.Constants;
using Game.Data;
using Game.Commands;
using System.Linq;

namespace Game.Procgen {
    public class RoundSharpCornersGenStage : BaseShipGenStage {
        public override void Apply() {
            spec.ForEachTile(CheckSharpCorner);
        }

        private void CheckSharpCorner(int pos) {
            if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) { return; }
            //  TOP LEFT
            if (
                EntityUtils.IsNothingAt(Pos.N(pos)) &&
                EntityUtils.IsNothingAt(Pos.NW(pos)) &&
                EntityUtils.IsNothingAt(Pos.W(pos)) &&
                EntityUtils.IsFloorAt(Pos.E(pos)) &&
                EntityUtils.IsFloorAt(Pos.S(pos))
            ) {
                var f = EntityUtils.FloorAt(pos);
                var angleDef = The.Defs.TryGet(f.DefinitionId + "Angle");
                if (angleDef == null) { return; }
                var angle = EntityUtils.CreatePrototype<Tile>(f.DefinitionId + "Angle");
                //angle.Transform.Rotation = Facing.Type.Left;
                //f.Transform.Rotation = Facing.Type.Left; // angle in bottom right
                //f.Transform.Rotation = Facing.Type.Down; // angle in top right
                angle.Transform.Rotation = Facing.Type.Right; // angle in top left
                //f.GenReplaceWith(S, angle);
                BuildTile(pos, angle);
            }

            //  TOP RIGHT
            if (
                EntityUtils.IsNothingAt(Pos.N(pos)) &&
                EntityUtils.IsNothingAt(Pos.NE(pos)) &&
                EntityUtils.IsNothingAt(Pos.E(pos)) &&
                EntityUtils.IsFloorAt(Pos.W(pos)) &&
                EntityUtils.IsFloorAt(Pos.S(pos))
            ) {
                var f = EntityUtils.FloorAt(pos);
                var angleDef = The.Defs.TryGet(f.DefinitionId + "Angle");
                if (angleDef == null) { return; }
                var angle = EntityUtils.CreatePrototype<Tile>(f.DefinitionId + "Angle");
                angle.Transform.Rotation = Facing.Type.Down; // top right
                //f.GenReplaceWith(S, angle);
                BuildTile(pos, angle);
            }

            //  BOTTOM LEFT
            if (
                EntityUtils.IsNothingAt(Pos.S(pos)) &&
                EntityUtils.IsNothingAt(Pos.SW(pos)) &&
                EntityUtils.IsNothingAt(Pos.W(pos)) &&
                EntityUtils.IsFloorAt(Pos.E(pos)) &&
                EntityUtils.IsFloorAt(Pos.N(pos))
            ) {
                var f = EntityUtils.FloorAt(pos);
                var angleDef = The.Defs.TryGet(f.DefinitionId + "Angle");
                if (angleDef == null) { return; }
                var angle = EntityUtils.CreatePrototype<Tile>(f.DefinitionId + "Angle");
                //angle.Transform.Rotation = Facing.Type.Left;
                //f.Transform.Rotation = Facing.Type.Left; // angle in bottom right
                //f.Transform.Rotation = Facing.Type.Down; // angle in top right
                angle.Transform.Rotation = Facing.Type.Up; // angle in bottom left
                //f.GenReplaceWith(S, angle);
                BuildTile(pos, angle);
            }

            //  BOTTOM RIGHT
            if (
                EntityUtils.IsNothingAt(Pos.S(pos)) &&
                EntityUtils.IsNothingAt(Pos.SE(pos)) &&
                EntityUtils.IsNothingAt(Pos.E(pos)) &&
                EntityUtils.IsFloorAt(Pos.W(pos)) &&
                EntityUtils.IsFloorAt(Pos.N(pos))
            ) {
                var f = EntityUtils.FloorAt(pos);
                var angleDef = The.Defs.TryGet(f.DefinitionId + "Angle");
                if (angleDef == null) { return; }
                var angle = EntityUtils.CreatePrototype<Tile>(f.DefinitionId + "Angle");
                angle.Transform.Rotation = Facing.Type.Left;
                //f.GenReplaceWith(S, angle);
                BuildTile(pos, angle);
            }
        }
    }
}
