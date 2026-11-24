using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class EngineGenStage : BaseShipGenStage {
        private readonly List<Def> engineDefs;
        private Def lastEngine;
        private int maxEngines;
        private int minEngines = 4;
        public EngineGenStage(int minEngines, int maxEngines, float chanceLocked,
                params string[] defs) {
            if (minEngines % 2 == 1) {
                D.Err("MinEngines has to be an even number! Fixing to *2: {0} => {1}", minEngines, minEngines * 2);
                minEngines *= 2;
            }
            engineDefs = new List<Def>();
            this.minEngines = minEngines;
            this.maxEngines = maxEngines;
            foreach (var d in defs) {
                var eng = The.Defs.Get(d);
                if (eng != null) {
                    if (chanceLocked < 1f && S?.Research != null) {
                        if (!S.Research.IsDefUnlocked(eng)) {
                            if (!rng.Chance(chanceLocked)) {
                                continue;
                            }
                        }
                    }
                    engineDefs.Add(eng);
                }
            }
        }


        public Def NextEngine() {
            var eng = rng.From(engineDefs);
            if (engineDefs.Count == 1) {
                return eng;
            }
            int attempts = 10;
            while (lastEngine == eng && attempts-- > 0) {
                eng = rng.From(engineDefs);
            }
            lastEngine = eng;
            return eng;
        }

        public override void Apply() {
            if (maxEngines == 0) { return; }
            if (engineDefs.Count == 0) {
                D.Err("Trying to generate {0} engines with an empty engine list!",
                    maxEngines);
            }
            if (spec.IsAsymmetric) {
                ApplyFull();
                return;
            }
            // 1. scan y starting from x to find first cell
            var potentialEngines = new List<XY>();
            for (int y = 0; y < spec.TemplateHeight; y++) {
                for (int x = 0; x < spec.TemplateWidth; x++) {
                    if (!spec.IsEmpty(x, y, false, false) && !spec.IsOutOfBounds(x, y, false)) {
                        // D.Warn("Potential engine at {0}:{1}", x, y);
                        potentialEngines.Add(new XY(x, y));
                        // yield return Viz(spec.ToWorldPosMirrored(new XY(x, y), true), 0.3f);
                        break;
                    }
                }
            }

            // D.Warn("Potential engines: {0}", potentialEngines.Count);
            // 2. pick a few random cells and convert them into Engine type
            var engineCellQueue = new Queue<XY>(potentialEngines.OrderBy(
                c => rng.Value).ToList());

            var numEngines = rng.Range(minEngines / 2, potentialEngines.Count);
            if (numEngines > maxEngines / 2) {
                numEngines = maxEngines / 2;
            }
            if (numEngines < minEngines / 2) {
                numEngines = minEngines / 2;
            }

            // D.Warn("Numengines: {0}. Max: {1}, min: {2}", numEngines, maxEngines, minEngines);

            while (numEngines-- > 0 && engineCellQueue.Count > 0) {
                var pos = engineCellQueue.Dequeue();
                spec.SetCell(pos.X, pos.Y, CellType.Engine);
                // Build mirrored images of the engine
                // Engine root is 1 tile to the left, and we need one more time
                // to the left to get out of engine tile
                var engine = NextEngine();
                var engTop = EntityUtils.CreatePrototype<Tile>(engine);
                var engBottom = EntityUtils.CreatePrototype<Tile>(engine);
                engTop.Transform.Rotation = Facing.Type.Left;
                engBottom.Transform.Rotation = Facing.Type.Left;
                var engHeight = engTop.Transform.Height;
                var engOffset = Mathf.FloorToInt(
                    spec.CellHeight / 2f - engHeight / 2f);
                var engineRootOffset = engTop.Transform.RotatedWidth;
                var p1 = Pos.NeighborAt(spec.ToWorldPosMirrored(pos, true),
                    -engineRootOffset, spec.CellHeight - engOffset - engHeight);
                var eng1 = BuildTile(p1, engTop);
                var p2 = Pos.NeighborAt(spec.ToWorldPosMirrored(pos, false),
                        -engineRootOffset, engOffset);
                var eng2 = BuildTile(p2, engBottom);

                // yield return Viz(p1, 1);
                // yield return Viz(p2, 1);

                Refuel(eng1);
                Refuel(eng2);
            }
        }

        // One engine per chosen row: the tail cell of every full template row is a candidate,
        // and the two halves no longer come in pairs.
        private void ApplyFull() {
            var potentialEngines = new List<XY>();
            for (int fy = 0; fy < spec.FullHeight; fy++) {
                for (int x = 0; x < spec.TemplateWidth; x++) {
                    if (!spec.IsEmptyFull(x, fy, false)) {
                        potentialEngines.Add(new XY(x, fy));
                        break;
                    }
                }
            }
            var queue = new Queue<XY>(potentialEngines.OrderBy(c => rng.Value).ToList());
            var numEngines = rng.Range(minEngines, potentialEngines.Count);
            numEngines = Mathf.Clamp(numEngines, minEngines, Mathf.Max(minEngines, maxEngines));
            while (numEngines-- > 0 && queue.Count > 0) {
                var cell = queue.Dequeue();
                spec.SetFull(cell.X, cell.Y, CellType.Engine);
                var proto = EntityUtils.CreatePrototype<Tile>(NextEngine());
                proto.Transform.Rotation = Facing.Type.Left;
                var engHeight = proto.Transform.Height;
                var engOffset = Mathf.FloorToInt(spec.CellHeight / 2f - engHeight / 2f);
                var p = Pos.NeighborAt(spec.FullCellToWorld(cell.X, cell.Y),
                    -proto.Transform.RotatedWidth, engOffset);
                Refuel(BuildTile(p, proto));
            }
        }

        private void Refuel(Tile engine) {
            if (engine?.TryGetComponent<RefillableStorageComp>(out var fuel) == true) {
                fuel.Fill(10);
            }
        }
    }
}
