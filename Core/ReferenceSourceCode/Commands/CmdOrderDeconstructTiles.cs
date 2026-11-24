using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Grid;

namespace Game.Commands {
    public class CmdOrderDeconstructTiles : Command {
        private readonly List<int> positions = new();
        private readonly List<int> layers = new();

        public CmdOrderDeconstructTiles(IEnumerable<Tile> tiles)
                : base("order_deconstruct_tiles") {
            var seen = new HashSet<long>();
            foreach (var tile in tiles) {
                if (tile?.IsActive != true || tile.Transform == null) {
                    continue;
                }
                var target = tile.Transform.IsChild ? tile.Transform.Parent : tile;
                if (target?.Definition == null) {
                    continue;
                }
                var layer = target.Definition.LayerId;
                var pos = target.PosIdx;
                var key = ((long) layer << 32) | (uint) pos;
                if (!seen.Add(key)) {
                    continue;
                }
                positions.Add(pos);
                layers.Add(layer);
            }
        }

        protected override void Execute() {
            var orderedFloors = new List<Tile>();
            for (int i = 0; i < positions.Count; i++) {
                var targetTile = ResolveTile(positions[i], layers[i]);
                var goal = targetTile?.Constructable?.CreateDeconstructGoal();
                if (goal != null
                        && targetTile.Definition.LayerId == WorldLayer.Floor
                        && targetTile.IsConstructed
                        && targetTile.Goal == goal) {
                    orderedFloors.Add(targetTile);
                }
            }
            if (orderedFloors.Count > 0) {
                FloorDeconstructionWavePlanner.AssignAfterOrder(orderedFloors);
            }
        }

        private Tile ResolveTile(int pos, int layer) {
            if (Pos.IsOOB(pos)) {
                return null;
            }
            var targetTile = S.Map.Grids[layer].Get(pos);
            if (targetTile == null || targetTile.Transform == null) {
                return null;
            }
            if (targetTile.Transform.IsChild) {
                targetTile = S.Map.Grids[targetTile.Definition.LayerId].Get(
                    targetTile.Transform.ParentPos);
            }
            return targetTile;
        }
    }
}
