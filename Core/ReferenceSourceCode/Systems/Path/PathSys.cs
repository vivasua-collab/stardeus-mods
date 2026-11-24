#define USE_FAST_JOB
#if !PRODUCTION_BUILD
// #define DEBUG_CLAIMS
// #define TRACE_REQUESTS
// #define DEBUG_TELEPORTERS
// #define IMMEDIATE_PATH
// JOB_DIRECT will only work if IMMEDIATE_PATH is enabled
// #define JOB_DIRECT
// #define DEBUG_PATH
// #define PROFILE_PATH
// #define VIZ_ASTAR
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Motion;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
#if USE_FAST_JOB
using Game.Systems.Reg;
#endif

namespace Game.Systems.Path {
    public sealed class PathSys : GameSystem, IOverlayProvider  {
        public const string SysId = "PathSys";
        public override string Id => SysId;
        private uint currentReqId;
        public List<OverlayInfo> Overlays { get; set; } = new();
        public RegSys Reg;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PathSys());
            #if !PRODUCTION_BUILD
            maxFrames = 0;
            #endif
        }
        private readonly Dictionary<uint, (PathReq, MotionControl)> pending = new();
        #if TRACE_REQUESTS
        private readonly Dictionary<uint, string> requestTraces = new();
        #endif
        private NativeList<PathReq> requests;
        private NativeHashMap<uint, PathRes> results;
        private NativeParallelMultiHashMap<uint, PathResStep> paths;
        public NativeArray<PathNode> Nodes;
        private NativeArray<short> coverMap;
        private NativeArray<short> noCoverMap;
        /// <summary>
        /// When a being stops at an empty tile, it will have to claim it. Nobody else should be able to stop / stand
        /// in that same position during combat.
        /// </summary>
        private NativeArray<int> claimedPosMap;
        // Army bits of the fighters standing on or stepping into each tile, written by
        // HostileBodies: a walker does not enter a tile that holds a body of an army hostile
        // to its own, and its path search avoids one
        private NativeArray<int> bodyMap;
        private readonly List<int> bodyTiles = new();
        // The being walking to stop at each tile (MotionControl.ReserveDestination). Spot
        // pickers leave a tile someone already walks to alone (RW:
        // PawnDestinationReservationManager): two units heading for one spot ended side by
        // side, the second stopped a tile short by the first one's claim.
        private int[] destOwner;

        #if VIZ_ASTAR
        public NativeHashMap<int, float> CrawledPositions;
        #endif

        //private bool needsRecalculate;
        public readonly Dictionary<int, int> ClaimedPosBeingToPosMap = new();

        public readonly Dictionary<int, int> TeleporterMap = new();
        public readonly HashSet<int> Ports = new();
        private readonly HashSet<TeleporterComp> teleporters = new();
        private readonly HashSet<TileTransformComp> tilesWithPorts = new();

        private readonly List<PathResStep> tempSteps = new();
        private readonly ResStepComparer resStepComparer = default;
        private bool hasRequests;
        private bool isJobRunning;
        private int jobStartFrame;
        private int gridSize;
        private int gridH;
        private bool isInitialized;
        // FIXME remove fucking teleporters
        public static bool IsFastPath;
        public static bool IsFastPathOrNoTeleporters;
        private JobHandle pathJob;
        public JobHandle PathJobHandle => pathJob;
        // Area sys will set this to true
        public bool HasUnreachableAreas;
        public uint RequestsExecuted;

        // Debug properties
        public int PendingRequestsCount => pending.Count;
        public bool IsJobRunning => isJobRunning;
        public bool HasRequests => hasRequests;
        public IEnumerable<PathReq> PendingRequests => pending.Values.Select(p => p.Item1);

        public override void Unload() {
            overlay?.ReleaseResources();
            TeleporterMap.Clear();
            teleporters.Clear();
            pending.Clear();
            ClaimedPosBeingToPosMap.Clear();
            EndJob();
            #if VIZ_ASTAR
            if (CrawledPositions.IsCreated) {
                CrawledPositions.Dispose();
            }
            #endif
            if (claimedPosMap.IsCreated) {
                claimedPosMap.Dispose();
            }
            if (bodyMap.IsCreated) {
                bodyMap.Dispose();
            }
            bodyTiles.Clear();
            destOwner = null;
            if (coverMap.IsCreated) {
                coverMap.Dispose();
            }
            if (noCoverMap.IsCreated) {
                noCoverMap.Dispose();
            }
            if (Nodes.IsCreated) {
                Nodes.Dispose();
            }
            if (requests.IsCreated) {
                requests.Dispose();
            }
            if (results.IsCreated) {
                results.Dispose();
            }
            if (paths.IsCreated) {
                paths.Dispose();
            }
        }

        public void EndJob() {
            if (isJobRunning) {
                pathJob.Complete();
            }
        }

        public bool IsWalkable(int pos) {
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsPassable && node.IsWalkable;
        }

        public bool IsPassable(int pos) {
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsPassable;
        }
        public bool CanBeCoverSlot(int pos) {
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsPassable && !node.IsTeleporter && !node.IsDoor;
        }

        protected override void OnInitialize() {
            Ready.SetPathfinding(false);
            currentReqId = 1;
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RecalculatePassability.AddListener(OnUpdatePassability);
            S.Sig.AddTeleporter.AddListener(OnAddTeleporter);
            S.Sig.PortsChanged.AddListener(OnPortChange);
            S.Sig.RemoveTeleporter.AddListener(OnRemoveTeleporter);
            S.Sig.WinchEnded.AddListener(OnWinchEnd);
        }

        private void OnPortChange(TileTransformComp transform, bool added) {
            if (added) {
                tilesWithPorts.Add(transform);
            } else {
                tilesWithPorts.Remove(transform);
            }
            RebuildPorts();
        }

        private void OnUpdatePassability(int posIdx) {
            passabilityUpdateQueue.Add(posIdx);
        }

        private readonly HashSet<int> passabilityUpdateQueue = new();

        private void OnWinchEnd(bool moved) {
            if (!moved) { return; }
            RebuildPorts();
            if (teleporters.Count > 0) {
                D.Warn("Rebuilding teleporter map after winch");
                RebuildTeleporterMap();
            }
        }

        private void OnAddTeleporter(TeleporterComp tp, int from, int to) {
            #if DEBUG_TELEPORTERS
            D.Warn("Add teleporter: {0}", Pos.StringFromTo(from, to));
            #endif
            if (!teleporters.Add(tp)) {
                D.Err("Trying to add same teleporter twice! {0} / {1}",
                    Pos.StringFromTo(from, to), tp);
                return;
            }
            if (tp.Tile.PosIdx != from) {
                D.Err("Teleporter position does not match from: {0}->{1}",
                    Pos.StringFromTo(from, to), tp);
            }
            TeleporterMap[from] = to;
            var fromNode = Nodes[from];
            fromNode.Type |= PathNodeType.Teleporter;
            fromNode.TeleportsTo = to;
            Nodes[from] = fromNode;
            S.Sys.Areas.DoorCheck.RegisterTP(from, to);
            overlay.NeedsVisualUpdate = true;
            S.Sig.RebuildRegs.Send();
            // FUCK teleporters
            IsFastPathOrNoTeleporters = IsFastPath || TeleporterMap.Count == 0;
        }

        private void OnRemoveTeleporter(TeleporterComp tp, int from) {
            #if DEBUG_TELEPORTERS
            D.Warn("Removing teleporter: {0}", Pos.String(from));
            #endif
            if (!teleporters.Remove(tp)) {
                D.Err("Failed to remove teleporter from: {0}", tp);
            }
            if (!TeleporterMap.Remove(from)) {
                D.Err("Failed to remove teleporter from: {0}", Pos.String(from));
            }
            var fromNode = Nodes[from];
            if (!fromNode.IsTeleporter) {
                D.Err("Trying to remove teleporter that wasn't! {0}",
                    Pos.String(from));
            }
            fromNode.Type &= ~PathNodeType.Teleporter;
            #if !PRODUCTION_BUILD
            D.Ass(!fromNode.IsTeleporter, "From node is teleporter!");
            #endif
            fromNode.TeleportsTo = -1;
            Nodes[from] = fromNode;
            S.Sys.Areas.DoorCheck.UnregisterTP(from);
            overlay.NeedsVisualUpdate = true;
            S.Sig.RebuildRegs.Send();
            // FUCK teleporters
            IsFastPathOrNoTeleporters = IsFastPath || TeleporterMap.Count == 0;
        }

        private PathOverlay overlay;

        private void OnLoadSave(GameState state) {
            overlay = new PathOverlay(this, state);
            overlay.PrepareCompute(Overlays);
            gridSize = state.GridSize;
            gridH = state.GridHeight;
            //state.Sig.OnLateUpdate.AddListener(OnLateUpdate);
            state.Sig.EveryFrame.AddListener(EveryFrame);
            state.Clock.OnTick.AddListener(OnTick);
            // FIXME remove the fucking teleporters
            IsFastPath = !S.Prefs.GetBool(Pref.PathOptimizeTeleporters, PrefH.PathOptimizeTeleporters, true);
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                IsFastPath = true;
            }
            #endif
            WarmupSystem();
        }
        private void Recalculate() {
            isInitialized = true;
            for (int i = 0; i < gridSize; i++) {
                UpdateNode(i);
            }
        }

        private void WarmupSystem() {
            Nodes = new NativeArray<PathNode>(gridSize, Allocator.Persistent);
            for (int i = 0; i < gridSize; i++) {
                Nodes[i] = PathNode.ForSpace(i);
            }
            requests = new NativeList<PathReq>(Allocator.Persistent);
            results = new NativeHashMap<uint, PathRes>(16, Allocator.Persistent);
            paths = new NativeParallelMultiHashMap<uint, PathResStep>(16, Allocator.Persistent);
            noCoverMap = new NativeArray<short>(1, Allocator.Persistent);
            claimedPosMap = new NativeArray<int>(gridSize, Allocator.Persistent);
            bodyMap = new NativeArray<int>(gridSize, Allocator.Persistent);
            destOwner = new int[gridSize];
            Recalculate();
            Ready.SetPathfinding(true);
        }

        public int BodyBitsAt(int posIdx) {
            return bodyMap[posIdx];
        }

        public void MarkBody(int posIdx, int bit) {
            ref var bits = ref bodyMap.GetUnsafeRef(posIdx);
            if (bits == 0) {
                bodyTiles.Add(posIdx);
            }
            bits |= bit;
        }

        public void ClearBodies() {
            for (int i = 0; i < bodyTiles.Count; i++) {
                bodyMap[bodyTiles[i]] = 0;
            }
            bodyTiles.Clear();
        }

        // The first walker to a tile keeps it; a later one still walks there, and the claim on
        // arrival decides
        public bool TryReserveDest(int posIdx, int beingId) {
            ref var owner = ref destOwner[posIdx];
            if (owner == 0) {
                owner = beingId;
                return true;
            }
            return owner == beingId;
        }

        public void ReleaseDest(int posIdx, int beingId) {
            if (destOwner != null && destOwner[posIdx] == beingId) {
                destOwner[posIdx] = 0;
            }
        }

        public bool IsDestTaken(int posIdx, int beingId) {
            var owner = destOwner[posIdx];
            return owner != 0 && owner != beingId;
        }

        // Another being stands there or walks there to stop
        public bool IsSpotTaken(int posIdx, int beingId) {
            return IsClaimed(posIdx, beingId) || IsDestTaken(posIdx, beingId);
        }

        // How many of the 8 tiles around a spot another being stands on or walks to (elbow
        // room for spot pickers, CastPosition.ElbowRoomMul)
        public int OthersAround(int posIdx, int beingId) {
            int n = 0;
            for (int i = 0; i < 8; i++) {
                var np = Pos.NeighborAt(posIdx, i);
                if (Pos.IsOOB(np)) { continue; }
                if (IsSpotTaken(np, beingId)) {
                    n++;
                }
            }
            return n;
        }

        public bool IsClaimed(int posIdx, int beingId) {
            ref var claimedBeingId = ref claimedPosMap.GetUnsafeRef(posIdx);
            return claimedBeingId != 0 && claimedBeingId != beingId;
        }

        /// <summary>
        /// Use this before moving into next position with intent to stop there.
        /// If it returns false, the position cannot be stopped at, so we have to
        /// end the move immediately and recalculate the path or just GTFO, or
        /// try to stay where we are now.
        /// </summary>
        /// <param name="beingId">being entity (instance) id of who will try to claim</param>
        /// <returns>claim succeeded</returns>
        public bool TryClaimPos(int posIdx, Being being) {
            if (!being.IsActive) {
                #if DEBUG_CLAIMS
                D.Err("Trying to claim position by inactive being {0}", being);
                #endif
                return false;
            }
            if (posIdx == 0) {
                D.Err("Trying to claim 0 pos by {0}", being);
            }
            if (Pos.IsOOB(posIdx)) {
                #if DEBUG_CLAIMS
                D.Err("Trying to claim invalid position {0} by {1}", posIdx, being);
                #endif
                return false;
            }
            // Everything fails if this is not allowed!
            // if (WorkSpots.Contains(posIdx)) {
            //     // #if DEBUG_CLAIMS
            //     // D.Err("Trying to claim work spot at {0} by {1}", Pos.String(posIdx), beingId);
            //     // #endif
            //     D.Err("Trying to claim work spot at {0} by {1} | {2}", Pos.String(posIdx), being, being.AIAgent.CurrentAction);
            //     return false;
            // }
            // if (TeleporterMap.ContainsKey(posIdx) || TeleporterMap.ContainsValue(posIdx)) {
            //     // #if DEBUG_CLAIMS
            //     // D.Err("Trying to claim a teleporter i/o at {0} by {1}", Pos.String(posIdx), beingId);
            //     // #endif
            //     D.Err("Trying to claim a teleporter i/o at {0} by {1} | {2}", Pos.String(posIdx), being, being.AIAgent.CurrentAction);
            //     return false;
            // }
            // if (IsDoor(posIdx)) {
            //     #if DEBUG_CLAIMS
            //     D.Err("Trying to door at {0} by {1}", Pos.String(posIdx), beingId);
            //     #endif
            //     return false;
            // }
            // A sandbag or a door is walked across, not lingered on. Paths end beside one
            // (FastPathfindJob) unless it is the only spot to work from, so a being with a task
            // may stop there; an idle one is turned away and steps aside (CheckClaimedPos).
            if (!MayStopAt(posIdx, being)) {
                return false;
            }
            var beingId = being.Id;
            if (ClaimedPosBeingToPosMap.TryGetValue(beingId, out var oldPos)) {
                if (oldPos > 0) {
                    // #if DEBUG_CLAIMS
                    D.Err("Being trying to claim new pos without releasing the old one! Use ReleaseClaimedPos() before moving out! Attempting to auto fix. New pos: {0}. Old pos: {1}. Being Id: {2}",
                        Pos.String(posIdx), Pos.String(oldPos), being);
                    // #endif
                    ReleaseClaimedPos(oldPos, beingId);
                }
            }
            ref var claimedBeingId = ref claimedPosMap.GetUnsafeRef(posIdx);
            if (claimedBeingId == 0) {
                claimedPosMap[posIdx] = beingId;
                ClaimedPosBeingToPosMap[beingId] = posIdx;
                overlay.NeedsVisualUpdate = true;
                return true;
            } else if (claimedBeingId == beingId) {
                #if DEBUG_CLAIMS
                D.Err("Trying to claim same position twice! {0} => {1}",
                    Pos.String(posIdx), being);
                #endif
                return true;
            }
            return false;
        }

        /// <summary>
        /// Use this when moving from the first position or getting incapacitated / dead / carried
        /// </summary>
        /// <param name="posIdx"></param>
        /// <param name="beingId"></param>
        public void ReleaseClaimedPos(int posIdx, int beingId) {
            if (beingId != 0) {
                ref var claimedBeingId = ref claimedPosMap.GetUnsafeRef(posIdx);
                if (claimedBeingId == 0) {
                    #if DEBUG_CLAIMS
                    D.Err("Trying to release a position that wasn't claimed! {0}. Releaser id: {1}",
                        Pos.String(posIdx), beingId);
                    #endif
                    return;
                }
                if (claimedBeingId != beingId) {
                    #if DEBUG_CLAIMS
                    D.Err("Trying to release a position that was not claimed by this being: {0} -> claimed by: {1}. Releasing by: {2}",
                        Pos.String(posIdx), claimedBeingId, beingId);
                    #endif
                    // Another being holds it now: keep their claim and only forget this
                    // being's stale one. Clearing it let a third being stop on the tile
                    // the holder was still standing on.
                    if (ClaimedPosBeingToPosMap.TryGetValue(beingId, out var stalePos)
                            && stalePos == posIdx) {
                        ClaimedPosBeingToPosMap.Remove(beingId);
                    }
                    return;
                }
            } else {
                foreach (var bp in ClaimedPosBeingToPosMap) {
                    if (bp.Value == posIdx) {
                        #if DEBUG_CLAIMS
                        D.Err("Found original being id for call to release pos with 0 being id: {0} -> {1}",
                            Pos.String(posIdx), bp.Key);
                        #endif
                        // the owner's entry is what gets cleared below, not key 0
                        beingId = bp.Key;
                        break;
                    }
                }
            }
            claimedPosMap[posIdx] = 0;
            // removed rather than zeroed, or the map grows by every being that ever moved
            ClaimedPosBeingToPosMap.Remove(beingId);
            overlay.NeedsVisualUpdate = true;
        }

        private void UpdateNode(int posIdx) {
            if (!isInitialized) { return; }
            PathNode.Update(ref Nodes, posIdx, TeleporterMap, Ports);
            overlay.NeedsVisualUpdate = true;
        }

        private double nextTeleporterMapValidationTime;

        private void UpdatePassability() {
            if (passabilityUpdateQueue.Count > 0) {
                if (isJobRunning) {
                    D.Warn("Forcing path job completion due to passability update");
                    pathJob.Complete();
                    AfterPathJob(false);
                }
                foreach (var posIdx in passabilityUpdateQueue) {
                    PathNode.Update(ref Nodes, posIdx, TeleporterMap, Ports);
                    Reg.UpdateRegDenyAt(posIdx);
                }
                overlay.NeedsVisualUpdate = true;
                passabilityUpdateQueue.Clear();
            }
        }

        private void OnTick(long ticks) {
            if (G.ClockSpeed > ClockSpeed.Fast1) {
                // when clock ticks super fast, we have multiple ticks per frame
                // so we want to handle it here
                RunAllPathfinding();
            }
        }

        private void EveryFrame() {
            if (isJobRunning || G.ClockSpeed < ClockSpeed.Fast2) {
                // If clock is unlimited, we run many ticks per frame anyway
                RunAllPathfinding();
            }
        }

        private void RunAllPathfinding() {
            UpdatePassability();
            RunPathfinding();
            overlay.TryUpdate();
            if (Ready.Gen && Ready.Game && Ready.Save
                    && GameTimer.realtimeSinceStartupAsDouble > nextTeleporterMapValidationTime) {
                if (G.ClockSpeed < ClockSpeed.Fast1) {
                    if (nextTeleporterMapValidationTime > 0) {
                        if (!ValidateTeleporterMap()) {
                            D.Err("Teleporter map validation failed, rebuilding map");
                            RebuildTeleporterMap();
                        }
                    }
                    nextTeleporterMapValidationTime = GameTimer.realtimeSinceStartupAsDouble + 5f;
                }
            }
            if (!isJobRunning) { return; }
            // 1. if job was running, read back and call the callbacks
            var jobFrames = GameTimer.frameCount - jobStartFrame;
            // we don't care if job runs longer than 4 frames, since it's not using TempJob allocators!
            // if (jobFrames > 3) {
            //     D.Warn("Forcing the end of {0} path requests ({1} frames) [{2}]",
            //         requests.Length, jobFrames,
            //             GameTimer.realtimeSinceStartup - jobStartTime);
            //     pathJob.Complete();
            //     AfterPathJob(false);
            // } else {
                if (pathJob.IsCompleted) {
                    pathJob.Complete();
                    AfterPathJob(false);
                #if PROFILE_PATH
                } else {
                    var frames = Time.frameCount - jobStartFrame;
                    D.Log("Still waiting for {0} path requests ({1} frames) [{2}]",
                        requests.Length, frames, Time.realtimeSinceStartupAsDouble - jobStartTime);
                #endif
                }
            // }
        }

        #if !PRODUCTION_BUILD
        private static int maxFrames;
        #else
        private int maxFrames;
        #endif

        #if !PRODUCTION_BUILD
        // path searches run and the cells they visited, for perf tests
        public static long DebugPathResults;
        public static long DebugPathIterations;
        #endif

        private void AfterPathJob(bool isImmediate) {
            isJobRunning = false;
            int totalIterations = 0;
            foreach (var res in results) {
                if (res.GetKeyValue(out var reqId, out var pathRes)) {
                    totalIterations += pathRes.Iterations;
                    #if !PRODUCTION_BUILD
                    DebugPathResults++;
                    DebugPathIterations += pathRes.Iterations;
                    #endif
                    #if DEBUG_PATH
                    D.Warn("Processing path result: {0} => {1}", reqId, pathRes);
                    #endif

                    if (pending.TryGetValue(reqId, out var reqAndControl)) {
                        SetPathResult(reqAndControl.Item1, ref pathRes, reqAndControl.Item2);
                    #if DEBUG_PATH
                    } else {
                        D.Warn("Request wasn't in pending. Cancelled? {0}", reqId);
                    #endif
                    }
                }
            }
            if (isImmediate && pending.Count > 0) {
                D.Err("Still have pending requests after immediate PF: {0}", pending.Count);
            }

            var frames = GameTimer.frameCount - jobStartFrame;
            #if PROFILE_PATH
            D.Log("Handled {0} path requests in {1} frames ({2:0.####}ms). Total iterations: {3}. Immediate: {4}",
                results.Count, frames, (Time.realtimeSinceStartupAsDouble - jobStartTime) * 1000d,
                    totalIterations, isImmediate);
            #elif !PRODUCTION_BUILD
            if (frames > maxFrames) {
                maxFrames = frames;
                if (frames > 2) {
                    D.Warn("Handled {0} path requests in {1} frames ({2:0.####}ms). Total iterations: {3}. Immediate: {4}. Speed: {5}. Beings: {6}",
                        results.Count, frames, (Time.realtimeSinceStartupAsDouble - jobStartTime) * 1000d,
                            totalIterations, isImmediate, G.ClockSpeed, S.Beings.AliveCount);
                }
            }
            #endif
            requests.Clear();
            results.Clear();
            paths.Clear();
            if (coverMap.IsCreated) {
                coverMap.Dispose();
            }
            hasRequests = false;
            S.Clock.Unlock(pathLockKey);
        }

        private unsafe void SetPathResult(PathReq req, ref PathRes pathRes,
                MotionControl control) {
            // Execute the callback to the request owner
            // Callback is the 2nd item in the tuple
            pending.Remove(req.Id);
            bool isEstimate = req.HasFlag(PathReqFlags.Estimate);
            if (!isEstimate && control.ExpectedReqId != pathRes.ReqId) {
                #if DEBUG_PATH || TRACE_REQUESTS
                LogStalePathResult(req, pathRes, control);
                #endif
                return;
            }
            if (!pathRes.IsValid || pathRes.Length == 0) {
                control.SetPathRes(req, pathRes);
                return;
            }
            var pathValues = paths.GetValuesForKey(req.Id);
            #if !PRODUCTION_BUILD
            if (tempSteps.Count > 0) {
                D.Err("TempSteps should be clear at this point! {0}",
                    D.Dump(tempSteps));
                tempSteps.Clear();
            }
            #endif
            foreach (var pv in pathValues) {
                tempSteps.Add(pv);
            }
            tempSteps.Sort(resStepComparer);
            List<int> nodes;
            if (isEstimate) {
                #if VIZ_ASTAR
                overlay.NeedsVisualUpdate = true;
                #endif
                nodes = control.PathEstimate;
            } else {
                nodes = control.Path;
            }
            nodes.Clear();
            for (int i = 0; i < pathRes.Length; i++) {
                nodes.Add(tempSteps[i].PosIdx);
            }
            tempSteps.Clear();
            control.SetPathRes(req, pathRes);
            #if TRACE_REQUESTS
            requestTraces.Remove(req.Id);
            #endif
        }

        private unsafe void LogStalePathResult(PathReq req, PathRes pathRes, MotionControl control) {
            #if !UNITY_EDITOR
            if (control.ExpectedReqId == 0) {
                return;
            }
            #endif
            string msg = string.Format("Discarding stale path result. Expected req id: {0}. Got: {1}. Who: {2}. Goal: {3}. Action: {4}",
                    control.ExpectedReqId, pathRes.ReqId, control.Being, control.Being.AIAgent.Goal, control.Being.AIAgent.CurrentAction);
            #if !UNITY_EDITOR
            D.Warn(msg);
            return;
            #endif
            if (control.ExpectedReqId == 0) {
                D.Warn(msg);
            } else {
                D.ErrSoft(msg);
            }
            #if TRACE_REQUESTS
            if (requestTraces.TryGetValue(req.Id, out var trace)) {
                D.Warn("Req id {0} trace: {1}", req.Id, trace);
            }
            #endif
        }

        private void RunPathfinding() {
            if (!hasRequests || isJobRunning) { return; }
            if (!S.Clock.TryLock(pathLockKey, "Path Job")) {
                D.Err("Failed locking path job!");
                return;
            }
            var reql = requests.Length;
            D.Ass(reql == 0, "Requests not empty!");
            //D.Ass(results.Count == 0, "Results not empty!");
            bool needsCoverMap = false;
            foreach (var req in pending) {
                var r = req.Value.Item1;
                requests.Add(r);
                if (!needsCoverMap && (r.Flags & PathReqFlags.CombatCover) == PathReqFlags.CombatCover) {
                    needsCoverMap = true;
                }
            }
            // D.Warn("Pending / processed requests: {0}/{1}",
            //     pending.Count, processedRequests.Count);
            isJobRunning = true;

            if (needsCoverMap) {
                // can't use TempJob allocator here
                coverMap = new NativeArray<short>(S.Query.GetCoverTotalMap.Ask(),
                    Allocator.Persistent);
            }

            #if VIZ_ASTAR
            if (CrawledPositions.IsCreated) {
                CrawledPositions.Dispose();
            }
            CrawledPositions = new NativeHashMap<int, float>(16, Allocator.Persistent);
            #endif

            // D.Warn("Fast path: {0}", IsFastPathOrNoTeleporters);
            #if USE_FAST_JOB
            var regSys = S.Sys.Get<RegSys>(RegSys.SysId);
            var job = new FastPathfindJob {
                GridH = gridH,
                GridSize = gridSize,
                Nodes = Nodes,
                FastPathHeuristics = IsFastPathOrNoTeleporters,
                CoverMap = needsCoverMap ? coverMap : noCoverMap,
                ClaimedMap = claimedPosMap,
                Requests = requests,
                OutResults = results,
                OutPaths = paths,
                Temp = S.Map.Temp.Data,
                O2 = S.Map.O2.Data,
                BodyMap = bodyMap,
                PosRegIds = regSys.PosRegIds,
                RegTypes = regSys.RegTypes,
                RegEdgeStarts = regSys.RegEdgeStarts,
                RegEdges = regSys.RegEdges,
                RegPosStarts = regSys.RegPosStarts,
                RegPos = regSys.RegPos,
                RegDeny = regSys.RegDeny,
                RegHasPassableNeighbor = regSys.RegHasPassableNeighbor,
                NumRegions = regSys.NumRegions,
                #if VIZ_ASTAR
                CrawledPositions = CrawledPositions,
                #endif
            };
            #else
            var job = new PathfindJob {
                FastPathHeuristics = IsFastPathOrNoTeleporters,
                GridH = gridH,
                GridSize = gridSize,
                Nodes = Nodes,
                CoverMap = needsCoverMap ? coverMap : noCoverMap,
                ClaimedMap = claimedPosMap,
                Requests = requests,
                OutResults = results,
                OutPaths = paths,
                Temp = S.Map.Temp.Data,
                O2 = S.Map.O2.Data,
                #if VIZ_ASTAR
                CrawledPositions = CrawledPositions,
                #endif
            };
            #endif

            jobStartTime = GameTimer.realtimeSinceStartupAsDouble;
            // During cooldown we can afford frame drops
            var isImmediate = G.ClockSpeed > ClockSpeed.Fast2;
            #if DEBUG_PATH
            D.Warn("Running pathfinding for {0} requests. Immediate: {1}",
                requests.Length, isImmediate);
            #endif
            // #if !PRODUCTION_BUILD
            // if (!isImmediate && The.TestsAreRunning) {
            //     isImmediate = Rng.UChance(0.5f);
            // }
            // #endif
            #if IMMEDIATE_PATH
            isImmediate = true;
            #endif

            jobStartFrame = GameTimer.frameCount;
            if (isImmediate) {
                #if JOB_DIRECT
                for (int i = 0; i < requests.Length; i++) {
                    job.Execute(i);
                }
                #else
                job.Run(requests.Length);
                #endif
                AfterPathJob(isImmediate);
            } else {
                pathJob = job.Schedule(requests.Length, default);
            }
        }

        private double jobStartTime;
        public bool AreIslandsLinked(int isle1, int isle2) {
            // either one is in open space
            if (isle1 == 0 || isle2 == 0) { return false; }
            if (isle1 == isle2) { return true; }
            var tc = TeleporterMap.Count;
            if (tc == 0) { return false; }
            // Assumption / optimization
            if (tc > 30) { return true; }
            var areas = S.Sys.Areas;

            foreach (var tp in TeleporterMap) {
                if (areas.IslandAt(tp.Key) == isle1) {
                    var output = tp.Value;
                    if (areas.IslandAt(output) == isle2) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool ValidateTeleporterMap() {
            if (teleporters.Count == 0 || TeleporterMap.Count == 0) {
                return true;
            }
            if (teleporters.Count != TeleporterMap.Count) {
                D.Err("We have a mismatch between teleporters and map! {0} != {1}",
                    teleporters.Count, TeleporterMap.Count);
            }
            foreach (var teleporter in teleporters) {
                if (!teleporter.IsTeleporterActive) {
                    // we can have outbound inactive due to switched off, but link from
                    // this teleporter
                    if (teleporter.Outbound == null || teleporter.Outbound.IsTeleporterActive) {
                        D.Err("Teleporter is linked but can't teleport! {0}", teleporter);
                        return false;
                    }
                }
                if (teleporter.Outbound == null) {
                    D.Err("Teleporter has no outbound teleporter but is in the path sys! {0}", teleporter);
                    return false;
                }
                if (TeleporterMap.TryGetValue(teleporter.Tile.PosIdx, out var targetPos)) {
                    var obp = teleporter.Outbound.Tile.PosIdx;
                    if (obp != targetPos) {
                        D.Err("Teleporter outbound leads to a different location that in the map! {0}, expected {1} actual {2}",
                            teleporter, Pos.String(obp), Pos.String(targetPos));
                        return false;
                    }
                } else {
                    D.Err("Teleporter can teleport but not in the map! {0}", teleporter);
                    return false;
                }
            }
            return true;
        }

        private void RebuildPorts() {
            foreach (var w in Ports) {
                var n = Nodes[w];
                n.Type &= ~PathNodeType.Port;
                Nodes[w] = n;
            }
            Ports.Clear();
            foreach (var tile in tilesWithPorts) {
                if (tile.HasWorkSpot) {
                    AddPort(tile.WorkSpot);
                }
                if (tile.HasInPort) {
                    AddPort(tile.InPort);
                }
                if (tile.HasOutPort) {
                    AddPort(tile.OutPort);
                }
            }
        }

        private void AddPort(int pos) {
            if (Ports.Add(pos)) {
                var n = Nodes[pos];
                n.Type |= PathNodeType.Port;
                Nodes[pos] = n;
            }
        }

        private void RebuildTeleporterMap() {
            // we modify teleports in this foreach loop, so we can't iterate it directly
            var tps = teleporters.ToArray();
            foreach (var tp in tps) {
                tp.RemoveFromPath();
                if (tp.IsTeleporterActive) {
                    tp.AddToPath();
                }
            }
            IsFastPathOrNoTeleporters = IsFastPath || TeleporterMap.Count == 0;
        }

        private static readonly int pathLockKey = Hashes.S("PathJob");

        // List<int> path will be filled out.
        // Input path needs to be clean
        // ONLY use this for preview, nothing else
        public PathRes ExecuteReq(ref PathReq req, List<int> path) {
            if (path.Count > 0) {
                #if !JOB_DIRECT && !IMMEDIATE_PATH
                D.Err("Path must be clear before passing it to ExecuteReq! {0} | {1} | {2}",
                    req, S.Beings.Find(req.BeingId), EntityUtils.DumpPath(path));
                #endif
                path.Clear();
            }
            if (Pos.IsOOB(req.PosTo) || Pos.IsOOB(req.PosFrom)) {
                D.Err("Trying to execute path from or to OOB: {0}", req);
                return default;
            }

            if (IsRestrictedByUnreachableAreas(req)) {
                return default;
            }

            var requests = new NativeList<PathReq>(1, Allocator.TempJob);
            var results = new NativeHashMap<uint, PathRes>(1, Allocator.TempJob);
            var paths = new NativeParallelMultiHashMap<uint, PathResStep>(1, Allocator.TempJob);
            NativeArray<short> coverMap;
            if ((req.Flags & PathReqFlags.CombatCover) == PathReqFlags.CombatCover) {
                coverMap = new NativeArray<short>(S.Query.GetCoverTotalMap.Ask(), Allocator.TempJob);
            } else {
                coverMap = new NativeArray<short>(1, Allocator.TempJob);
            }
            #if VIZ_ASTAR
            if (CrawledPositions.IsCreated) {
                CrawledPositions.Dispose();
            }
            CrawledPositions = new NativeHashMap<int, float>(16, Allocator.Persistent);
            #endif
            requests.Add(req);
            // D.Warn("Fast path direct: {0}", IsFastPathOrNoTeleporters);
            #if USE_FAST_JOB
            var regSys = S.Sys.Get<RegSys>(RegSys.SysId);
            var job = new FastPathfindJob {
                FastPathHeuristics = IsFastPathOrNoTeleporters,
                GridH = gridH,
                GridSize = gridSize,
                Nodes = Nodes,
                CoverMap = coverMap,
                ClaimedMap = claimedPosMap,
                Requests = requests,
                OutResults = results,
                OutPaths = paths,
                Temp = S.Map.Temp.Data,
                O2 = S.Map.O2.Data,
                BodyMap = bodyMap,
                PosRegIds = regSys.PosRegIds,
                RegTypes = regSys.RegTypes,
                RegEdgeStarts = regSys.RegEdgeStarts,
                RegEdges = regSys.RegEdges,
                RegPosStarts = regSys.RegPosStarts,
                RegPos = regSys.RegPos,
                RegDeny = regSys.RegDeny,
                RegHasPassableNeighbor = regSys.RegHasPassableNeighbor,
                NumRegions = regSys.NumRegions,
                #if VIZ_ASTAR
                CrawledPositions = CrawledPositions,
                #endif
            };
            #else
            var job = new PathfindJob {
                FastPathHeuristics = IsFastPathOrNoTeleporters,
                GridH = gridH,
                GridSize = gridSize,
                Nodes = Nodes,
                CoverMap = coverMap,
                ClaimedMap = claimedPosMap,
                Requests = requests,
                OutResults = results,
                OutPaths = paths,
                Temp = S.Map.Temp.Data,
                O2 = S.Map.O2.Data,
                #if VIZ_ASTAR
                CrawledPositions = CrawledPositions,
                #endif
            };
            #endif
            #if JOB_DIRECT
            job.Execute(0);
            #else
            job.Run(1);
            #endif
            RequestsExecuted++;
            requests.Dispose();
            coverMap.Dispose();
            #if VIZ_ASTAR
            if ((req.Flags & PathReqFlags.Estimate) == PathReqFlags.Estimate) {
                overlay.NeedsVisualUpdate = true;
            }
            #endif
            if (results.TryGetValue(req.Id, out var res)) {
                if (!res.IsValid) {
                    results.Dispose();
                    paths.Dispose();
                    return res;
                }
            } else {
                D.Err("Path job failed?");
                results.Dispose();
                paths.Dispose();
                return default;
            }
            results.Dispose();
            #if DEBUG_PATH
            D.Warn("Got path: {0} -> {1}", req, res);
            #endif
            #if !PRODUCTION_BUILD
            if (tempSteps.Count > 0) {
                D.Err("TempSteps should be clear at this point! {0}",
                    D.Dump(tempSteps));
                tempSteps.Clear();
            }
            #endif
            var pathValues = paths.GetValuesForKey(req.Id);
            foreach (var pv in pathValues) {
                tempSteps.Add(pv);
            }
            tempSteps.Sort(resStepComparer);
            paths.Dispose();
            for (int i = 0; i < res.Length; i++) {
                path.Add(tempSteps[i].PosIdx);
            }
            tempSteps.Clear();
            return res;
        }
        public bool IsRoamable(int pos, bool grounded) {
            if (Pos.IsOOBNonZero(pos)) { return false; }
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsRoamable(grounded);
        }
        public bool IsNavigable(int pos, bool grounded) {
            if (Pos.IsOOB(pos)) { return false; }
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsPassable && (!grounded || node.IsWalkable);
        }
        public bool FlagsMatch(int pos, PathNodeType flags) {
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return (node.Type & flags) == flags;
        }

        public bool IsTeleporter(int pos, out int posTo) {
            if (!TeleporterMap.TryGetValue(pos, out posTo)) {
                posTo = Pos.Invalid;
                return false;
            }
            return true;
        }

        public bool IsNavigable(int pos, bool grounded, float maxDifficulty) {
            if (Pos.IsOOB(pos)) { return false; }
            ref var node = ref Nodes.GetUnsafeRef(pos);
            return node.IsPassable && (!grounded || node.IsWalkable)
                && node.Difficulty < maxDifficulty;
        }
        public bool IsNavigable(int pos, bool grounded, float maxDifficulty, out int claimedById) {
            if (Pos.IsOOB(pos)) {
                claimedById = 0;
                return false;
            }
            ref var node = ref Nodes.GetUnsafeRef(pos);
            claimedById = claimedPosMap[pos];
            return node.IsPassable && (!grounded || node.IsWalkable)
                && node.Difficulty < maxDifficulty;
        }

        private bool IsRestrictedByUnreachableAreas(PathReq req) {
            if (!HasUnreachableAreas) { return false; }
            var a = S.Sys.Areas;
            var a1 = a.At(req.PosTo);
            var a2 = a.At(req.PosFrom);
            // within the same area
            if (a1 == a2) { return false; }
            if (a1 == -1 || a2 == -1) {
                // we are either inside a wall or trying to reach a wall
                // we don't want to check reachability
                return false;
            }
            // if neither is unreachable or both are unreachable, we are not restricted
            var isToReachable = a.IsAreaReachable(a1);
            var isFromReachable = a.IsAreaReachable(a2);
            if (isToReachable != isFromReachable) {
                if (req.HasFlag(PathReqFlags.AdjNearby) && !req.HasFlag(PathReqFlags.NonDiagonal)) {
                    // check if there is an adjacent diagonal access to the pos
                    var p = req.PosTo;
                    var diagA = a.At(Pos.SW(p));
                    if (diagA != a1 && a.IsAreaReachable(diagA) == isFromReachable) {
                        return false;
                    }
                    diagA = a.At(Pos.SE(p));
                    if (diagA != a1 && a.IsAreaReachable(diagA) == isFromReachable) {
                        return false;
                    }
                    diagA = a.At(Pos.NE(p));
                    if (diagA != a1 && a.IsAreaReachable(diagA) == isFromReachable) {
                        return false;
                    }
                    diagA = a.At(Pos.NW(p));
                    if (diagA != a1 && a.IsAreaReachable(diagA) == isFromReachable) {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        public uint AddReq(ref PathReq req, MotionControl control) {
            var id = currentReqId++;
            req.Id = id;
            if (Pos.IsOOB(req.PosTo) || Pos.IsOOB(req.PosFrom)) {
                D.Err("Trying to add path req from or to OOB: {0}", req);
                control.Progress.FailReason = T.NavFailTargetUnavailable;
                control.Progress.Flags |= MotionProgressFlags.Failed;
                control.Progress.Flags &= ~MotionProgressFlags.Moving;
                return 0;
            }

            if (IsRestrictedByUnreachableAreas(req)) {
                control.Progress.FailReason = T.AdRejUnreachable;
                control.Progress.Flags |= MotionProgressFlags.Failed;
                control.Progress.Flags &= ~MotionProgressFlags.Moving;
                return 0;
            }
            #if TRACE_REQUESTS
            requestTraces[id] = Environment.StackTrace;
            #endif

            #if !PRODUCTION_BUILD
            if (req.GoalWidth > 0 || req.GoalHeight > 0) {
                if (req.GoalWidth == 0 || req.GoalHeight == 0) {
                    D.Err("Multi request has goal width or height as 0! {0}", req);
                }
            }
            if (((req.Flags & PathReqFlags.UnsafeOK) == PathReqFlags.None)
                    && req.MinTemp == req.MaxTemp) {
                D.Err("Safe request has min/max temp equal! {0}", req);
            }
            #endif
            #if DEBUG_PATH
            D.Warn("Adding req: {0}", req);
            #endif

            var immediate = (req.Flags & PathReqFlags.Instant) == PathReqFlags.Instant;
            #if IMMEDIATE_PATH
            immediate = true;
            #endif

            if (immediate) {
                List<int> path;
                if (req.HasFlag(PathReqFlags.Estimate)) {
                    path = control.PathEstimate;
                } else {
                    path = control.Path;
                }
                var res = ExecuteReq(ref req, path);
                control.SetPathRes(req, res);
                if (!res.IsValid) {
                    return 0;
                }
            } else {
                pending.Add(id, (req, control));
                hasRequests = true;
            }

            return id;
        }

        public bool IsGoodCoverAt(int pos, int beingId) {
            if (IsSpotTaken(pos, beingId)) {
                return false;
            }
            ref var n = ref Nodes.GetUnsafeRef(pos);
            return !n.IsPassThroughOnly && !n.IsTeleporter;
        }

        // Walked across, not stopped on: a sandbag, a door (PassabilityComp.IsPassThroughOnly)
        public bool IsPassThroughOnly(int pos) {
            ref var n = ref Nodes.GetUnsafeRef(pos);
            return n.IsPassThroughOnly;
        }

        // Only a being at a task stops on a pass-through tile: one whose only spot to work from
        // is a doorway. Nobody idles there.
        public bool MayStopAt(int pos, Being being) {
            return !IsPassThroughOnly(pos) || being.AIAgent?.Goal != null;
        }

        public bool IsDoorOrTeleporter(int pos) {
            ref var n = ref Nodes.GetUnsafeRef(pos);
            return n.IsDoor || n.IsTeleporter;
        }

        public bool IsDoor(int pos) {
            ref var n = ref Nodes.GetUnsafeRef(pos);
            return n.IsDoor;
        }
        public bool CanIdleAt(int pos) {
            ref var n = ref Nodes.GetUnsafeRef(pos);
            if (n.IsPassThroughOnly || n.IsTeleporter || !n.IsPassable || n.Difficulty > 0f) {
                return false;
            }
            if (S.Beings.ByPos.TryGetValue(pos, out var beings)) {
                if (beings.Count > 1) {
                    return false;
                }
            }
            return true;
        }

        public string DescriptionOf(int pos) {
            if (!EntityUtils.IsWithinBounds(pos)) { return null; }
            var pn = Nodes[pos];
            // #if PRODUCTION_BUILD
            var posStr = Texts.Accented2(Pos.String(pos));
            // #else
            // var posStr = Texts.Accented2(Pos.StringD(pos));
            // #endif
            string pd;
            if (!pn.IsPassable) {
                pd = T.PathAtImpassable_V1.F(posStr);
                // if (pn.Crowd != 0) {
                //     pd = $"{pd} [Crowd: {pn.Crowd}]";
                // }
                return pd;
            } else if (pn.IsWalkable) {
                pd = T.PathWalkable;
            } else {
                pd = T.PathFlyable;
            }
            if (pn.IsTeleporter) {
                pd = $"{pd} ({T.ObjectsDevicesTeleporter} -> {Pos.String(pn.TeleportsTo)})";
            }
            // if (pn.Crowd != 0) {
            //     pd = $"{pd} [Crowd: {pn.Crowd}]";
            // }
            var desc = T.PathAt_V3.F(posStr, pd,
                Units.Percentage(pn.Difficulty));
            if (pn.Deny != NavPermission.None) {
                desc = $"{desc}\n * {PathNode.DenyStr(pn.Deny)}";
            }
            return desc;
        }
        public bool HasManyTeleporters => TeleporterMap.Count > 30;
        public int TeleporterCount => TeleporterMap.Count;

        private float diffCostMul = 0f;
        // https://www.desmos.com/calculator/cqbpdmjakc
        public float TeleporterEnergyCost() {
            if (diffCostMul == 0f) {
                diffCostMul = TeleporterDifficultyCostMul(S.Difficulty.Level);
            }
            return TeleporterEnergyCost(TeleporterMap.Count) * diffCostMul;
        }

        // Before the difficulty multiplier
        public static float TeleporterEnergyCost(int teleporters) {
            var x = teleporters;
            return 0.75f * x * x - 1.45f * x + 0.75f;
        }

        public static float TeleporterDifficultyCostMul(float difficultyLevel) {
            return difficultyLevel * 0.5f;
        }
    }
}
