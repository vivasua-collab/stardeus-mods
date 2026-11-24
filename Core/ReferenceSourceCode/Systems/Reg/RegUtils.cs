using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Burst;

namespace Game.Systems.Reg {
    public static class RegUtils {
        public static int ChunksPerEdge(int mapW) {
            var fChunksPerEdge = ((float) mapW) / Consts.MapChunkSize;
            int chunksPerEdge = mapW / Consts.MapChunkSize;
            if (((int) fChunksPerEdge) != chunksPerEdge) {
                D.Err("Map does not divide into chunks without fraction! {0}x{1}. Chunk: {2}x{2}",
                    mapW, mapW, Consts.MapChunkSize);
            } else {
                // D.Log("Map divides into chunks without fraction: {0}x{1}. Chunk: {2}x{2}",
                //     w, h, Consts.MapChunkSize);
            }
            return chunksPerEdge;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToChunkIdPos(int pos, int GridH, int chunksPerEdge) {
            // Pos.ToXY(pos, out var x, out var y);
            var x = pos / GridH;
            var y = pos - GridH * x;
            return ToChunkId(x, y, chunksPerEdge);
        }
        public static int ToChunkIdSafe(int pos, int chunksPerEdge, int gridSize) {
            if (pos < 0 || pos > gridSize) {
                return -1;
            }
            Pos.ToXY(pos, out var x, out var y);
            return ToChunkId(x, y, chunksPerEdge);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToChunkId(int x, int y, int chunksPerEdge) {
            var cx = x / Consts.MapChunkSize;
            var cy = y / Consts.MapChunkSize;
            return (cx * chunksPerEdge) + cy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ChunkIdToXY(int chunkId, int chunksPerEdge,
                out int x, out int y) {
            int cy = chunkId % chunksPerEdge; // Get Y position in chunk grid
            int cx = chunkId / chunksPerEdge; // Get X position in chunk grid

            // Convert chunk grid positions back to world coordinates
            x = cx * Consts.MapChunkSize;
            y = cy * Consts.MapChunkSize;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ChunkBounds(int chunkId,
                int chunksPerEdge,
                out int startX,
                out int endX,
                out int startY,
                out int endY) {

            ChunkIdToXY(chunkId, chunksPerEdge, out startX, out startY);
            endX = startX + Consts.MapChunkSize;
            endY = startY + Consts.MapChunkSize;
        }

        /// <summary>
        /// Returns the closest reachable Item according to the regions.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="all"></param>
        /// <param name="tPos">This function should return the position of the item if it returns the invalid position it should skip the item </param>
        /// <returns>Closest reachable item. From all </returns>
        /// <param name="permissions">Doors that deny these are closed. None counts every door
        /// as open, locked ones too</param>
        public static T FindClosestReachable<T>(GameState S,
                int startPos, bool isGrounded,
                T[] all, int cnt, Func<T, int> tPos, bool onTop = true,
                NavPermission permissions = NavPermission.None) where T : class {
            if (cnt == 0) {
                return null;
            }
            var reqPool = ArrayPool<RegPathReq>.Shared;
            var regPathReqs = reqPool.Rent(cnt);
            var resPool = ArrayPool<int>.Shared;
            var resPathLengths = resPool.Rent(cnt);
            // for (int i = 0; i < cnt; i++) {
            // }
            for (int i = 0; i < cnt; i++) {
                var item = all[i];
                if (item == null) {
                    regPathReqs[i] = default;
                    continue;
                }
                var pos = tPos(item);
                if (pos > 0) {
                    if (onTop) {
                        regPathReqs[i] = RegPathReq.To(startPos, pos, isGrounded);
                    } else {
                        regPathReqs[i] = RegPathReq.Near(startPos, pos, isGrounded);
                    }
                    regPathReqs[i].Permissions = permissions;
                } else {
                    regPathReqs[i] = default;
                }
            }
            T closest = default;
            if (S.Query.RegPathLengths.Ask(regPathReqs, cnt, resPathLengths)) {
                float closestDist = float.MaxValue;
                for (int i = 0; i < cnt; i++) {
                    var dist = resPathLengths[i];
                    if (dist < 0) { continue; }
                    var s = all[i];
                    // to break the monotony of always going for the same item
                    // in same region

                    if (dist + Rng.UValue < closestDist) {
                        closest = s;
                        closestDist = dist;
                        if (dist < 0.5f) {
                            break;
                        }
                    }
                }
            }
            reqPool.Return(regPathReqs);
            resPool.Return(resPathLengths);
            return closest;

        }
    }
}
