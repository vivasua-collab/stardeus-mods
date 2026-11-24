using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen {
    // The starting ship's particle collector, built into the hull the way a shipyard would
    // have done it (STA-1673): a one tile deep pad of reinforced floor outside an original
    // hull wall, the collector standing beyond it with its port on the pad, and an airlock in
    // the wall right behind the port so the crew can reach the output. The legacy placement
    // cast it anywhere into space and broke whatever wall was in the way of the port.
    //
    // North and south faces first, then east and west, then the legacy placement: the
    // scenarios' validation refuses a ship without a collector, and starting over would
    // change the curated Quick Start seeds.
    //
    // Deterministic: the spot nearest the ship computer wins, so what the collector makes
    // lands near the starting area. That also leaves the solar stage after it on the rng fork
    // ObjectsInSpaceGenStage had, and every stage after that on theirs.
    public class HullParticleCollectorGenStage : ObjectsInSpaceGenStage {
        public override bool SkipRNG => true;

        private static readonly Facing.Type[] NorthSouth = { Facing.Type.Up, Facing.Type.Down };
        private static readonly Facing.Type[] EastWest = { Facing.Type.Right, Facing.Type.Left };

        private readonly List<HullRun> runs = new();
        private readonly List<Spot> spots = new();
        private readonly List<int> pad = new();
        private readonly List<Tile> padTiles = new();

        private struct Spot {
            public HullRun Run;
            // Run index of the first pad tile
            public int PadStart;
            public int PadLength;
            public int Root;
            public int Door;
            public int Score;
        }

        public HullParticleCollectorGenStage(List<Def> collectors) : base(collectors) {
        }

        public override void Apply() {
            EngineExhaust.Collect(S, spec, exhaust);
            foreach (var def in objects) {
                if (def == null) { continue; }
                if (TryPlaceOnHull(def, NorthSouth) || TryPlaceOnHull(def, EastWest)) {
                    continue;
                }
                D.Warn("{0} No hull spot for {1}, placing it anywhere in space", LogPrefix, def.Id);
                // The legacy placement shuffles; this stage takes no fork of its own
                stageRngFork ??= new Rng(spec.Seed);
                PlaceThing(def, false);
            }
        }

        private bool TryPlaceOnHull(Def def, Facing.Type[] faces) {
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            if (!proto.Transform.HasOutPort) {
                D.Err("{0} {1} has no out port to put on a pad", LogPrefix, def.Id);
                return false;
            }
            spots.Clear();
            foreach (var outward in faces) {
                // The collector faces the hull, so its port is between it and the wall
                proto.Transform.Rotation = Facing.Opposite(outward);
                var horizontal = HullRuns.IsHorizontal(outward);
                var width = horizontal ? proto.Transform.RotatedWidth : proto.Transform.RotatedHeight;
                var depth = horizontal ? proto.Transform.RotatedHeight : proto.Transform.RotatedWidth;
                var padLength = width + 2;
                runs.Clear();
                // Pad, collector, and one free tile beyond it
                HullRuns.Collect(S, spec, outward, depth + 2, exhaust, runs);
                foreach (var run in runs) {
                    for (int s = 0; s + padLength <= run.Length; s++) {
                        if (TryMakeSpot(proto, run, s, padLength, out var spot)) {
                            spots.Add(spot);
                        }
                    }
                }
            }
            var cpuIsland = spec.HasCPU ? S.Sys.Areas.IslandAt(spec.CPUPos) : 0;
            spots.Sort((a, b) => {
                var aOff = a.Run.Island == cpuIsland ? 0 : 1;
                var bOff = b.Run.Island == cpuIsland ? 0 : 1;
                if (aOff != bOff) { return aOff.CompareTo(bOff); }
                if (a.Score != b.Score) { return a.Score.CompareTo(b.Score); }
                return a.Door.CompareTo(b.Door);
            });
            foreach (var spot in spots) {
                if (TryBuild(def, spot)) {
                    return true;
                }
            }
            return false;
        }

        private bool TryMakeSpot(Tile proto, HullRun run, int padStart, int padLength,
                out Spot spot) {
            spot = default;
            var outward = run.Outward;
            // The collector's footprint starts one tile in from the pad's ends, one tile out
            // from the pad
            var firstWall = run.WallAt(padStart + 1);
            Pos.ToXY(firstWall, out var wx, out var wy);
            var w = proto.Transform.RotatedWidth;
            var h = proto.Transform.RotatedHeight;
            int rootX = wx;
            int rootY = wy;
            switch (outward) {
                case Facing.Type.Up: rootY = wy + 2; break;
                case Facing.Type.Down: rootY = wy - 1 - h; break;
                case Facing.Type.Right: rootX = wx + 2; break;
                case Facing.Type.Left: rootX = wx - 1 - w; break;
            }
            var root = Pos.FromXY(rootX, rootY);
            if (Pos.IsOOB(root)) { return false; }
            var port = proto.Transform.RotatedOutPort(root);
            if (Pos.IsOOB(port)) { return false; }
            // The port has to land on the pad, in front of a wall of this pad
            var door = Facing.NeighborAt(port, Facing.Opposite(outward));
            var doorIdx = -1;
            for (int i = padStart; i < padStart + padLength; i++) {
                if (run.WallAt(i) == door) {
                    doorIdx = i;
                    break;
                }
            }
            // Not at the pad's ends either: the airlock needs wall on both sides
            if (doorIdx <= padStart || doorIdx >= padStart + padLength - 1) { return false; }
            if (spec.InvincibleParts.Contains(door)) { return false; }
            var inside = Facing.NeighborAt(door, Facing.Opposite(outward));
            if (spec.InvincibleParts.Contains(inside)) { return false; }
            if (!EntityUtils.IsEmptyFloorAt(inside)) { return false; }
            // AirlockGenStage's rule: nothing standing next to the door
            for (int i = 0; i < 4; i++) {
                if (EntityUtils.IsObjAt(Pos.NeighborAt(door, i))) { return false; }
            }
            var target = spec.HasCPU ? spec.CPUPos : Pos.FromXY(spec.CenterX, spec.CenterY);
            spot = new Spot {
                Run = run,
                PadStart = padStart,
                PadLength = padLength,
                Root = root,
                Door = door,
                Score = Pos.SqrDistance(door, target),
            };
            return true;
        }

        private bool TryBuild(Def def, Spot spot) {
            var outward = spot.Run.Outward;
            pad.Clear();
            padTiles.Clear();
            for (int i = spot.PadStart; i < spot.PadStart + spot.PadLength; i++) {
                pad.Add(Facing.NeighborAt(spot.Run.WallAt(i), outward));
            }
            foreach (var p in pad) {
                var floor = BuildTile(p, EntityUtils.CreatePrototype<Tile>(
                    DefIds.StructureFloorsFloorReinforced01));
                if (floor == null) {
                    RemovePad();
                    return false;
                }
                padTiles.Add(floor);
            }
            var proto = EntityUtils.CreatePrototype<Tile>(def);
            proto.Transform.Rotation = Facing.Opposite(outward);
            if (!CmdPlaceTile.CanPlace(spot.Root, proto, WorldLayer.Objects, warn: false)
                    || BuildTile(spot.Root, proto) == null) {
                RemovePad();
                return false;
            }
            BuildTile(spot.Door, EntityUtils.CreatePrototype<Tile>(
                DefIds.StructureDoorsDoorAirlock01));
            var door = EntityUtils.WallAt(spot.Door);
            if (door?.Door == null) {
                D.Warn("{0} Could not put an airlock behind the collector at {1}",
                    LogPrefix, Pos.String(spot.Door));
            } else {
                door.Door.SetClosed(true);
            }
            S.Sig.ForceAreas.Send();
            return true;
        }

        private void RemovePad() {
            foreach (var t in padTiles) {
                new CmdRemoveTile(t).Execute(S);
            }
            padTiles.Clear();
        }
    }
}
