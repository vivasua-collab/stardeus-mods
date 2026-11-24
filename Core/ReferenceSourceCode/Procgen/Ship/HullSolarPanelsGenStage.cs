using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    // The starting ship's solar panels, side by side in one strip on the north or south hull
    // (STA-1673). Cast anywhere into space, they used to land on torn edges and in the gaps
    // between wreck sections, where no ship would have had them. North and south only: the
    // ship is long along x, with the engines at the tail and the nose ahead.
    //
    // The strip is centered on its run, which reads as planned. The run is drawn at random,
    // from the ship computer's island while it has one that fits. A strip that fits on no
    // run is split, and a panel that still finds no hull goes where it used to go.
    public class HullSolarPanelsGenStage : ObjectsInSpaceGenStage {
        private readonly List<HullRun> runs = new();
        private readonly List<HullRun> fitting = new();
        private readonly List<Def> unplaced = new();

        public HullSolarPanelsGenStage(List<Def> panels) : base(panels) {
        }

        public override void Apply() {
            EngineExhaust.Collect(S, spec, exhaust);
            var remaining = objects.Where(d => d != null).OrderBy(d => d.IdH).ToList();
            unplaced.Clear();
            while (remaining.Count > 0) {
                var taken = PlaceStrip(remaining);
                if (taken == 0) { break; }
                remaining.RemoveRange(0, taken);
            }
            remaining.AddRange(unplaced);
            if (remaining.Count > 0) {
                D.Warn("{0} No hull run left for {1} solar panels, placing them anywhere in space",
                    LogPrefix, remaining.Count);
                foreach (var def in remaining) {
                    PlaceThing(def, false);
                }
            }
        }

        // Takes as many panels as one run fits from the front of the list; 0 when no run fits
        private int PlaceStrip(List<Def> panels) {
            var cpuIsland = spec.HasCPU ? S.Sys.Areas.IslandAt(spec.CPUPos) : 0;
            for (int count = panels.Count; count > 0; count--) {
                StripSize(panels, count, out var width, out var depth);
                runs.Clear();
                // One tile beyond the panels stays free, so the strip never touches anything
                HullRuns.Collect(S, spec, Facing.Type.Up, depth + 1, exhaust, runs);
                HullRuns.Collect(S, spec, Facing.Type.Down, depth + 1, exhaust, runs);
                fitting.Clear();
                foreach (var run in runs) {
                    if (run.Length >= width && run.Island == cpuIsland) {
                        fitting.Add(run);
                    }
                }
                if (fitting.Count == 0) {
                    foreach (var run in runs) {
                        if (run.Length >= width) {
                            fitting.Add(run);
                        }
                    }
                }
                if (fitting.Count == 0) { continue; }
                var chosen = rng.From(fitting);
                return BuildStrip(panels, count, chosen, (chosen.Length - width) / 2);
            }
            return 0;
        }

        private static void StripSize(List<Def> panels, int count, out int width,
                out int depth) {
            width = 0;
            depth = 0;
            for (int i = 0; i < count; i++) {
                var proto = EntityUtils.CreatePrototype<Tile>(panels[i]);
                width += proto.Transform.RotatedWidth;
                depth = Maths.Max(depth, proto.Transform.RotatedHeight);
            }
        }

        private int BuildStrip(List<Def> panels, int count, HullRun run, int offset) {
            var along = offset;
            for (int i = 0; i < count; i++) {
                var proto = EntityUtils.CreatePrototype<Tile>(panels[i]);
                var wall = run.WallAt(along);
                along += proto.Transform.RotatedWidth;
                // The root is the bottom left tile: right above a north wall, and a whole
                // panel height below a south one
                var root = run.Outward == Facing.Type.Up
                    ? Pos.N(wall)
                    : Pos.NeighborAt(wall, 0, -proto.Transform.RotatedHeight);
                if (!CmdPlaceTile.CanPlace(root, proto, WorldLayer.Objects, warn: false)
                        || BuildTile(root, proto) == null) {
                    D.Warn("{0} Could not place {1} on the hull at {2}", LogPrefix,
                        panels[i].Id, Pos.String(root));
                    unplaced.Add(panels[i]);
                }
            }
            return count;
        }
    }
}
