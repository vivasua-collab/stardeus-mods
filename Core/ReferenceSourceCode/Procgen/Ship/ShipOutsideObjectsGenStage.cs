using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;

namespace Game.Procgen {
    public class ShipOutsideObjectsGenStage : BaseShipGenStage {

        private readonly List<string> templatesTop = new();

        public override void Apply() {
            CollectTemplates();
            var chance = 1f;
            // 1. find a nice spot on x axis of the ship
            for (int x = 0; x < spec.Width; x += 20) {
                if (!rng.Chance(chance)) { continue; }
                chance *= 0.7f;
                var wX = x + rng.Range(5, 15);
                var top = new List<int>();
                var bot = new List<int>();
                FindOuterBuildSpots(wX, top, bot);
                BuildSomething(top, bot);
            }
        }

        private void CollectTemplates() {
            templatesTop.Clear();
            foreach (var def in The.Defs.Defs) {
                var k = def.Value.Id;
                if (k.StartsWith("Templates/Outside") && !k.EndsWith("D")) {
                    templatesTop.Add(def.Value.Id);
                }
            }
        }

        private bool WillFit(int t, Tile proto, int dir) {
            var w = proto.Transform.RotatedWidth;
            var h = proto.Transform.RotatedHeight;
            for (int x = 0; x < w; x++) {
                // Check if there's floor across the whole template width
                if (!EntityUtils.IsFloorAt(Pos.NeighborAt(t, x, -dir))) {
                    return false;
                }
                // Also check if we're not blocking any shit
                for (int y = 0; y < h; y++) {
                    if (!EntityUtils.IsBlankAt(Pos.NeighborAt(t, x, y * dir))) {
                        return false;
                    }
                }
            }
            return true;
        }

        private void BuildSomething(List<int> top, List<int> bot) {
            // 3. attach some stuff on both y and -y
            var tplIdx = rng.Range(0, templatesTop.Count);
            var topItem = templatesTop[tplIdx];
            var botItem = $"{topItem}D";

            for (int i = 0; i < top.Count; i++) {
                int t = top[i];
                var proto = EntityUtils.CreatePrototype<Tile>(topItem);
                if (WillFit(t, proto, 1)) {
                    new CmdPlaceTile(t, proto, warn: false, true).Execute(S);
                }
            }
            for (int i = 0; i < bot.Count; i++) {
                int t = bot[i];
                var proto = EntityUtils.CreatePrototype<Tile>(botItem);
                if (WillFit(t, proto, -1)) {
                    var h = proto.Transform.RotatedHeight - 1;
                    t = Pos.NeighborAt(t, 0, -h);
                    new CmdPlaceTile(t, proto, warn: false, true).Execute(S);
                }
            }
        }

        private void FindOuterBuildSpots(int shipX, List<int> top, List<int> bot) {
            // 2. find where y axis has an outer wall
            var x = spec.TileToWorldX(shipX);
            for (int y = 0; y < spec.Height; y++) {
                var wY = spec.TileToWorldY(y);
                var idx = Pos.FromXY(x, wY);
                var wall = EntityUtils.WallAt(idx);
                if (wall == null) { continue; }
                if (wall.Definition.Id == DefIds.StructureWallsWall01) {
                    var n = Pos.N(idx);
                    var s = Pos.S(idx);
                    if (EntityUtils.IsBlankAt(n)) {
                        top.Add(n);
                    } else if (EntityUtils.IsBlankAt(s)) {
                        bot.Add(s);
                    }
                }
            }
        }
    }
}
