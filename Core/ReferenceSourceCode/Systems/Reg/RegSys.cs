// #define CACHE_REG_PATHS
#if !PRODUCTION_BUILD
// #define DEBUG_REG_FF
// #define VALIDATE_REG_TYPES
// #define DEBUG_CACHE
// #define REGION_JOB_IMMEDIATE
// #define REGION_PATH_JOB_IMMEDIATE
// #define PROFILE_REG_JOB
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using NavPermission = Game.Systems.Path.NavPermission;
using KL.Clock;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Game.Systems.Areas;
using Game.Systems.Atmo;
using Game.Systems.AI;
using Game.Systems.Motion;

namespace Game.Systems.Reg {
    public sealed class RegSys : GameSystem, IOverlayProvider {
        public const string SysId = "RegSys";
        public override string Id => SysId;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new RegSys());
        }
        private RegViz viz;
        public AreaSys Areas;
        public PathSys Path;
        private readonly int[] RegPosCache = new int[Consts.MapChunkSizeSquared];
        public List<OverlayInfo> Overlays => viz.Overlays;

        // idx = posIdx, val = region id
        // 640*640 ~ 400000 -> could gave over 65K regions (over max ushort)
        public NativeArray<int> PosRegIds;
        // idx = reg id, val = chunk id
        public NativeList<int> RegChunkIds;
        // idx = reg id, val = reg type
        public NativeList<AreaType> RegTypes;
        // idx = reg id, val = index of RegEdges where the edges for this reg id begins
        public NativeList<int> RegEdgeStarts;
        // idx = reg id, val = index of RegPos where the positions for this reg id begins
        public NativeList<int> RegPosStarts;
        // idx = array index, used in RegEdgeStarts, val = reg id
        public NativeList<int> RegEdges;
        // idx = array index, used in RegPosStarts, val = pos idx
        public NativeList<int> RegPos;
        // idx = reg id, val = deny mask for door regions (NavPermissions.All = locked/impassable)
        public NativeList<NavPermission> RegDeny;
        // idx = posIdx, val = true if the position has a cell-adjacent passable
        // neighbor (per-position check for Wall/BlockedByObject regions)
        public NativeArray<bool> RegHasPassableNeighbor;
        #if CACHE_REG_PATHS
        public readonly Dictionary<int, int> ResultCache = new();
        #endif
        public int NumRegions;
        // where each being can walk to, by the pathfinder's rules (BeingReach.md)
        public BeingReach Reach;

        // chunks per map edge
        public int ChunksPerEdge;
        private bool needsRecalc;
        private int gridSize;
        private int gridH;
        private readonly Queue<(int, int)> regionQueue = new();
        private HashSet<int> visitedRegions = new();
        // FindClosestNearby keeps its own collections, so a check callback that asks
        // another region query can't corrupt the search
        private readonly List<int> closestLevel = new();
        private readonly List<int> closestNextLevel = new();
        private readonly HashSet<int> closestVisited = new();
        private readonly HashSet<int> closestScanned = new();
        private void PrepareData(int gridSize) {
            PosRegIds = new NativeArray<int>(gridSize, Allocator.Persistent);
            RegChunkIds = new NativeList<int>(Allocator.Persistent);
            RegTypes = new NativeList<AreaType>(Allocator.Persistent);
            RegEdgeStarts = new NativeList<int>(Allocator.Persistent);
            RegEdges = new NativeList<int>(Allocator.Persistent);
            RegPosStarts = new NativeList<int>(Allocator.Persistent);
            RegPos = new NativeList<int>(Allocator.Persistent);
            RegDeny = new NativeList<NavPermission>(Allocator.Persistent);
            RegHasPassableNeighbor = new NativeArray<bool>(gridSize, Allocator.Persistent);
        }

        private void UnloadData() {
            PosRegIds.Dispose();
            RegChunkIds.Dispose();
            RegTypes.Dispose();
            RegEdgeStarts.Dispose();
            RegEdges.Dispose();
            RegPosStarts.Dispose();
            RegPos.Dispose();
            RegDeny.Dispose();
            RegHasPassableNeighbor.Dispose();
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RebuildRegs.AddListener(MarkRegsDirty);
            viz = new RegViz(S, this);
        }

        private void OnLoadSave(GameState state) {
            Areas = S.Sys.Areas;
            Path = S.Sys.Path;
            Path.Reg = this;
            gridSize = S.GridSize;
            gridH = S.GridHeight;
            ChunksPerEdge = RegUtils.ChunksPerEdge(state.GridWidth);

            PrepareData(state.GridSize);
            Reach = new BeingReach(S, this);
            S.Sig.AtmoRecalculated.AddListener(OnAtmoRecalculated);

            S.Clock.OnTickProgress.AddListener(OnTickProgress);
            S.Clock.OnTick.AddListener(OnTick);
            S.Sig.EveryFrame.AddListener(OnEveryFrame);
            S.Sig.AreasRecalculated.AddListener(MarkRegsDirty);
            S.Query.RegionDist.SetResponder(RegionPathLength);
            S.Query.RegionBreachDoor.SetResponder(RegionBreachDoor);
            S.Query.RegionPositions.SetResponder(RegionPositions);
            S.Query.RegionPositionsAt.SetResponder(RegionPositionsAt);
            S.Query.FindNearby.SetResponder(FindNearby);
            S.Query.FindClosestNearby.SetResponder(FindClosestNearby);
            S.Query.FindNearbyRandom.SetResponder(FindNearbyRandom);
            S.Query.FindTileNearby.SetResponder(FindTileNearby);
            S.Query.RegionAt.SetResponder(RegionAt);
            S.Query.RegionTypeAt.SetResponder(RegionTypeAt);
            S.Query.RegPathLengths.SetResponder(RegionPathLengths);
            S.Query.RegionDists.SetResponder(RegionDists);
            S.Query.RandomSafePosFrom.SetResponder(RandomSafePosFrom);
            S.Query.RandomSafePosNearby.SetResponder(RandomSafePosNearby);
            S.Query.IsRegionReachable.SetResponder(IsRegionReachable);

            viz.OnLoadSave();
        }

        private void OnAtmoRecalculated(AtmoLayer layer) {
            Reach.OnAtmoChanged();
        }

        private bool IsRegionReachable(int posIdx) {
            return RegHasPassableNeighbor[posIdx];
        }

        private void OnTick(long parameter) {
            OnEveryFrame();
        }

        private void OnTickProgress(float prog) {
            OnEveryFrame();
        }

        private int RandomSafePosNearby(int nearPos, AIAgentComp agent, int depth, float maxDiff) {
            using (D.Timer("RandomSafePosNearby", 0.0001f)) {
                var reg = PosRegIds[nearPos];
                var isG = agent.Motion.IsGrounded;
                var pos = agent.PosIdx;
                var req = new RegPathReq {
                    FromPos = pos,
                    ToPos = nearPos,
                    Flags = isG ? RegPathReqFlags.Grounded : RegPathReqFlags.None,
                };
                var pathLen = RegionPathLength(req);
                if (pathLen < 0) {
                    if (!isG) {
                        D.Warn("Won't try to find random safe pos nearby, no region path between agent and target! {0} -> {1}",
                            Pos.String(nearPos), agent);
                    }
                    return Pos.Invalid;
                }
                regionQueue.Clear();
                visitedRegions.Clear();
                visitedRegions.Add(reg);
                regionQueue.Enqueue((reg, 0));
                Func<int, bool> check = (p) => Path.IsNavigable(p, isG, maxDiff)
                    && agent.IsFeelingSafeAt(p);
                if (TryFindInRegionQueueBFS(regionQueue, isG, depth, check, out var foundPos)) {
                    return foundPos;
                }
                return Pos.Invalid;
            }
        }

        // This includes unstucking
        private int RandomSafePosFrom(AIAgentComp agent, int depth, float maxDiff) {
            using (D.Timer("RandomSafePosFrom", 0.0001f)) {
                var pos = agent.PosIdx;
                var reg = PosRegIds[pos];
                var regType = RegTypes[reg];
                var isG = agent.Motion.IsGrounded;
                Func<int, bool> check = (p) => Path.IsNavigable(p, isG, maxDiff)
                    && agent.IsFeelingSafeAt(p);
                // unstuck
                if (regType == AreaType.Wall || regType == AreaType.BlockedByObject || isG && regType == AreaType.Space) {
                    bool foundAlternative = false;
                    for (int i = 0; i < 8; i++) {
                        var npos = Pos.NeighborAt(pos, i);
                        if (check(npos)) {
                            reg = PosRegIds[npos];
                            foundAlternative = true;
                            break;
                        }
                    }
                    if (!foundAlternative) {
                        D.Err("Cannot get random navigable position from: {0} (stuck!), {1}",
                            Pos.String(pos), agent);
                        return Pos.Invalid;
                    }
                }
                regionQueue.Clear();
                visitedRegions.Clear();
                visitedRegions.Add(reg);
                regionQueue.Enqueue((reg, 0));
                if (TryFindInRegionQueueBFS(regionQueue, isG, depth, check, out var foundPos)) {
                    return foundPos;
                }
                return Pos.Invalid;
            }
        }


        private int RegionAt(int pos) {
            return PosRegIds[pos];
        }
        private AreaType RegionTypeAt(int pos) {
            var regId = PosRegIds[pos];
            return RegTypes[regId];
        }

        private Tile FindTileNearby(int pos, bool isGrounded, int depth, int worldLayer, Func<Tile, bool> check) {
            regionQueue.Clear();
            visitedRegions.Clear();
            var reg = PosRegIds[pos];
            if (worldLayer == WorldLayer.Walls) {
                if (RegTypes[reg] != AreaType.Wall && RegTypes[reg] != AreaType.BlockedByObject) {
                    D.Err("Trying to find a Wall tile through regions with start region being no a wall! This won't work: {0} / {1}",
                        Pos.String(pos), RegTypes[reg]);
                    return null;
                }
            }
            regionQueue.Enqueue((reg, 0));
            using (D.Timer("FindTileNearby", 0.00001f)) {
                if (TryFindTileInRegionQueueBFS(regionQueue, isGrounded, depth, worldLayer, check, out var foundTile)) {
                    return foundTile;
                }
            }
            return null;
        }
        private bool TryFindTileInRegionQueueBFS(Queue<(int, int)> q,
                bool isGrounded, int maxDepth,
                int worldLayer, Func<Tile, bool> check, out Tile foundTile) {
            while (q.TryDequeue(out var regWithDepth)) {
                var depth = regWithDepth.Item2;
                if (depth > maxDepth) {
                    continue;
                }
                var reg = regWithDepth.Item1;
                var regType = RegTypes[reg];
                if (regType == AreaType.Wall) {
                    continue;
                }
                if (isGrounded && regType == AreaType.Space) {
                    continue;
                }
                if (regType != AreaType.Door) {
                    depth++;
                    int found = RegionPositions(reg, RegPosCache);
                    Arrays.Shuffle(Rng.Unseeded, RegPosCache, found);
                    if (worldLayer == WorldLayer.Objects) {
                        Caches.IntSet.Clear();
                        for (int i = 0; i < found; i++) {
                            var p = RegPosCache[i];
                            var tile = EntityUtils.ParentObjAt(p);
                            if (tile != null && Caches.IntSet.Add(tile.Id)) {
                                if (check(tile)) {
                                    foundTile = tile;
                                    return true;
                                }
                            }
                            // BlockedByObject regions have no edges to
                            // Floor regions - check neighbors directly
                            for (int n = 0; n < 8; n++) {
                                var np = Pos.NeighborAt(p, n);
                                if (Pos.IsOOB(np)) { continue; }
                                if (RegTypes[PosRegIds[np]] != AreaType.BlockedByObject) {
                                    continue;
                                }
                                var nTile = EntityUtils.ParentObjAt(np);
                                if (nTile != null && Caches.IntSet.Add(nTile.Id)) {
                                    if (check(nTile)) {
                                        foundTile = nTile;
                                        return true;
                                    }
                                }
                            }
                        }
                    } else {
                        for (int i = 0; i < found; i++) {
                            var p = RegPosCache[i];
                            var tile = EntityUtils.TileAt(p, worldLayer);
                            if (tile == null) { continue; }
                            if (check(tile)) {
                                foundTile = tile;
                                return true;
                            }
                        }
                    }
                }
                if (depth < maxDepth) {
                    var from = RegEdgeStarts[reg];
                    var to = RegEdgeStarts[reg + 1];
                    for (int i = from; i < to; i++) {
                        var edge = RegEdges[i];
                        if (visitedRegions.Add(edge)) {
                            q.Enqueue((edge, depth));
                        }
                    }
                }
            }
            // D.Warn("Burned through all region depth! (tile: {0})", maxDepth);
            foundTile = null;
            return false;
        }

        private int FindNearby(int pos, bool isGrounded, int depth, Func<int, bool> check) {
            regionQueue.Clear();
            visitedRegions.Clear();
            var reg = PosRegIds[pos];
            visitedRegions.Add(reg);
            regionQueue.Enqueue((reg, 0));
            using (D.Timer("FindNearby", 0.000001f)) {
                if (TryFindInRegionQueueBFS(regionQueue, isGrounded, depth, check, out var foundPos)) {
                    return foundPos;
                }
            }
            return Pos.Invalid;
        }

        // In the room pass, straight line distance competes across this many levels after
        // the first level with a match. The chunks around the mover are at most 2 levels
        // from its own region, so a chunk border can't outrank a spot right next to the
        // mover, while spots that need a long walk around a wall stay out
        private const int RoomPassExtraLevels = 2;

        // Room first: doors are crossed only when the connected area around fromPos
        // has no match, so the mover doesn't leave the room with work still in it
        private int FindClosestNearby(int fromPos, MotionControl motion, int maxDist,
                Func<int, bool> check) {
            if (NumRegions == 0) { return Pos.Invalid; }
            closestScanned.Clear();
            var found = FindClosestByRegionLevels(fromPos, motion, maxDist, false,
                RoomPassExtraLevels, check, out var skippedDoor);
            if (found == Pos.Invalid && skippedDoor) {
                // strict levels through doors, the nearest room by hops wins
                found = FindClosestByRegionLevels(fromPos, motion, maxDist, true, 0,
                    check, out _);
            }
            return found;
        }

        private int FindClosestByRegionLevels(int fromPos, MotionControl motion, int maxDist,
                bool crossDoors, int extraLevels, Func<int, bool> check,
                out bool skippedDoor) {
            skippedDoor = false;
            var isGrounded = motion.IsGrounded;
            var permissions = motion.Permissions;
            var maxDistSqr = maxDist * maxDist;
            Pos.ToXY(fromPos, out var fromX, out var fromY);
            var level = closestLevel;
            var nextLevel = closestNextLevel;
            level.Clear();
            nextLevel.Clear();
            closestVisited.Clear();
            var startReg = PosRegIds[fromPos];
            level.Add(startReg);
            closestVisited.Add(startReg);
            int best = Pos.Invalid;
            int bestDist = int.MaxValue;
            // levels still to scan after the first level with a match
            int levelsLeft = -1;
            while (level.Count > 0) {
                for (int i = 0; i < level.Count; i++) {
                    var reg = level[i];
                    // regions scanned by the room pass have no match
                    if (!closestScanned.Add(reg)) { continue; }
                    var to = RegPosStarts[reg + 1];
                    for (int j = RegPosStarts[reg]; j < to; j++) {
                        var p = RegPos[j];
                        var dist = Pos.SqrDistance(fromPos, p);
                        if (dist > maxDistSqr || dist >= bestDist) { continue; }
                        if (check(p)) {
                            best = p;
                            bestDist = dist;
                        }
                    }
                }
                // Straight line distance only competes within the first level with a
                // match and the extra levels after it. Beyond that, a region right
                // behind a wall would win whenever the walk to it is a few hops longer
                if (best != Pos.Invalid) {
                    if (levelsLeft < 0) {
                        levelsLeft = extraLevels;
                    }
                    if (levelsLeft == 0) { return best; }
                    levelsLeft--;
                }
                // once there is a match, only regions that could hold a closer one matter
                var boundSqr = best == Pos.Invalid ? maxDistSqr : bestDist - 1;
                for (int i = 0; i < level.Count; i++) {
                    var reg = level[i];
                    var to = RegEdgeStarts[reg + 1];
                    for (int j = RegEdgeStarts[reg]; j < to; j++) {
                        var edge = RegEdges[j];
                        if (closestVisited.Contains(edge)) { continue; }
                        if (!IsRegPassable(edge, isGrounded, permissions)) { continue; }
                        if (!IsChunkWithinDist(RegChunkIds[edge], fromX, fromY, boundSqr)) {
                            continue;
                        }
                        if (!crossDoors && RegTypes[edge] == AreaType.Door) {
                            skippedDoor = true;
                            continue;
                        }
                        closestVisited.Add(edge);
                        nextLevel.Add(edge);
                    }
                }
                (level, nextLevel) = (nextLevel, level);
                nextLevel.Clear();
            }
            return best;
        }

        private bool IsRegPassable(int reg, bool isGrounded, NavPermission permissions) {
            switch (RegTypes[reg]) {
                case AreaType.Space:
                    return !isGrounded;
                case AreaType.Door:
                    return (RegDeny[reg] & permissions) == NavPermission.None;
                case AreaType.Floor:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsChunkWithinDist(int chunkId, int x, int y, int maxDistSqr) {
            RegUtils.ChunkBounds(chunkId, ChunksPerEdge,
                out var startX, out var endX, out var startY, out var endY);
            var dx = Mathf.Clamp(x, startX, endX - 1) - x;
            var dy = Mathf.Clamp(y, startY, endY - 1) - y;
            return dx * dx + dy * dy <= maxDistSqr;
        }

        private int FindNearbyRandom(int pos, bool isGrounded, int depth, Func<int, bool> check) {
            if (pos < 1) {
                D.Err("Trying to find random nearby position from pos: {0}! Will return pos.invalid", pos);
                return Pos.Invalid;
            }
            if (depth < 2) {
                return FindNearby(pos, isGrounded, depth, check);
            }
            regionQueue.Clear();
            visitedRegions.Clear();
            var reg = PosRegIds[pos];
            visitedRegions.Add(reg);
            regionQueue.Enqueue((reg, 0));
            var cache = Caches.PosIdx;
            using (D.Timer("FindNearbyRandom", 0.000001f)) {
                int found = GetAllPosInRegionQueueBFS(regionQueue, isGrounded, depth, cache);
                if (found > 0) {
                    Arrays.Shuffle(Rng.Unseeded, cache, found);
                }
                for (int i = 0; i < found; i++) {
                    var p = cache[i];
                    if (check(p)) {
                        return p;
                    }
                }
            }
            return Pos.Invalid;
        }
        private int GetAllPosInRegionQueueBFS(Queue<(int, int)> q, bool isGrounded, int maxDepth,
                int[] positions) {
            int foundPositions = 0;
            int maxPositions = positions.Length;
            while (q.TryDequeue(out var regWithDepth)) {
                var depth = regWithDepth.Item2;
                if (depth > maxDepth) {
                    continue;
                }
                var reg = regWithDepth.Item1;
                var regType = RegTypes[reg];
                if (regType == AreaType.Wall || regType == AreaType.BlockedByObject) {
                    continue;
                }
                if (isGrounded && regType == AreaType.Space) {
                    continue;
                }
                if (regType != AreaType.Door) {
                    depth++;
                    var from = RegPosStarts[reg];
                    var to = RegPosStarts[reg + 1];
                    int found = 0;
                    for (int i = from; i < to; i++) {
                        RegPosCache[found++] = RegPos[i];
                    }
                    Arrays.Shuffle(Rng.Unseeded, RegPosCache, found);
                    for (int i = 0; i < found; i++) {
                        var p = RegPosCache[i];
                        // D.Log("Adding found position to: positions[{0}]. Maxlen: {1}", foundPositions, maxPositions);
                        positions[foundPositions++] = p;
                        if (foundPositions == maxPositions) {
                            // D.Log("Breaking due to Maxlen: {1}", foundPositions, maxPositions);
                            return foundPositions;
                            // break;
                        }
                    }
                }
                if (depth < maxDepth) {
                    var from = RegEdgeStarts[reg];
                    var to = RegEdgeStarts[reg + 1];
                    for (int i = from; i < to; i++) {
                        var edge = RegEdges[i];
                        if (visitedRegions.Add(edge)) {
                            q.Enqueue((edge, depth));
                        }
                    }
                }
            }
            // D.Warn("Burned through all region depth ({0})!", maxDepth);
            return foundPositions;
        }

        private bool TryFindInRegionQueueBFS(Queue<(int, int)> q, bool isGrounded, int maxDepth,
                Func<int, bool> check, out int foundPos) {
            bool foundDoor = false;
            while (q.TryDequeue(out var regWithDepth)) {
                var depth = regWithDepth.Item2;
                if (depth > maxDepth) {
                    continue;
                }
                var reg = regWithDepth.Item1;
                var regType = RegTypes[reg];
                if (regType == AreaType.Wall || regType == AreaType.BlockedByObject) {
                    continue;
                }
                if (isGrounded && regType == AreaType.Space) {
                    continue;
                }
                if (regType != AreaType.Door) {
                    depth++;
                    var from = RegPosStarts[reg];
                    var to = RegPosStarts[reg + 1];
                    int found = 0;
                    for (int i = from; i < to; i++) {
                        RegPosCache[found++] = RegPos[i];
                    }
                    Arrays.Shuffle(Rng.Unseeded, RegPosCache, found);
                    for (int i = 0; i < found; i++) {
                        var p = RegPosCache[i];
                        if (check(p)) {
                            foundPos = p;
                            return true;
                        }
                    }
                } else {
                    // door
                    if (!foundDoor) {
                        foundDoor = true;
                    } else {
                        // otherwise two doors side by side will lead to infinite loop
                        depth++;
                    }
                }
                if (depth < maxDepth) {
                    var from = RegEdgeStarts[reg];
                    var to = RegEdgeStarts[reg + 1];
                    for (int i = from; i < to; i++) {
                        var edge = RegEdges[i];
                        if (visitedRegions.Add(edge)) {
                            q.Enqueue((edge, depth));
                        }
                    }
                }
            }
            // D.Warn("Burned through all region depth ({0})!", maxDepth);
            foundPos = Pos.Invalid;
            return false;
        }

        private int RegionEdges(int regId, int[] res) {
            var from = RegEdgeStarts[regId];
            var to = RegEdgeStarts[regId + 1];
            int cnt = 0;
            for (int i = from; i < to; i++) {
                res[cnt++] = RegEdges[i];
            }
            return cnt;
        }

        private int RegionPositionsAt(int pos, int[] res) {
            return RegionPositions(PosRegIds[pos], res);
        }

        private int RegionPositions(int regId, int[] res) {
            var from = RegPosStarts[regId];
            var to = RegPosStarts[regId + 1];
            int cnt = 0;
            for (int i = from; i < to; i++) {
                res[cnt++] = RegPos[i];
            }
            return cnt;
        }

        private bool RegionPathLengths(RegPathReq[] requests, int cnt,
                int[] pathLength) {
            using (D.Timer("Region PF MULTI request", 0.005f)) {
            var reqArray = new NativeArray<RegPathReq>(cnt, Allocator.TempJob);
            NativeArray<RegPathReq>.Copy(requests, reqArray, cnt);
            #if CACHE_REG_PATHS
            var cacheIn = new NativeHashMap<int, int>(ResultCache.Count, Allocator.TempJob);
            var cacheOut = new NativeHashMap<int, int>(cnt, Allocator.TempJob);
            #endif
            var resArray = new NativeArray<int>(cnt, Allocator.TempJob);
            var job = new RegPathFindMultiJob {
                PosRegIds = PosRegIds,
                RegEdges = RegEdges,
                RegEdgeStarts = RegEdgeStarts,
                RegTypes = RegTypes,
                Requests = reqArray,
                RegPosStarts = RegPosStarts,
                RegPos = RegPos,
                RegDeny = RegDeny,
                OutPathLengths = resArray,
                FastPathHeuristics = PathSys.IsFastPathOrNoTeleporters,
                GridH = gridH,
                #if CACHE_REG_PATHS
                CacheIn = cacheIn,
                CacheOut = cacheOut,
                #endif
            };
            #if REGION_PATH_JOB_IMMEDIATE
            for (int i = 0; i < cnt; i++) {
                job.Execute(i);
            }
            #else
            var handle = job.Schedule(cnt, 4);
            handle.Complete();
            #endif

            #if CACHE_REG_PATHS
            cacheIn.Dispose();
            foreach (var c in cacheOut) {
                // D.Warn("Got new cache: {0}", cacheOut.Count);
                ResultCache[c.Key] = c.Value;
            }
            cacheOut.Dispose();
            #endif
            NativeArray<int>.Copy(resArray, pathLength, cnt);
            reqArray.Dispose();
            resArray.Dispose();
            return true;
            }
        }

        private bool RegionDists(RegDistsReq req, int[] outDists) {
            if (NumRegions == 0 || req.FromPos < 0 || req.FromPos >= gridSize) {
                return false;
            }
            var cnt = req.Count;
            var hasPenalties = req.Penalties != null;
            var targets = new NativeArray<int>(cnt, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<int>.Copy(req.Targets, targets, cnt);
            var penalties = new NativeArray<float>(cnt, Allocator.TempJob);
            if (hasPenalties) {
                NativeArray<float>.Copy(req.Penalties, penalties, cnt);
            }
            var dists = new NativeArray<int>(cnt, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            new RegDistsJob {
                PosRegIds = PosRegIds,
                RegTypes = RegTypes,
                RegEdgeStarts = RegEdgeStarts,
                RegEdges = RegEdges,
                RegDeny = RegDeny,
                Targets = targets,
                Penalties = penalties,
                OutDists = dists,
                FromPos = req.FromPos,
                NumRegions = NumRegions,
                MaxDist = req.MaxDist,
                IsGrounded = req.Grounded,
                Permissions = req.Permissions,
                HasPenalties = hasPenalties,
                Jitter = req.Jitter,
            }.Run();
            NativeArray<int>.Copy(dists, outDists, cnt);
            targets.Dispose();
            penalties.Dispose();
            dists.Dispose();
            return true;
        }

        private int RegionPathLength(RegPathReq req) {
            using (D.Timer("Region PF request", 0.00001f)) {
            var fromReg = PosRegIds[req.FromPos];
            var toReg = PosRegIds[req.ToPos];
            if (fromReg == toReg) {
                return 0;
            }
            #if CACHE_REG_PATHS
            int cacheHash = RegPathReq.CacheHash(fromReg, toReg, req.Flags, RegTypes[toReg]);
            if (cacheHash != 0 && ResultCache.TryGetValue(cacheHash, out var cacheRes)) {
                #if DEBUG_CACHE
                D.Warn("Cache hit! {0} -> {1}", req, cacheRes);
                #endif
                return cacheRes;
            }
            #endif
            var outPath = new NativeList<int>(Allocator.TempJob);
            #if PROFILE_REG_JOB
            var iterations = new NativeArray<int>(1, Allocator.TempJob);
            #endif
            var job = new RegPathFindJob {
                Request = req,
                PosRegIds = PosRegIds,
                RegEdges = RegEdges,
                RegEdgeStarts = RegEdgeStarts,
                RegTypes = RegTypes,
                RegPosStarts = RegPosStarts,
                RegPos = RegPos,
                RegDeny = RegDeny,
                OutPath = outPath,
                FastPathHeuristics = PathSys.IsFastPathOrNoTeleporters,
                GridH = gridH,
                #if PROFILE_REG_JOB
                Iterations = iterations,
                #endif
            };
            #if REGION_PATH_JOB_IMMEDIATE
            job.Execute();
            #else
            var handle = job.Schedule();
            handle.Complete();
            #endif
            #if PROFILE_REG_JOB
            D.Warn("Found region path: {0}: Iterations: {1} | {2}", req, iterations[0], D.Dump(outPath));
            iterations.Dispose();
            #endif
            var res = outPath.Length - 1;
            outPath.Dispose();
            #if CACHE_REG_PATHS
            if (cacheHash != 0) {
                ResultCache[cacheHash] = res;
            }
            #if DEBUG_CACHE
            D.Warn("Cache miss! {0} -> {1}", req, res);
            #endif
            #endif
            return res;
            }
        }

        // No goal direction bias here: it adds up to 5.7 per region that leads away from the
        // goal, so a short open way around would lose to a door that only points the right way
        private int RegionBreachDoor(RegPathReq req) {
            if (NumRegions == 0) { return -1; }
            req.Flags |= RegPathReqFlags.Breach;
            var outPath = new NativeList<int>(Allocator.TempJob);
            var job = new RegPathFindJob {
                Request = req,
                PosRegIds = PosRegIds,
                RegEdges = RegEdges,
                RegEdgeStarts = RegEdgeStarts,
                RegTypes = RegTypes,
                RegPosStarts = RegPosStarts,
                RegPos = RegPos,
                RegDeny = RegDeny,
                OutPath = outPath,
                FastPathHeuristics = false,
                GridH = gridH,
            };
            #if REGION_PATH_JOB_IMMEDIATE
            job.Execute();
            #else
            job.Schedule().Complete();
            #endif
            int res = -1;
            if (outPath.Length > 0) {
                res = 0;
                // the path is stored goal first. The start is left out: a unit standing in a
                // doorway can step out of it
                for (int i = outPath.Length - 2; i >= 0; i--) {
                    var reg = outPath[i];
                    if (RegTypes[reg] == AreaType.Door
                            && (RegDeny[reg] & req.Permissions) != NavPermission.None) {
                        res = RegPos[RegPosStarts[reg]];
                        break;
                    }
                }
            }
            outPath.Dispose();
            return res;
        }

        private void MarkRegsDirty() {
            // D.Warn("Marking regions dirty");
            needsRecalc = true;
        }

        public void UpdateRegDenyAt(int posIdx) {
            if (NumRegions == 0) { return; }
            var reg = PosRegIds[posIdx];
            if (reg < 0 || reg >= NumRegions) { return; }
            var regType = RegTypes[reg];
            // Regions are flood filled from area types (and teleporters, which rebuild by
            // themselves), so a passability change that left the area type alone leaves
            // the regions as they are. Rebuilding the whole map for every built or removed
            // object was a steady cost in large colonies
            if (regType != Areas.AreaTypes[posIdx]) {
                MarkRegsDirty();
                return;
            }
            if (regType == AreaType.Door) {
                var node = Path.Nodes[posIdx];
                var deny = node.IsPassable ? node.Deny : NavPermissions.All;
                if (RegDeny[reg] != deny) {
                    RegDeny[reg] = deny;
                    Reach.OnDoorsChanged();
                }
            }
        }

        private void OnEveryFrame() {
            if (needsRecalc) {
                if (!Ready.Areas || !Ready.Gen) {
                    // D.Warn("Trying to recalculate regions while areas not ready!");
                    return;
                }
                Recalculate();
                needsRecalc = false;
            }
        }

        public void Recalculate() {
            // D.Warn("[95:159] Start reg recalc. Area type: {0}", Areas.AreaTypes[Pos.FromXY(95, 159)]);
            var stats = new NativeArray<int>(6, Allocator.TempJob);
            var tpMap = Path.TeleporterMap;
            var teleporters = new NativeHashMap<int, int>(tpMap.Count, Allocator.TempJob);
            foreach (var tp in tpMap) {
                teleporters.Add(tp.Key, tp.Value);
            }
            var job = new RegFloodFillJob {
                AreaTypes = Areas.AreaTypes,
                PosRegIds = PosRegIds,
                RegChunkIds = RegChunkIds,
                RegEdges = RegEdges,
                RegEdgeStarts = RegEdgeStarts,
                RegPos = RegPos,
                RegPosStarts = RegPosStarts,
                RegTypes = RegTypes,
                RegHasPassableNeighbor = RegHasPassableNeighbor,
                Teleporters = teleporters,
                OutStats = stats,
                GridSize = gridSize,
                GridH = gridH,
                ChunksPerEdge = ChunksPerEdge,
            };
            using (D.Timer("RegFF", 0.0001f)) {
                #if REGION_JOB_IMMEDIATE
                job.Execute();
                #else
                var handle = job.Schedule(Path.PathJobHandle);
                handle.Complete();
                #endif
            }
            NumRegions = stats[0];

            // Build per-region deny masks from PathNode data
            RegDeny.Clear();
            RegDeny.Resize(NumRegions, NativeArrayOptions.ClearMemory);
            if (Path.Nodes.IsCreated) {
                for (int i = 0; i < NumRegions; i++) {
                    if (RegTypes[i] == AreaType.Door) {
                        var posIdx = RegPos[RegPosStarts[i]];
                        var node = Path.Nodes[posIdx];
                        if (!node.IsPassable) {
                            // Locked door - deny all
                            RegDeny[i] = NavPermissions.All;
                        } else {
                            RegDeny[i] = node.Deny;
                        }
                    }
                }
            }
            #if DEBUG_REG_FF
            D.Warn("Visits: {0}. Skips: {1}. Enqueues: {2}. FIlls: {3}. Edges Found: {4}",
                stats[1],
                stats[2],
                stats[3],
                stats[4],
                stats[5]);
            D.Warn("Got num regions: {0}", NumRegions);
            D.Warn("Region edges: {0}", RegEdges.Length);
            #endif

            stats.Dispose();
            teleporters.Dispose();
            Reach.OnRegionsChanged();
            #if CACHE_REG_PATHS
            ResultCache.Clear();
            #endif

            #if VALIDATE_REG_TYPES
            for (int i = 0; i < NumRegions; i++) {
                ref var start = ref RegPosStarts.GetUnsafeRef(i);
                ref var end = ref RegPosStarts.GetUnsafeRef(i + 1);
                for (int j = start; j < end; j++) {
                    ref var pos = ref RegPos.GetUnsafeRef(j);
                    var chunkId = RegUtils.ToChunkIdPos(pos, gridH, ChunksPerEdge);
                    ref var regChunkId = ref RegChunkIds.GetUnsafeRef(i);
                    ref var regType = ref RegTypes.GetUnsafeRef(i);
                    ref var areaType = ref Areas.AreaTypes.GetUnsafeRef(pos);
                    if (chunkId != regChunkId || regType != areaType) {
                        D.ErrSoft("Incorrect region chunk or area type at pos! {0}. chunk id ({1} -> actual {2}). RegType: {3} -> actual {4}",
                            Pos.StringD(pos), chunkId, RegChunkIds[i], Areas.AreaTypes[pos], RegTypes[i]);
                        The.ScreenBlock.HideImmediate();
                        EntityUtils.CameraFocusOn(pos);
                        S.Clock.SetSpeed(ClockSpeed.Stopped);
                        return;
                    }
                }
            }
            #endif
            // for (int i = 0; i < RegEdges.Length; i++) {
            //     D.Log("Edge: {0}->{1}", i, RegEdges[i]);
            // }
        }

        /// <returns>Pos</returns>
        // Not guaranteed to be unobstructed or safe

        public override void Unload() {
            // make sure there isn't a tangling pathfinding request
            A.S?.Sys?.Path?.EndJob();
            viz?.ReleaseResources();
            Reach?.Dispose();
            UnloadData();
        }
        public bool IsRegionWalkable(int region) {
            var type = RegTypes[region];
            return type == AreaType.Floor || type == AreaType.Door;
        }

        public int LinkedRegions(int region, int[] res) {
            int cnt = 0;
            var start = RegEdgeStarts[region];
            var end = RegEdgeStarts[region + 1];
            for (int i = start; i < end; i++) {
                res[cnt++] = RegEdges[i];
            }
            return cnt;
        }
    }
}
