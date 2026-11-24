using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using KL.Grid;
using UnityEngine;

namespace Game.Procgen {
    // Splits every hull area into rooms with RoomPartitioner, builds the walls between them and
    // records the corridors, which TreeDoorGenStage treats as hubs. The largest area also gets
    // the rooms reserved for the stasis arrays and the shuttle.
    [MustGoAfterGenStage(typeof(InnerWallGenStage))]
    public class RoomGrowthGenStage : BaseShipGenStage {
        // smaller areas stay one room
        public const int MinAreaToSplit = 60;
        // StasisAreaGenStage keeps the array this far from the CPU and the engines: the largest
        // minDistToCPU and minDistToEngine a scenario passes, plus the diagonal step from the
        // ship's center to the CPU's corner, where the stage measures from
        public const float StasisCPUClearance = 11.5f;
        public const float StasisEngineClearance = 8f;
        public override bool RequiresAreas => true;
        private readonly ShipLayout layout;
        private readonly HashSet<int> walls = new();
        private readonly List<int> buffer = new();
        private readonly List<int> engines = new();

        public RoomGrowthGenStage(ShipLayout layout) {
            this.layout = layout;
        }

        public override void Apply() {
            walls.Clear();
            engines.Clear();
            foreach (var eng in S.Components.GenFind<ShipEngineComp>()) {
                if (!IsWithinGenArea(eng.Tile.PosIdx)) { continue; }
                engines.Add(eng.Entity.PosIdx);
            }
            var areas = S.Sys.Areas.Areas
                .Where(a => a.Key > 0)
                .Select(a => a.Value)
                .Where(a => a.Count >= MinAreaToSplit)
                .Where(a => IsWithinGenArea(a.Min()))
                .Where(a => !GenUtils.IsCenterRoom(spec, a))
                .OrderBy(a => a.Min())
                .ToList();
            var largest = areas.OrderByDescending(a => a.Count).FirstOrDefault();
            foreach (var area in areas) {
                Partition(area, area == largest);
            }
            GenUtils.FillWallGaps(walls);
            BuildTiles(walls, DefIds.StructureWallsWall01);
        }

        private void Partition(HashSet<int> area, bool withReservations) {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var p in area) {
                Pos.ToXY(p, out var x, out var y);
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            var owner = new int[w * h];
            for (int i = 0; i < owner.Length; i++) {
                owner[i] = RoomPartitioner.Outside;
            }
            foreach (var p in area) {
                Pos.ToXY(p, out var x, out var y);
                owner[x - minX + (y - minY) * w] = RoomPartitioner.Free;
            }
            List<RoomPartitioner.Reservation> reservations = null;
            bool[] blocked = null;
            if (withReservations) {
                reservations = RoomPartitioner.ReservationsFor(layout.StasisRooms,
                    layout.ShuttleRooms);
                blocked = new bool[w * h];
                RoomPartitioner.BlockAround(blocked, w, h, spec.CenterX - minX,
                    spec.CenterY - minY, StasisCPUClearance);
                foreach (var e in engines) {
                    Pos.ToXY(e, out var ex, out var ey);
                    RoomPartitioner.BlockAround(blocked, w, h, ex - minX, ey - minY,
                        StasisEngineClearance);
                }
            }
            var partitioner = new RoomPartitioner(rng, layout.RoomSizeMul,
                layout.Rooms == RoomVariant.Spine, layout.RoomSymmetry);
            var cpu = new Vector2(spec.CenterX - minX, spec.CenterY - minY);
            if (!partitioner.Run(owner, w, h, spec.CenterY - minY, reservations, blocked, cpu)) {
                return;
            }
            buffer.Clear();
            partitioner.CollectWalls(buffer);
            foreach (var i in buffer) {
                walls.Add(Pos.FromXY(i % w + minX, i / w + minY));
            }
            buffer.Clear();
            partitioner.CollectCorridorTiles(buffer);
            foreach (var i in buffer) {
                spec.CorridorTiles.Add(Pos.FromXY(i % w + minX, i / w + minY));
            }
            if (reservations == null) { return; }
            foreach (var res in reservations) {
                if (res.Center < 0) { continue; }
                var p = Pos.FromXY(res.Center % w + minX, res.Center / w + minY);
                if (res.Kind == RoomPartitioner.ReservedRoom.Stasis) {
                    spec.StasisRoomHints.Add(p);
                } else {
                    spec.ShuttleRoomHints.Add(p);
                }
            }
        }
    }
}
