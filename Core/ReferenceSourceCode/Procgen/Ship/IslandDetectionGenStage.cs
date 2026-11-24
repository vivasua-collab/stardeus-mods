using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Algos;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class IslandDetectionGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        public override IEnumerator ApplyAsync() {
            yield return FindIslands(true, spec);
        }

        public IEnumerator FindIslands(bool doYield, ShipSpec spec) {
            var islands = new List<ShipIsland>();

            var assigned = new List<int>();
            // Non empty cells in the ship spec
            var nonEmpty = spec.FindNonEmpty();

            while (nonEmpty.Count > 0) {
                var ff = new FloodFillParallel<int>(nonEmpty.First(),
                    (pos) => {
                        Pos.ToXY(pos, out var x, out var y);
                        return spec.IsEmpty(x, y);
                    }, (_) => false,
                        (p, x, y) => {
                            return Pos.NeighborAt(p, x, y);
                        }, false);

                var task = ff.ProcessAsync(out var cancelIslands);
                var startTime = Time.realtimeSinceStartupAsDouble;
                while (!task.IsCompleted && !task.IsCanceled) {
                    if (Time.realtimeSinceStartupAsDouble - startTime >
                            Consts.FloodFillTimeBudget) {
                        cancelIslands.Cancel();
                        ff.IsCancelled = true;
                        break;
                    }
                    if (doYield && FrameTimer.HasToYieldSlow()) {
                        yield return null;
                        if (!GameState.IsCurrent(S)) { yield break; }
                    }
                }
                if (task.IsCanceled) {
                    D.Log("FF cancelled");
                    ff.IsCancelled = true;
                }
                D.Ass(!task.IsFaulted, "Island Flood fill successful");
                if (task.IsFaulted) { break; }

                var newIsland = ff.Result;
                islands.Add(new(newIsland));

                assigned.AddRange(newIsland);
                nonEmpty.RemoveAll(c => assigned.Contains(c));
            }
            spec.Islands = islands;
        }
    }
}
