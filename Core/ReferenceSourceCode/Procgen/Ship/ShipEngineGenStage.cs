using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using KL.Grid;
using UnityEngine;

namespace Game.Procgen {
    public class ShipEngineGenStage : BaseShipGenStage {
        private readonly NPCShip ship;
        private readonly Stack<ShipEngine> engines = new();
        public ShipEngineGenStage(NPCShip ship) {
            this.ship = ship;
        }

        private ShipEngine NextEngine() {
            if (engines.TryPop(out var engine)) {
                return engine;
            }
            return null;
        }

        public override void Apply() {
            foreach (var dev in ship.CombatState.Devices) {
                if (dev.Device.IsDestroyed) { continue; }
                if (!rng.Chance(Tunables.ShipDeviceDerelictSurvivalChance)) { continue; }
                if (dev is ShipEngine engine) {
                    engines.Push(engine);
                }
            }
            if (engines.Count == 0) { return; }
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

            var numEngines = Mathf.Min(potentialEngines.Count, engines.Count);
            // D.Warn("Numengines: {0}. Max: {1}, min: {2}", numEngines, maxEngines, minEngines);

            while (numEngines-- > 0 && engineCellQueue.Count > 0) {
                var pos = engineCellQueue.Dequeue();
                spec.SetCell(pos.X, pos.Y, CellType.Engine);
                // Build mirrored images of the engine
                // Engine root is 1 tile to the left, and we need one more time
                // to the left to get out of engine tile
                var engineTop = NextEngine();
                if (engineTop == null) { break; }
                var engTop = EntityUtils.CreatePrototype<Tile>(engineTop.Def);
                var engineBot = NextEngine();
                Tile engBottom = null;
                if (engineBot != null) {
                    engBottom = EntityUtils.CreatePrototype<Tile>(engineBot.Def);
                }
                engTop.Transform.Rotation = Facing.Type.Left;
                var engHeight = engTop.Transform.Height;
                var engOffset = Mathf.FloorToInt(
                    spec.CellHeight / 2f - engHeight / 2f);
                var engineRootOffset = engTop.Transform.RotatedWidth;
                var p1 = Pos.NeighborAt(spec.ToWorldPosMirrored(pos, true),
                    -engineRootOffset, spec.CellHeight - engOffset - engHeight);
                var eng1 = BuildTile(p1, engTop);
                var d1 = engineTop.Device;
                if (eng1 != null && d1.Health < d1.HealthMax) {
                    eng1.Health.TakeDamage(d1.HealthMax - d1.Health, DamageType.Explosive);
                }

                if (engBottom != null) {
                    var p2 = Pos.NeighborAt(spec.ToWorldPosMirrored(pos, false),
                            -engineRootOffset, engOffset);
                    engBottom.Transform.Rotation = Facing.Type.Left;
                    var eng2 = BuildTile(p2, engBottom);
                    var d2 = engineBot.Device;
                    if (eng2 != null && d2.Health < d2.HealthMax) {
                        eng2.Health.TakeDamage(d2.HealthMax - d2.Health, DamageType.Explosive);
                    }
                    if (rng.Chance(0.5f)) {
                        Refuel(eng2);
                    }
                }

                // yield return Viz(p1, 1);
                // yield return Viz(p2, 1);
                if (rng.Chance(0.5f)) {
                    Refuel(eng1);
                }

                if (engBottom == null) { break; }
            }
        }

        // One engine per chosen row of an asymmetric hull: the halves no longer come in pairs
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
            while (queue.Count > 0) {
                var engine = NextEngine();
                if (engine == null) { break; }
                var cell = queue.Dequeue();
                spec.SetFull(cell.X, cell.Y, CellType.Engine);
                var proto = EntityUtils.CreatePrototype<Tile>(engine.Def);
                proto.Transform.Rotation = Facing.Type.Left;
                var engOffset = Mathf.FloorToInt(spec.CellHeight / 2f - proto.Transform.Height / 2f);
                var p = Pos.NeighborAt(spec.FullCellToWorld(cell.X, cell.Y),
                    -proto.Transform.RotatedWidth, engOffset);
                var built = BuildTile(p, proto);
                var d = engine.Device;
                if (built != null && d.Health < d.HealthMax) {
                    built.Health.TakeDamage(d.HealthMax - d.Health, DamageType.Explosive);
                }
                if (rng.Chance(0.5f)) {
                    Refuel(built);
                }
            }
        }

        private void Refuel(Tile engine) {
            if (engine?.TryGetComponent<RefillableStorageComp>(out var fuel) == true) {
                fuel.Fill(rng.Range(1, 4));
            }
        }
    }
}
