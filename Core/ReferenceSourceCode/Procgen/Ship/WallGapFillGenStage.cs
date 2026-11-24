//#define DEBUG_GEN
using KL.Grid;
using Game.Data;

namespace Game.Procgen {
    public class WallGapFillGenStage : BaseShipGenStage {
        public override void Apply() {
            spec.ForEachTile((idx) => {
                if (EntityUtils.IsWallAt(idx)) { 
                    MaybeFillTheGap(idx);
                }
            });
        }

        private void MaybeFillTheGap(int idx) {
            var w = Pos.NeighborAt(idx, -1, 0);
            var n = Pos.NeighborAt(idx, 0, 1);
            var s = Pos.NeighborAt(idx, 0, -1);
            var e = Pos.NeighborAt(idx, 1, 0);
            var ne = Pos.NeighborAt(idx, 1, 1);
            var se = Pos.NeighborAt(idx, 1, -1);

            var built = false;
            if (W(ne) && !W(n) && !W(e)) {
                BuildWallAt(idx, n);
                built = true;
            }

            if (W(se) && !W(s) && !W(e)) {
                BuildWallAt(idx, s);
                built = true;
            }

            if (built) { return; }

            if (W(n) && !W(s) && !W(e) && !W(w) && W(Pos.S(s))) {
                BuildWallAt(idx, s);
                return;
            }

            if (W(w) && !W(s) && !W(e) && !W(n) && W(Pos.E(e))) {
                BuildWallAt(idx, e);
            }
        }

        private bool W(int idx) {
            return EntityUtils.IsWallAt(idx);
        }

        private void BuildWallAt(int parentIdx, int idx) {
            var curWallType = EntityUtils.WallAt(parentIdx).Definition.Id;
            BuildTile(idx, EntityUtils.CreatePrototype<Tile>(curWallType));
        }
    }
}
