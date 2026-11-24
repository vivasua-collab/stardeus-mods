using System.Collections.Generic;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    // Rooms on the new-game ship preview, in grayscale. The preview only runs the hull stages,
    // with no game map, so the rooms are grown with RoomPartitioner and the ship's rolled
    // layout on the hull the preview drew. They show what kind of interior the seed has,
    // not its exact walls: the real generation grows them after engines, rounded corners and
    // outer walls have changed the hull a little.
    public static class ShipPreviewRooms {
        private static readonly Color32 Wall = new(255, 255, 255, 255);
        private static readonly Color32 Corridor = new(70, 70, 70, 255);
        private static readonly Color32 CenterRoomShade = new(150, 150, 150, 255);
        private static readonly Color32 Unsplit = new(115, 115, 115, 255);

        public static void Draw(Texture2D tex, ShipFactory factory) {
            var layout = factory.Layout;
            var spec = factory.Spec;
            if (layout == null || !layout.HasRooms || spec == null) { return; }
            int texW = tex.width;
            int texH = tex.height;
            var px = tex.GetPixels32();
            bool IsHull(int x, int y) => x >= 0 && y >= 0 && x < texW && y < texH
                && px[y * texW + x].a > 127;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < texH; y++) {
                for (int x = 0; x < texW; x++) {
                    if (!IsHull(x, y)) { continue; }
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }
            if (minX > maxX) { return; }
            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            var owner = new int[w * h];
            var isWall = new bool[w * h];
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    int wx = x + minX;
                    int wy = y + minY;
                    var i = x + y * w;
                    if (!IsHull(wx, wy)) {
                        owner[i] = RoomPartitioner.Outside;
                        continue;
                    }
                    // the outer wall: hull tiles that touch space, diagonals included
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            if (!IsHull(wx + dx, wy + dy)) {
                                edge = true;
                                break;
                            }
                        }
                    }
                    isWall[i] = edge;
                    owner[i] = edge ? RoomPartitioner.Outside : RoomPartitioner.Free;
                }
            }

            var centerRoom = CenterRoomTiles(spec, layout, owner, isWall, minX, minY, w, h);

            var rng = new Rng(Hashes.Quick(spec.Seed, 7907));
            var partitioner = new RoomPartitioner(rng, layout.RoomSizeMul,
                layout.Rooms == RoomVariant.Spine, layout.RoomSymmetry);
            // no engines on the preview, only the CPU to keep the stasis room from
            var blocked = new bool[w * h];
            RoomPartitioner.BlockAround(blocked, w, h, spec.CenterX - minX, spec.CenterY - minY,
                RoomGrowthGenStage.StasisCPUClearance);
            var reservations = RoomPartitioner.ReservationsFor(layout.StasisRooms,
                layout.ShuttleRooms);
            var split = partitioner.Run(owner, w, h, spec.CenterY - minY, reservations, blocked,
                new Vector2(spec.CenterX - minX, spec.CenterY - minY));
            var walls = new List<int>();
            if (split) {
                partitioner.CollectWalls(walls);
            }
            foreach (var i in walls) {
                isWall[i] = true;
            }

            for (int i = 0; i < owner.Length; i++) {
                int x = i % w + minX;
                int y = i / w + minY;
                Color32 col;
                if (isWall[i]) {
                    col = Wall;
                } else if (centerRoom.Contains(i)) {
                    col = CenterRoomShade;
                } else if (owner[i] == RoomPartitioner.Outside) {
                    continue;
                } else if (owner[i] <= 0) {
                    col = Unsplit;
                } else if (partitioner.IsCorridor(owner[i])) {
                    col = Corridor;
                } else {
                    col = RoomShade(owner[i]);
                }
                px[y * texW + x] = col;
            }
            tex.SetPixels32(px);
        }

        // Grays between 95 and 205, scattered so neighbors rarely match
        private static Color32 RoomShade(int roomId) {
            var t = (roomId * 0.61803398875f) % 1f;
            var v = (byte) Mathf.RoundToInt(95 + t * 110);
            return new Color32(v, v, v, 255);
        }

        // The round CPU room InnerWallGenStage cuts at the ship's center when it fits: its ring
        // is wall, its inside stays out of the partition
        private static HashSet<int> CenterRoomTiles(ShipSpec spec, ShipLayout layout, int[] owner,
                bool[] isWall, int minX, int minY, int w, int h) {
            var inside = new HashSet<int>();
            if (layout.CenterAreaSize <= 0) { return inside; }
            var radius = layout.CenterAreaSize + 0.6f;
            var center = Pos.FromXY(spec.CenterX, spec.CenterY);
            bool fits = true;
            Pos.ForEachInCircle(center, radius, false, p => {
                Pos.ToXY(p, out var x, out var y);
                x -= minX;
                y -= minY;
                if (x < 0 || y < 0 || x >= w || y >= h || owner[x + y * w] != RoomPartitioner.Free) {
                    fits = false;
                    return;
                }
                inside.Add(x + y * w);
            });
            if (!fits) {
                inside.Clear();
                return inside;
            }
            foreach (var i in inside) {
                owner[i] = RoomPartitioner.Outside;
            }
            Pos.ForEachInCircle(center, radius, true, p => {
                Pos.ToXY(p, out var x, out var y);
                var i = x - minX + (y - minY) * w;
                isWall[i] = true;
                inside.Remove(i);
            });
            return inside;
        }
    }
}
