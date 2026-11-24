using Game.Constants;
using Game.Data;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using System;

namespace Game.Commands {
    public class CmdOrderDeconstruct : Command {
        private Tile targetTile;
        private readonly int pos;
        private readonly int layer;
        private readonly bool isValid;
        // FIXME create a multi tile async version instead of spawning this command 1000 times"
        public CmdOrderDeconstruct(int pos, int layer) : base("order_deconstruct") {
            if (Pos.IsOOB(pos)) {
                D.Err("Trying to deconstruct something with invalid pos {0} (layer: {1})",
                    pos, layer);
                isValid = false;
            } else {
                isValid = true;
            }
            this.pos = pos;
            this.layer = layer;
        }
        protected override void Execute() {
            // This tile can already be removed by another call if it was a
            // multitile
            if (!isValid) { return; }
            targetTile = S.Map.Grids[layer].Get(pos);
            if (targetTile == null || targetTile.Transform == null) { return; }
            if (targetTile.Transform.IsChild) {
                targetTile = S.Map.Grids[targetTile.Definition.LayerId].Get(
                    targetTile.Transform.ParentPos);
                if (targetTile == null) { return; }
            }
            var goal = targetTile.Constructable?.CreateDeconstructGoal();
            if (goal != null && targetTile.Definition.LayerId == WorldLayer.Floor) {
                FloorDeconstructionWavePlanner.AssignAfterOrder(targetTile);
            }
        }
    }
}
