#if UNITY_EDITOR
// #define DEBUG_GRID
// #define DEBUG_GRID_JOB
// #define PROFILE_DELEGATES
// #define DEBUG_AFTER_TICK
#endif
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Story.Events;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Game.Systems.UIUX;
using Game.Systems.Notify;

namespace Game.Systems.Electricity {
    public class ElectricitySys : GameSystem, IOverlayProvider,
            ISaveableSpecial {
        public const string SysId = "ElectricitySys";
        public override string Id => SysId;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new ElectricitySys());
        }
        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;
        public ElectricityUI UI;
        public List<EnergyStateSnapshot> History => data.StateHistory;

        public int TotalMem;
        public int TotalCPU;
        public readonly BInt TotalDisk = new();
        public int TotalGPU;
        private bool isLockCreated;

        public delegate void AfterTickGrid();
        private readonly List<AfterTickGrid> afterTickDelegates = new();
        // For tests
        public List<AfterTickGrid> AfterTickDelegates => afterTickDelegates;
        private readonly Dictionary<IComponent, AfterTickGrid> afterTickOwners = new();
        public void AddAfterTick(IComponent who, AfterTickGrid hook) {
            if (!who.Entity.IsActive) {
                D.Err("Trying to add an inactive entity to AfterTickGrid: {0}", who);
                return;
            }
            if (afterTickOwners.TryAdd(who, hook)) {
                afterTickDelegates.Add(hook);
            } else {
                D.Err("Trying to add a second el after tick hook for component: {0} ({1})",
                    who, hook);
            }
        }
        public void RemoveAfterTick(IComponent who) {
            if (afterTickOwners.TryGetValue(who, out var hook)) {
                if (!afterTickDelegates.Remove(hook)) {
                    D.Err("Failed removing el after tick hook for {0} -> {1}",
                        who, hook);
                }
                #if DEBUG_GRID
                D.Warn("Removing after tick from {0} ({1})", who, who.Entity);
                #endif
                afterTickOwners.Remove(who);
                delegateErrors.Forget(hook);
            } else {
                D.Err("Failed removing el after tick owner for hook {0} -> {1}",
                    who, hook);
            }
        }
        public void RemoveAfterTick(IComponent who, AfterTickGrid hook) {
            if (!afterTickDelegates.Remove(hook)) {
                D.Err("Failed removing el after tick hook for {0} -> {1}",
                    who, hook);
            }
            if (!afterTickOwners.Remove(who)) {
                D.Err("Failed removing el after tick owner for hook {0} -> {1}",
                    who, hook);
            }
            delegateErrors.Forget(hook);
        }

        public Entity Entity => null;

        // Managed in real time by devices, always up to date
        private ReusableNativeArray<ElectricNode> arrNodes;
        public ReusableNativeArray<ElectricNode> ArrNodes => arrNodes;
        // Managed in real time by player and devices, always up to date
        private ReusableNativeArray<ElectricConnection> arrConnections;
        public ReusableNativeArray<ElectricConnection> ArrConnections => arrConnections;
        // Recalculated at runtime after connections change
        private NativeList<ElectricGrid> grids;
        public int GridsCount;
        // These are entity Ids, not index ids
        private readonly HashSet<(int, int)> existingConnections = new();
        // int is idx
        private readonly Dictionary<int, ElectricNodeComp> nodeComponents = new();
        public Dictionary<int, ElectricNodeComp> NodeComponents => nodeComponents;

        public override void Unload() {
            arrNodes?.Unload();
            arrConnections?.Unload();
            if (grids.IsCreated) {
                grids.Dispose();
            }
        }

        public int DisconnectAll(ElectricNodeComp comp) {
            if (comp == null) {
                D.Warn("Trying to disconnect all from a null ElectricNodeComp");
                return 0;
            }
            if (!comp.Node.IsValid) {
                D.Warn("Trying to DisconnectAll from an invalid node! {0} | {1}", comp.Entity, comp.Node);
                return 0;
            }
            CatchUp(comp);
            var node = comp.Node;
            node.Consumed = 0f;
            node.Produced = 0f;
            node.Deficit = 0f;
            if (node.NumConnections == 0) { return 0; }
            #if DEBUG_GRID
            D.Warn("Disconnecting {1} from node: {0}", node, node.NumConnections);
            #endif
            int disconnected = 0;
            int expected = node.NumConnections;
            for (int i = 0; i < arrConnections.Count; i++) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                if (conn.Connection.Item1 == node.EntityId
                        || conn.Connection.Item2 == node.EntityId) {
                    if (!existingConnections.Remove(conn.Connection)) {
                        D.Err("Failed to remove a conn from existing connections! {0}",
                            conn);
                    }
                    arrConnections.Remove(i);
                    node.NumConnections--;
                    if (node.NumConnections < 0) {
                        D.Err("Node num connections went below zero! {0}.", node);
                        node.NumConnections = 0;
                    }
                    var otherNode = conn.NodeIdxs.Item1 == node.Idx ?
                        arrNodes.Array[conn.NodeIdxs.Item2] : arrNodes.Array[conn.NodeIdxs.Item1];
                    if (otherNode.IsInitialized && otherNode.IsValid) {
                        otherNode.NumConnections--;
                        if (otherNode.NumConnections < 0) {
                            D.ErrSoft("Other node num connections went below zero! This is fine if other node was destroyed. {0} -> {1}",
                                comp.Entity, otherNode);
                            otherNode.NumConnections = 0;
                        }
                        if (otherNode.NumConnections == 0) {
                            otherNode.Consumed = 0f;
                            otherNode.Produced = 0f;
                            otherNode.Deficit = 0f;
                        }
                        otherNode.NeedsSync = true;
                        arrNodes.Update(otherNode.Idx, otherNode);
                    }
                    disconnected++;
                }
            }
            if (expected != disconnected) {
                // happens during asteroid smashes
                D.ErrSoft("Failed to disconnect all node connections! Expected {0}, disconnected: {1}. {2}. {3}",
                    expected, disconnected, comp.Tile, node);
            }
            if (disconnected > 0) {
                needsGridRebuild = true;
                node.NeedsSync = true;
                arrNodes.Update(node.Idx, node);
            }
            return disconnected;
        }

        // A component's Node is a copy that catches up with arrNodes only in SyncNodes. Changing
        // connections through that copy without catching it up first writes back a count that
        // misses every change made since, which left connectors with phantom slots (STA-1617)
        private void CatchUp(ElectricNodeComp comp) {
            var idx = comp.Node.Idx;
            if (!arrNodes.TryGet(idx, out var trueNode) || !trueNode.NeedsSync) { return; }
            #if DEBUG_GRID
            D.Warn("Catching up node before changing connections {0}", trueNode);
            #endif
            trueNode.NeedsSync = false;
            arrNodes.Update(idx, trueNode);
            comp.Update(trueNode);
        }

        private readonly List<(ElectricNodeComp, ElectricNodeComp)> autoConnectLinks = new();
        private bool isBulkConnecting;

        // Links a node the way a player would by hand (ElectricityGrid.md, "Auto-connect"). A
        // device without links gets the nearest reachable connector with a free slot; a device
        // on a grid that does not reach a Ship Computer gets that grid uplinked, or a link of
        // its own when no connector of the grid can reach. A connector uplinks its grid if
        // needed, then connects what is broken around it. onlyReachable false lets a node link
        // to any connector. True when the node ends up linked, and on a grid with a Ship
        // Computer unless onlyReachable is false
        public bool TryAutoConnect(ElectricNodeComp nodeComp, bool onlyReachable) {
            if (nodeComp == null) { return false; }
            if (!nodeComp.Entity.IsActive) { return false; }
            if (!nodeComp.IsRegistered) { return false; }
            CatchUp(nodeComp);
            var node = nodeComp.Node;
            if (!node.IsInitialized) { return false; }
            if (!node.IsValid) {
                D.Err("Registed Node is invalid in TryAutoConnect! {0}", node);
                return false;
            }
            RebuildGrid(false);
            autoConnectLinks.Clear();
            AutoConnectPlanner.PlanFor(this, nodeComp, onlyReachable, autoConnectLinks);
            ApplyAutoConnect(autoConnectLinks);
            return nodeComp.Node.NumConnections > 0 && (!onlyReachable || nodeComp.IsReachable);
        }

        // Explains why TryAutoConnect returned false, for player facing error messages
        public string GetAutoConnectFailReason(ElectricNodeComp nodeComp) {
            if (nodeComp == null || !nodeComp.Entity.IsActive || !nodeComp.IsRegistered) {
                return T.AdRejTargetUnavailable;
            }
            // Heavily damaged or unbuilt: say that, not that nothing is in reach
            if (!nodeComp.IsConnectableNow(out var notConnectable)) {
                return notConnectable;
            }
            RebuildGrid(false);
            autoConnectLinks.Clear();
            var failure = AutoConnectPlanner.PlanFor(this, nodeComp, true, autoConnectLinks);
            autoConnectLinks.Clear();
            switch (failure) {
                case AutoConnectPlanner.Failure.Unavailable:
                    return T.AdRejTargetUnavailable;
                case AutoConnectPlanner.Failure.OutOfRange:
                    return T.ElConnectorOutOfRange;
                case AutoConnectPlanner.Failure.ConnectorsFull:
                    return T.ElConnectorFullyUtilized;
                case AutoConnectPlanner.Failure.OnlyUnreachable:
                    return The.Toolbox.ConnectTool.UnreachableInfo();
                case AutoConnectPlanner.Failure.ShipComputerCap:
                    return T.CpuCapWouldBeReached_V1.F(ShipComputerCap);
                default:
                    return T.OperationFailed;
            }
        }

        // The Electricity panel's Try Auto Connect and ship generation. One frame for the whole
        // grid, so nothing has to pause the game or block saving while it runs
        public void TryAutoConnectAll() {
            RebuildGrid(false);
            autoConnectLinks.Clear();
            using (D.Timer("TryAutoConnectAll", 0.05f)) {
                AutoConnectPlanner.PlanAll(this, autoConnectLinks);
                ApplyAutoConnect(autoConnectLinks);
            }
        }

        // Through the components' TryConnect: it keeps the connection invariants and tells
        // both sides when their connected state flips - a Ship Computer that never hears it got
        // linked keeps its "Not Connected" alert up (STA-1647). The overlay is redrawn once at
        // the end, not once per link
        private void ApplyAutoConnect(List<(ElectricNodeComp, ElectricNodeComp)> links) {
            if (links.Count == 0) { return; }
            isBulkConnecting = true;
            try {
                for (int i = 0; i < links.Count; i++) {
                    var (a, b) = links[i];
                    if (!TryConnect(a, b, out var failReason)) {
                        D.Err("Auto connect planned a link it could not make: {0} -> {1}: {2}",
                            a.Tile, b.Tile, failReason);
                    }
                }
            } finally {
                isBulkConnecting = false;
            }
            RebuildGrid(false);
            UpdateDisconnectedCount(true);
        }

        // returns node indexes, not entity ids
        public int GetAutoConnectCandidatesForPosition(Vector2 position,
                int range, int[] candidates, bool onlyReachable) {
            unsafe {
                fixed (int* p = candidates) {
                    int numRes = ElectricityUtils.AutoConnectCandidatesForPosition(
                        position.x, position.y, range, arrNodes.Count, candidates.Length,
                            (ElectricNode*) arrNodes.Array.GetUnsafePtr(), p,
                                onlyReachable);
                    return numRes;
                }
            }
        }


        protected override void OnInitialize() {
            //Ready.SetEnergy(false, "Cold");
            Prepare();
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            overlays.Add(new OverlayInfo(OverlayType.Visual,
                OverlayId.Electricity, IconId.WOverlayEnergy));
        }

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            UI.ToggleOverlay(info, on);
        }

        private void OnLoadSave(GameState state) {
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.EveryTickOrFrame.AddListener(EveryFrame);
            UI = new ElectricityUI(state, this, arrNodes, arrConnections, grids);
            RestoreData();
            // why did we do this? to warm the grid up
            //ExecuteTick();
            //Ready.SetEnergy(false, "Just loaded");
        }

        private void EveryFrame() {
            // if (!GameState.IsCurrent(S)) { return; }
            if (needsGridRebuild) {
                RebuildGrid(false);
            }
            if (!Ready.Gen || !Ready.Save) { return; }
            if (currentDelegateIndex != NoPendingDelegates) {
                ExecuteDelegates();
            }
        }

        private int lastUpdateDisconnectedCountFrame;

        public int UpdateDisconnectedCount(bool force = false) {
            var fc = GameTimer.frameCount;
            if (!force && lastUpdateDisconnectedCountFrame == fc) {
                return DisconnectedCount;
            }
            lastUpdateDisconnectedCountFrame = fc;
            DisconnectedCount = 0;
            ConnectedCount = 0;
            for (int i = arrNodes.Count - 1; i >= 0; i--) {
                if (arrNodes.TryGet(i, out var node)) {
                    if (node.IsInitialized && node.NumConnections < 1) {
                        DisconnectedCount++;
                    } else {
                        ConnectedCount++;
                    }
                }
            }
            return DisconnectedCount;
        }

        private void AfterBuildGrid(NativeList<ElectricGrid> gridsOut) {
            #if DEBUG_GRID
            D.Warn("Got Grids: {0}", gridsOut.Length);
            for (int i = 0; i < gridsOut.Length; i++) {
                D.Warn("Grid: {0}", gridsOut[i]);
            }
            #endif
            SyncNodes();
            var gridsOutCnt = gridsOut.Length;
            var canCopy = Maths.Min(gridsOutCnt, GridsCount);
            var instantBuild = A.InstantBuild;
            // older grids
            for (int i = 0; i < canCopy; i++) {
                var newGrid = gridsOut[i];
                var oldGrid = grids[i];
                oldGrid.Size = newGrid.Size;
                oldGrid.HasShipComputer = newGrid.HasShipComputer;
                oldGrid.NumShipCPUs = newGrid.NumShipCPUs;
                grids[i] = oldGrid;
            }
            // newer grids
            for (int i = canCopy; i < gridsOutCnt; i++) {
                var grid = gridsOut[i];
                /*
                // no point, new grids will likely never have > 0 cpu anyway
                // until they get simulated
                grid.IsCPUBound = grid.HasShipComputer
                    && grid.Size > grid.CPU * Tunables.GridConnectionsPerCPU;
                */
                if (grids.Length > i) {
                    grids[i] = grid;
                } else {
                    grids.Add(grid);
                }
            }
            // we don't have to remove excess, because we always
            // use GridCount and never look at grids.Length
            // for (int i = grids.Length - 1; i >= gridsOutCnt; i--) {
            //     grids.RemoveAt(i);
            // }
            GridsCount = gridsOutCnt;
            gridsOut.Dispose();
            UI.AfterRebuild();
            S.Sig.ElectricityGridChanged.Send();
            if (S.Clock.IsStopped) {
                ValidateAllConnections(true);
            }
        }

        private void SyncNodes() {
            int syncedCount = 0;
            var array = arrNodes.Array;

            for (int i = arrNodes.Count - 1; i >= 0; i--) {
                ref var node = ref array.GetUnsafeRef(i);
                if (!node.IsValid) { continue; }

                if (!node.NeedsSync) { continue; }
                if (!node.IsInitialized) { continue; }
                syncedCount++;
                node.NeedsSync = false;
                array[i] = node;
                //D.Warn("Changed Node: {0}", node);
                if (nodeComponents.TryGetValue(node.Idx, out var comp)) {
                    #if DEBUG_GRID
                    if (comp.Node.Idx != node.Idx) {
                        D.Err("Trying to update a node component with wrong idx! expected {0}, was {1}. Old node: {2}. New node {3}",
                            node.Idx, comp.Node.Idx, comp.Node, node);
                    }
                    #endif
                    if (comp.Tile.IsActive) {
                        comp.Update(node);
                    }
                } else {
                    D.Err("Node changed but there is no registered component! {0} -> {1}",
                        node.Idx, node);
                }
            }
        }

        public void RebuildGrid(bool force) {
            if (!force && !needsGridRebuild) {
                return;
            }
            needsGridRebuild = false;
            var gridsOut = new NativeList<ElectricGrid>(grids.Length + 1, Allocator.TempJob);
            var parents = new NativeArray<int>(arrNodes.Count, Allocator.TempJob);
            var connected = new NativeArray<bool>(arrNodes.Count, Allocator.TempJob);
            var reachable = new NativeArray<bool>(arrNodes.Count, Allocator.TempJob);
            var ranks = new NativeArray<int>(arrNodes.Count, Allocator.TempJob);
            var gridIds = new NativeList<int>(grids.Length + 1, Allocator.TempJob);
            var job = new BuildGridJob {
                Conns = arrConnections.Array,
                ConnCount = arrConnections.Count,
                Nodes = arrNodes.Array,
                NodeCount = arrNodes.Count,
                Connected = connected,
                Reachable = reachable,
                Grids = gridsOut,
                Parents = parents,
                Ranks = ranks,
                GridIds = gridIds,
                IsSandbox = A.InstantBuild,
            };
            #if DEBUG_GRID_JOB
            job.Execute();
            #else
            job.Run();
            #endif
            parents.Dispose();
            ranks.Dispose();
            gridIds.Dispose();
            connected.Dispose();
            reachable.Dispose();
            AfterBuildGrid(gridsOut);
        }

        private void RestoreData() {
            if (data == null) {
                data = new EnergySysData();
                data.StateHistory = new();
                return;
            }
            TotalMem = data.TotalMem;
            TotalCPU = data.TotalCPU;
            TotalGPU = data.TotalGPU;
            TotalDisk.SetSilent(data.TotalDisk);

            arrNodes.LoadData(data.NodesCount, data.NodesRecycler, data.Nodes);
            data.Nodes = null;
            data.NodesRecycler = null;
            data.NodesCount = 0;
            arrConnections.LoadData(data.ConnectionsCount, data.ConnectionsRecycler, data.Connections);
            data.Connections = null;
            data.ConnectionsRecycler = null;
            data.ConnectionsCount = 0;

            grids.Clear();
            GridsCount = data.GridsCount;
            if (data.Grids != null) {
                for (int i = 0; i < GridsCount; i++) {
                    ElectricGrid g = data.Grids[i];
                    grids.Add(g);
                }
            }
            data.Grids = null;
            data.GridsCount = 0;
            var loadedIds = new HashSet<int>();
            var brokenNodes = new Dictionary<int, ElectricNodeComp>();
            // intentional forward loop
            for (int i = 0; i < arrNodes.Count; i++) {
                if (arrNodes.TryGet(i, out var n)) {
                    if (n.IsInitialized) {
                        var comp = S.Components.Find<ElectricNodeComp>(n.EntityId);
                        if (comp == null) {
                            var tile = S.FindEntity<Tile>(n.EntityId);
                            if (tile == null) {
                                D.ErrSoft("Trying to load a node comp for a tile that is null by id: {0}", n.EntityId);
                            } else {
                                D.ErrSoft("Trying to load a node comp is null by id! {0}",
                                    n.EntityId);
                            }
                            arrNodes.Remove(i);
                            brokenNodes.TryAdd(n.EntityId, null);
                            continue;
                        }
                        if (!comp.IsConstructed) {
                            D.Err("Trying to load a node where comp is not constructed! {0} {1}",
                                n, comp);
                            arrNodes.Remove(i);
                            brokenNodes.TryAdd(n.EntityId, null);
                            continue;
                        }
                        if (n.Idx != i) {
                            D.Err("Trying to load a node where idx does not match number! {0} != {1}. {2}",
                                n.Idx, i, comp);
                            arrNodes.Remove(i);
                            brokenNodes.TryAdd(n.EntityId, null);
                            continue;
                        }
                        if (!loadedIds.Add(n.EntityId)) {
                            var existing = comp.Node;
                            D.Err("Duplicate node for entity with id: {0} detected! {1}. Copy: {2}. Existing: {3}. Fixing",
                                n.EntityId, comp, n, existing);
                            arrNodes.Remove(i);
                            for (int ci = arrConnections.Count - 1; ci >= 0; ci--) {
                                var conn = arrConnections.Array[ci];
                                var c = conn.NodeIdxs;
                                if (c.Item1 == i || c.Item2 == i) {
                                    // well fuck
                                    arrConnections.Remove(ci);
                                    D.Err("Removing broken connection: {0}", c);
                                }
                            }
                            needsGridRebuild = true;
                            continue;
                        }
                        if (comp != null) {
                            var loaded = comp.LoadNode(n);
                            var idx = loaded.Idx;
                            if (!nodeComponents.TryAdd(loaded.Idx, comp)) {
                                D.Err("Trying to load component by idx {0} twice! {1} -> {2}. Existing: {3}",
                                    idx, comp, loaded, nodeComponents[idx]);
                            } else {
                                arrNodes.Update(idx, loaded);
                            }
                        } else {
                            brokenNodes.TryAdd(n.EntityId, null);
                            D.Err("Failed restoring el node - not found by entity id: {0}",
                                n.EntityId);
                                arrNodes.Remove(i);
                        }

                    }
                }
            }


            // final validation to detect broken state
            var allElComps = S.Components.AllByType<ElectricNodeComp>();
            if (allElComps != null) {
                foreach (ElectricNodeComp elnode in allElComps) {
                    elnode.IsInitialized = true;
                    if (!elnode.IsRegistered && elnode.Tile.IsConstructed) {
                        D.Err("Node is not registered after load! {0}. Autofixing.",
                            elnode.Entity);
                        brokenNodes.TryAdd(elnode.EntityId, elnode);
                    }
                }
            }

            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                var c = conn.Connection;
                if (brokenNodes.ContainsKey(c.Item1) ||
                        brokenNodes.ContainsKey(c.Item2) ||
                            conn.NodeIdxs.Item1 < 0 || conn.NodeIdxs.Item2 < 0
                                // double check that entities exist and are connectable
                                || !CanBeConnected(S.FindEntity<Tile>(c.Item1))
                                || !CanBeConnected(S.FindEntity<Tile>(c.Item2))) {
                    // well fuck
                    arrConnections.Remove(i);
                    needsGridRebuild = true;
                    D.ErrSoft("Removing broken connection: {0}", c);
                } else {
                    existingConnections.Add(conn.Connection);
                }
            }

            foreach (var n in brokenNodes) {
                if (n.Value != null) {
                    D.Err("Readding broken node: {0}", n);
                    AddNode(n.Value);
                    needsGridRebuild = true;
                }
            }
            // One more iteration to validate connections
            ValidateAllConnections(false);

            if (S.Components.CountByType<ShipComputerComp>() == 0) {
                D.Warn("No ship computers, will rebuild the grid");
                needsGridRebuild = true;
            }
        }

        private void ValidateAllConnections(bool fast) {
            var allElComps = S.Components.AllByType<ElectricNodeComp>();
            bool didRepair = false;
            if (allElComps != null) {
                foreach (ElectricNodeComp elnode in allElComps) {
                    if (fast) {
                        if (elnode.IsReachable && elnode.Node.GridId == -1) {
                            if (!ValidateConnectionsFor(elnode)) {
                                // revalidate full, then resync the cache - the
                                // full pass may find nothing left to repair
                                ValidateAllConnections(false);
                                RebuildExistingConnections();
                                return;
                            }
                        }
                    } else {
                        if (!ValidateConnectionsFor(elnode)) {
                            didRepair = true;
                        }
                    }
                }
            }
            if (didRepair) {
                RebuildExistingConnections();
            }
        }

        // A repair means the node counts and arrConnections had drifted apart,
        // which usually means the cache holds tuples with no connection behind
        // them. Those would reject every attempt to reconnect the pair for the
        // rest of the session, so the cache is rebuilt from the source of truth
        private void RebuildExistingConnections() {
            existingConnections.Clear();
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                existingConnections.Add(conn.Connection);
            }
        }

        public bool ValidateConnectionsFor(ElectricNodeComp elnode) {
            var found = ConnectionsFor(elnode.EntityId, Caches.IntArray1024);
            if (elnode.Node.NumConnections != found) {
                D.ErrSoft("Fixing incorrect connections count for connector: {0} != {1}. {2}",
                    elnode.Node.NumConnections, found, elnode);
                elnode.Node.NumConnections = found;
                if (!elnode.Node.IsShipComputer) {
                    elnode.Node.IsReachable = false;
                }
                if (found == 0 && elnode.Node.IsConsuming) {
                    elnode.Node.Consumed = 0f;
                }
                // Not Update: that keeps the system's count, and this is the system repairing it
                Store(elnode, in elnode.Node);
                // IsReachable is only ever recomputed by BuildGridJob, so
                // without a rebuild the node we just flagged stays unreachable
                needsGridRebuild = true;
                return false;
            }
            return true;
        }

        private bool CanBeConnected(Tile tile) {
            return tile != null && tile.IsActive && tile.IsConstructed &&
                tile.Health?.IsHeavilyDamaged != true;
        }

        private void Prepare() {
            arrNodes = new ReusableNativeArray<ElectricNode>(
                "ElectricNodes", 256, 256000);
            arrConnections = new ReusableNativeArray<ElectricConnection>(
                "ElectricConnections", 512, 512000);
            grids = new NativeList<ElectricGrid>(1, Allocator.Persistent);
        }

        private const int EnergyLockKey = 118490178;

        private void OnTick(long ticks) {
            if (!Ready.Gen) { return; }
            if (ticks % 10 != 0) { return; }
            if (GridsCount == 0) {
                // update situation even if there are no grids
                UpdateSituation();
                batteryDischargeAlert.Update(this);
                return;
            }
            if (currentDelegateIndex != NoPendingDelegates) {
                #if DEBUG_AFTER_TICK
                D.Warn("Skipping electricity tick, still got {0} delegates to crunch",
                    currentDelegateIndex);
                #endif
                S.Clock.TryLock(EnergyLockKey, "Energy Callbacks");
                isLockCreated = true;
                return;
            }
            if (isLockCreated) {
                D.Warn("Prematurely unlocking the tick, delegates are still executing");
                isLockCreated = false;
                S.Clock.Unlock(EnergyLockKey);
            }
            ExecuteTick(ticks);
        }

        // Index of the next after-tick hook to run when a pass was split across frames.
        // 0 used to mean "done" too: a pass that ran out of frame budget right before hook 0
        // never resumed, and one that ran out on hook 0 kept the tick lock until the clock
        // gave up on it after 60 s.
        private const int NoPendingDelegates = -1;
        private int currentDelegateIndex = NoPendingDelegates;
        private int delegateExecutionFrameCount;
        private readonly ErrorThrottle delegateErrors = new();

        public void ExecuteTick(long ticks) {
            if (needsGridRebuild) {
                // if (G.ClockSpeed < ClockSpeed.Unlimited) {
                //     D.Warn("Executing tick when needs grid rebuild is pending! {0}", ticks);
                // }
                RebuildGrid(false);
            }
            // Magnetic storm malfunction
            int malfuncMod = S.BB.Get(BBKeyH.MagneticStormAffectedMod, 0);
            // An EMP shield keeps the storm off the ship's own grid. The BB keys stay, they
            // also drive the storm's effects on other ships and on story events
            if (malfuncMod != 0 && S.Query.IsPlayerShieldedFromStorm.Ask()) {
                malfuncMod = 0;
            }
            int malfuncDevId;
            if (malfuncMod == 0) {
                malfuncDevId = 0;
            } else {
                malfuncDevId = S.BB.Get(BBKeyH.MagneticStormAffectedId, 0);
            }

            float consumptionMul = S.BB.Get(BBKeyH.EnergyConsumptionMul, 1f);

            var gridJob = new TickGridJob {
                Ticks = ticks,
                GridsCount = GridsCount,
                Grids = grids,
                Nodes = arrNodes.Array,
                NodesCount = arrNodes.Count,
                MalfunctionMod = malfuncMod,
                MalfunctionId = malfuncDevId,
                ConsumptionMul = consumptionMul,
            };
            using (D.Timer("TickGridJob", 0.005f)) {
                #if DEBUG_GRID_JOB
                for (int i = 0; i < GridsCount; i++) {
                    gridJob.Execute(i);
                }
                #else
                gridJob.Run(GridsCount);
                #endif
            }
            AfterExecuteTick();
        }

        private void AfterExecuteTick() {
            using (D.Timer("SyncNodes", 0.01f)) {
                SyncNodes();
            }
            using (D.Timer("UpdateSituation", 0.01f)) {
                UpdateSituation();
            }
            batteryDischargeAlert.Update(this);
            // if we haven't done executing old delegates, we will finish them up first
            if (currentDelegateIndex == NoPendingDelegates) {
                currentDelegateIndex = afterTickDelegates.Count - 1;
            }
            delegateExecutionFrameCount = 0;
            #if PROFILE_DELEGATES
            ExecuteDelegatesProfiled();
            #else
            using (D.Timer("ExecuteDelegates", 0.01f)) {
                ExecuteDelegates();
            }
            #endif
            #if !PRODUCTION_BUILD
            ValidateDelegates();
            #endif
        }

        private void ValidateDelegates() {
            foreach (var d in afterTickDelegates) {
                if (d.Target is IComponent comp) {
                    if (!comp.Entity.IsActive) {
                        D.Err("Delegates contains inactive entity! {0} -> {1}",
                            comp, comp.Entity);
                    }
                }
            }
        }

        private void ExecuteDelegates() {
            var startI = currentDelegateIndex;
            var startTime = Time.realtimeSinceStartupAsDouble;
            var isHighSpeed = S.Clock.IsUnlimited || !Ready.Gen;
            AfterTickGrid hook = null;
            int curI = startI;
            try {
                if (startI >= afterTickDelegates.Count) {
                    D.Warn("ExecuteDelegates currentDelegateIndex too large! StartI: {0}. Count: {1}",
                        startI, afterTickDelegates.Count);
                    startI = afterTickDelegates.Count - 1;
                }
                for (int i = startI; i >= 0; i--) {
                    var cnt = afterTickDelegates.Count;
                    if (cnt <= i) {
                        D.Warn("Can't get afterTickDelegate by idx: {0}. Count: {1}", i, cnt);
                        if (cnt == 0) {
                            curI = 0;
                            break;
                        }
                        i = cnt - 1;
                    }
                    curI = i;
                    hook = afterTickDelegates[i];
                    // per hook: the catch below ends the pass, so a hook that threw on
                    // every tick left every hook after it without its power callback
                    try {
                        hook.Invoke();
                    } catch (System.Exception ex) {
                        if (delegateErrors.ShouldLog(hook, out var count)) {
                            D.LogEx(ex, "Exception when executing after tick grid: {0} ({1} times)",
                                hook, count);
                        }
                    }
                    // not after the last hook: there is nothing left to push to the next frame
                    if (!isHighSpeed && i > 0 && i % 25 == 0) {
                        if (!GameTimer.HasAvailableFrameBudget(verbose: false)) {
                            currentDelegateIndex = i - 1;
                            delegateExecutionFrameCount++;
                            #if DEBUG_AFTER_TICK
                            D.Warn("Pushing delegate callbacks to next frame: {0}/{1}. Frames {2}",
                                currentDelegateIndex, afterTickDelegates.Count,
                                    delegateExecutionFrameCount);
                            #endif
                            return;
                        }
                    }
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Exception when executing after tick grid: {0}", hook);
                currentDelegateIndex = curI - 1;
            }
            // done!
            currentDelegateIndex = NoPendingDelegates;
            if (isLockCreated) {
                isLockCreated = false;
                S.Clock.Unlock(EnergyLockKey);
            }
        }

        #if PROFILE_DELEGATES
        private readonly Dictionary<Type, float> delegateDurations = new();
        private readonly Dictionary<Type, int> delegateCounts = new();

        private void ExecuteDelegatesProfiled() {
            double startTime = Time.realtimeSinceStartupAsDouble;
            delegateDurations.Clear();
            delegateCounts.Clear();
            double totalDur = 0d;
            for (int i = afterTickDelegates.Count - 1; i >= 0; i--) {
                double startA = Time.realtimeSinceStartupAsDouble;
                AfterTickGrid hook = afterTickDelegates[i];
                hook.Invoke();
                var durA = Time.realtimeSinceStartupAsDouble - startA;
                var deleg = hook.Target.GetType();
                if (!delegateDurations.TryGetValue(deleg, out float curDur)) {
                    curDur = 0f;
                }
                if (!delegateCounts.TryGetValue(deleg, out int curCnt)) {
                    curCnt = 0;
                }
                curDur += durA;
                totalDur += durA;
                curCnt++;
                delegateDurations[deleg] = curDur;
                delegateCounts[deleg] = curCnt;
            }
            var dur = Time.realtimeSinceStartupAsDouble - startTime;
            if (dur > 0.01d) {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                sb.AppendFormat("Ticking {0} energy delegates took {1:0.0000}s. (without profiling: {2:0.0000}s):\n",
                    afterTickDelegates.Count, dur, totalDur);
                foreach (var del in delegateDurations) {
                    var tot = del.Value;
                    var cnt = delegateCounts[del.Key];
                    var avg = tot / cnt;
                    sb.AppendFormat("[x{0}] {1:0.0000}s(avg{2:0.00000}s) - {3}\n",
                        cnt, tot, avg, del.Key.Name);
                }
                D.Warn(sb.ToString());
            }
        }
        #endif

        private int prevMem = 0;
        private int prevCPU = 0;
        private int prevGPU = 0;
        private int prevDisk = 0;

        private void UpdateSituation() {
            var totalMem = 0;
            var totalCPU = 0;
            var totalDisk = 0;
            var totalGPU = 0;
            int size = 0;
            int shipComputers = 0;
            float totalMaxOutputWOBat = 0;
            float totalConsumption = 0;
            float totalDeficit = 0;
            float totalBatCharge = 0;
            float totalBatOutput = 0;
            float totalRenewOutput = 0;
            bool hadDeficit = false;
            // quick scan if there are any grids with size > 1
            bool hasGridWithCPUandSizeOver1 = false;
            bool hasAtLeastOneShipComputer = false;
            for (int i = 0; i < GridsCount; i++) {
                var g = grids[i];
                if (g.HasShipComputer) {
                    hasAtLeastOneShipComputer = true;
                    if (g.Size > 1) {
                        hasGridWithCPUandSizeOver1 = true;
                        break;
                    }
                }
            }
            for (int i = 0; i < GridsCount; i++) {
                var g = grids[i];
                if (!g.HasShipComputer) { continue; }
                if (hasGridWithCPUandSizeOver1) {
                    if (g.HasShipComputer && g.Size < 2) {
                        continue;
                    }
                }
                totalCPU += g.CPU;
                totalMem += g.Memory;
                totalDisk += g.Disk;
                totalGPU += g.GPU;
                shipComputers += g.NumShipCPUs;
                size += g.Size;
                totalMaxOutputWOBat += g.MaxOutputWithoutBat;
                totalRenewOutput += g.OutputRenewable;
                totalConsumption += g.Consumption;
                if (g.BatCapacity > 0f) {
                    totalBatCharge += g.BatCharge / g.BatCapacity;
                }
                totalBatOutput += g.MaxOutputBat;
                // When starting a new game, first two electricity ticks could produce deficit
                if (g.Deficit > 0 && G.Ticks > 20) {
                    hadDeficit = true;
                    totalDeficit += g.Deficit;
                }
            }

            if (shipComputers == 0 && hasAtLeastOneShipComputer) {
                // D.Warn("Situation does not have any ship computers, while we have at least one!");
                shipComputers = 1;
            }

            var sit = S.Situation;
            sit.GridMaxOutput = totalMaxOutputWOBat;
            sit.GridConsumption = totalConsumption;
            sit.GridDeficit = totalDeficit;
            sit.GridBatOutput = totalBatOutput;
            sit.GridBatCharge = totalBatCharge;
            sit.GridRenewableOutput = totalRenewOutput;
            sit.GridSize = size;
            sit.Grids = GridsCount;
            sit.ShipComputers = shipComputers;

            if (prevMem != totalMem || prevCPU != totalCPU
                    || prevDisk != totalDisk || prevGPU != totalGPU) {
                sit.TotalMemory = prevMem = totalMem;
                sit.TotalCPU = prevCPU = totalCPU;
                sit.TotalGPU = prevGPU = totalGPU;
                sit.TotalDisk = prevDisk = totalDisk;

                TotalDisk.Set(totalDisk);
                TotalCPU = totalCPU;
                TotalMem = totalMem;
                TotalGPU = totalGPU;

                S.Sig.CPUResourcesUpdated.Send(
                    totalCPU, totalMem, totalDisk, totalGPU);
            }

            if (hadDeficit) {
                if (totalMaxOutputWOBat > 0f) {
                    float deficitPct = Maths.Clamp(totalDeficit / totalMaxOutputWOBat,
                        0f, 1f);
                    // S.Situation.AddTension((deficitPct * 10f) / Consts.TicksPerHour,
                        // "Electricity deficit");
                }
            }

            if (hadDeficit && !isDeficitEventShowing) {
                S.Sys.Log.AddLine($"{Texts.Yellow(T.ElectricityDeficit)}: {Texts.Red(Units.Electricity(totalDeficit))}");
                ShowDeficitEvent();
            } else if (!hadDeficit && isDeficitEventShowing) {
                Notification.Remove(ref deficitEvent);
                isDeficitEventShowing = false;
            }
            if (Ready.Energy) {
                SaveSnapshot(G.Ticks, sit);
                if (sit.ShipComputers == 0 && !S.Scenario.IsSandbox && !S.IsGameOver) {
                    if (S.Components.CountMatching<ShipComputerComp>(
                            c => c.IsConstructed) > 0) {
                        D.Err("Situation ship computers count is 0 while we have a ship computer!");
                    } else {
                        new GameOverCutscene(S);
                    }
                }
            } else {
                Ready.SetEnergy(true, "Warm");
            }
            S.Sig.SituationUpdated.Send(sit);
        }
        private void SaveSnapshot(long tick, Situation s) {
            data.Add(EnergyStateSnapshot.Create(tick, s));
        }

        private readonly BatteryDischargeAlert batteryDischargeAlert = new();
        private NotificationHandle deficitEvent;
        private bool isDeficitEventShowing;
        private void ShowDeficitEvent() {
            if (!deficitEvent.IsValid) {
                deficitEvent = Notification.Build(IconId.CElectricityDeficit,
                    T.ElectricityDeficit, 0, NotificationType.Alert)
                    .WithOnClick(() => {
                        S.UIQuery.ToggleElectricity.Ask(true);
                    })
                    // The top bar electricity widget gets the ring that points across the
                    // screen, and a second one sweeping onto the hub would fight it
                    .PulseOnly()
                    .Show();
            }
            isDeficitEventShowing = true;
        }

        public void AddNode(ElectricNodeComp comp, bool allowUnbuilt = false) {
            comp.IsInitialized = true;
            var node = comp.Node;
            #if DEBUG_GRID
            D.Warn("Registering a node: {0}->{1}", comp, node);
            #endif
            if (comp.Tile.Health?.IsDestroyed == true) {
                D.Err("Trying to register a node for a destroyed entity! {0}", comp.Entity);
                return;
            }
            node.EntityId = comp.EntityId;
            if (!node.IsInitialized) {
                D.Err("Trying to register an uninitialized node! {0} -> {1}", node, comp);
                return;
            }
            var tile = comp.Tile;
            if (!allowUnbuilt && !tile.IsConstructed) {
                D.Err("Trying to register an unbuilt tile! {0} -> {1}", node, comp);
                return;
            }
            node.PosIdx = tile.PosIdx;
            node.Position = tile.Position;
            if (arrNodes.TryReserveSlot(out var idx)) {
                node.Idx = idx;
                arrNodes.Update(idx, node);
                if (!nodeComponents.TryAdd(idx, comp)) {
                    D.Err("NodeComponents already contains idx {0}! Trying to overwrite {1} with {2}",
                        idx, nodeComponents[idx], comp);
                }
            } else {
                D.Err("Failed to reserve a slot for electric node: {0}", node);
                idx = -1;
                node.Idx = idx;
            }
            comp.Node = node;
            comp.SetSlot(idx);
            if (node.IsShipComputer) {
                // ship computer will create its own grid without any connections
                needsGridRebuild = true;
            }
        }

        public void RemoveNode(ElectricNodeComp comp) {
            var node = comp.Node;
            if (node.Idx == -1) {
                D.Err("Trying to remove an unregistered node: {0}", comp.Entity);
                return;
            }
            // get the most up to date version in case sync was pending:
            node = arrNodes.Array[node.Idx];
            #if DEBUG_GRID
            D.Warn("Removing node {0}", node);
            #endif
            int found = 0;
            int max = Caches.CompArray64.Length;
            foreach (var owner in afterTickOwners) {
                var eId = owner.Key.EntityId;
                // entity id could be set to 0 or it could be still valid, depends on order of components
                // in removal queue
                if (eId == 0 || eId == node.EntityId) {
                    Caches.CompArray64[found++] = owner.Key;
                    if (found >= max) {
                        D.Err("RemoveNode found something with over {0} components on one entity! {1}",
                            max, comp.Tile);
                        break;
                    }
                }
            }
            for (int i = 0; i < found; i++) {
                RemoveAfterTick(Caches.CompArray64[i]);
            }

            if (node.NumConnections > 0) {
                DisconnectAll(comp);
                // This node was connected, we will need a rebuild
                needsGridRebuild = true;
            }
            arrNodes.Remove(node.Idx);
            nodeComponents.Remove(node.Idx);
            node.Idx = -1;
            comp.Node = node;
            comp.SetSlot(-1);
        }

        public string GetName() {
            return SysId;
        }

        private bool needsGridRebuild;
        public bool TryConnect(ElectricNodeComp comp1, ElectricNodeComp comp2,
                out string failReason) {
            if (comp1 == null || comp2 == null) {
                failReason = T.AdRejTargetUnavailable;
                return false;
            }
            if (comp1 == comp2) {
                D.Err("Trying to connect an object to itself! {0}", comp1);
                failReason = T.OperationForbidden;
                return false;
            }
            CatchUp(comp1);
            CatchUp(comp2);
            // The ref overload mutates comp.Node in place, so ElectricNodeComp.Update
            // can't see the connected state flip later - listeners (battle stations
            // registering into combat, etc) have to be told here
            var wasConnected1 = comp1.IsConnected;
            var wasConnected2 = comp2.IsConnected;
            if (!TryConnect(ref comp1.Node, ref comp2.Node, out failReason)) {
                return false;
            }
            TransmitIfConnectionFlipped(comp1, wasConnected1);
            TransmitIfConnectionFlipped(comp2, wasConnected2);
            return true;
        }

        public bool TryDisconnect(ElectricNodeComp comp1, ElectricNodeComp comp2,
                out string failReason) {
            if (comp1 == null || comp2 == null) {
                failReason = T.AdRejTargetUnavailable;
                return false;
            }
            CatchUp(comp1);
            CatchUp(comp2);
            var wasConnected1 = comp1.IsConnected;
            var wasConnected2 = comp2.IsConnected;
            if (!TryDisconnect(ref comp1.Node, ref comp2.Node, out failReason)) {
                return false;
            }
            TransmitIfConnectionFlipped(comp1, wasConnected1);
            TransmitIfConnectionFlipped(comp2, wasConnected2);
            return true;
        }

        private static void TransmitIfConnectionFlipped(ElectricNodeComp comp, bool wasConnected) {
            if (comp.IsConnected != wasConnected) {
                comp.Transmit(comp, MsgIdH.ElectricNodeConnectionChanged);
            }
        }

        // Private: given a component's own Node, this hides the connected state flip from
        // listeners for good. Outside code links components through the overloads above
        private bool TryConnect(ref ElectricNode node1, ref ElectricNode node2,
                out string failReason) {

            if (!node1.IsInitialized || !node2.IsInitialized) {
                failReason = T.ElDeviceNotPowerable;
                return false;
            }
            if (!node1.IsValid || !node2.IsValid) {
                D.Err("Trying to connect something with an invalid node! {0} <-> {1}",
                    node1, node2);
                failReason = T.AdRejUndefined;
                return false;
            }

            if (node1.EntityId == node2.EntityId) {
                D.Err("Trying to connect nodes with same entityId! {0} and {1}",
                    node1, node2);
                failReason = T.OperationForbidden;
                return false;
            }

            if (node1.Idx == node2.Idx) {
                D.Err("Trying to connect nodes with same idx! (EntityID is different!) {0} and {1}",
                    node1, node2);
                failReason = T.OperationForbidden;
                return false;
            }

            int range = 0;
            if (node1.IsConnector) {
                if (node1.NumConnections >= node1.MaxConnections) {
                    node1.NumConnections = node1.MaxConnections;
                    failReason = T.ElConnectorFullyUtilized;
                    return false;
                }
                range = node1.ConnectorRange;
            }

            if (node2.IsConnector) {
                if (node2.NumConnections >= node2.MaxConnections) {
                    node2.NumConnections = node2.MaxConnections;
                    failReason = T.ElConnectorFullyUtilized;
                    return false;
                }
                if (node2.ConnectorRange > range) {
                    range = node2.ConnectorRange;
                }
            }

            if (Vectors.SqrDistance(node1.Position, node2.Position) > range * range) {
                #if DEBUG_GRID
                D.Warn("Out of range! {0} -> {1}. Range: {2}. Dist: {3}",
                    node1.Position, node2.Position, range, Vectors.Distance(
                            node1.Position, node2.Position));
                #endif
                failReason = T.ElConnectorOutOfRange;
                return false;
            }

            if (node1.IsShipComputer && node1.NumConnections == 0
                    || node2.IsShipComputer && node2.NumConnections == 0) {
                // See if we can connect one more CPU
                if (!CanAddAnotherShipComputer(out failReason)) {
                    return false;
                }
            }

            var idTuple = Maths.OrderedTuple(node1.EntityId, node2.EntityId);
            // 1. figure out if nodes are connected
            if (existingConnections.Contains(idTuple)) {
                // FIXME this translation is odd
                failReason = T.EnergyConAlreadyConnected;
                return false;
            }

            // 2. if not, reserve the connection slot. Everything that can fail
            // has to happen before we mutate anything, so that a failure can
            // never leave the nodes, existingConnections and arrConnections
            // disagreeing with each other
            if (!arrConnections.TryReserveSlot(out var idx)) {
                failReason = "Failed reserving connection slot";
                return false;
            }

            existingConnections.Add(idTuple);
            node1.NumConnections++;
            node1.NeedsSync = true;
            arrNodes.Update(node1.Idx, node1);
            node2.NumConnections++;
            node2.NeedsSync = true;
            arrNodes.Update(node2.Idx, node2);

            var conn = new ElectricConnection {
                Idx = idx,
                GridId = -1,
                Pos1 = node1.Position,
                Pos2 = node2.Position,
                Connection = idTuple,
                Type = ElectricConnection.TypeBetween(node1, node2),
                NodeIdxs = (node1.Idx, node2.Idx),
            };

            #if DEBUG_GRID
            D.Warn("Adding connection: {0}. {1} and {2}", conn, node1, node2);
            #endif
            arrConnections.Update(idx, conn);
            failReason = null;
            // Auto connect redraws once after its last link, the redraw is a full one
            if (!isBulkConnecting) {
                UI.RefreshVFX();
            }
            needsGridRebuild = true;
            // Sent last, once the grid is consistent: a listener that throws
            // must not be able to unwind through a half applied connection
            S.Sig.EnergyConnectionChanged.Send(node1.EntityId, node2.EntityId, true);
            return true;
        }

        public int ShipComputerCap => A.InstantBuild ? 512 : S.BB.Get(BBKeyH.CPUCap, 1);

        // A lone Ship Computer (a grid of its own) does not count against the cap
        public int ShipComputersInGrids() {
            int numCPUsInGrids = 0;
            for (int i = 0; i < GridsCount; i++) {
                var g = grids[i];
                if (g.Size > 1) {
                    numCPUsInGrids += g.NumShipCPUs;
                }
            }
            return numCPUsInGrids;
        }

        public bool CanAddAnotherShipComputer(out string failReason) {
            var cpuCap = ShipComputerCap;
            if (ShipComputersInGrids() >= cpuCap) {
                failReason = T.CpuCapWouldBeReached_V1.F(cpuCap);
                return false;
            }
            failReason = null;
            return true;
        }

        private bool TryDisconnect(ref ElectricNode node1, ref ElectricNode node2,
                out string failReason) {
            var idTuple = Maths.OrderedTuple(node1.EntityId, node2.EntityId);
            int gridId = node1.GridId;
            if (!existingConnections.Remove(idTuple)) {
                failReason = "Connection not found";
                #if !PRODUCTION_BUILD
                D.Err("Trying to disconnect but not connected: {0} and {1}", node1, node2);
                #endif
                return false;
            }
            failReason = null;
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                if (arrConnections.Array[i].Connection == idTuple) {
                    RemoveConnection(ref node1, ref node2, i, false);
                    if (node1.IsShipComputer && node1.NumConnections == 0
                            || node2.IsShipComputer && node2.NumConnections == 0) {
                        var g = grids[gridId];
                        g.NumShipCPUs--;
                        grids[gridId] = g;
                    }
                    return true;
                }
            }
            D.Err("Existing connection was in cache but not in arrConnections! {2}: {0}<->{1}",
                node1, node2, idTuple);
            // The cache was ahead of arrConnections, so both nodes are counting
            // a connection that has nothing behind it. Bring them back in sync,
            // otherwise a connector can stay permanently "fully utilized"
            DropOrphanedConnection(ref node1);
            DropOrphanedConnection(ref node2);
            needsGridRebuild = true;
            UI.RefreshVFX();
            return true;
        }

        private void DropOrphanedConnection(ref ElectricNode node) {
            if (!node.IsInitialized || !node.IsValid) { return; }
            if (node.NumConnections > 0) {
                node.NumConnections--;
            }
            if (node.NumConnections == 0) {
                node.Consumed = 0f;
                node.Produced = 0f;
                node.Deficit = 0f;
            }
            node.NeedsSync = true;
            arrNodes.Update(node.Idx, node);
        }

        private void RemoveConnection(ref ElectricNode node1, ref ElectricNode node2,
                int connIdx, bool isBulkRemoval) {
            var conn = arrConnections.Array[connIdx];
            if (!conn.IsInitialized) {
                D.Err("Trying to remove a non-initialized connection! {0}", conn);
                return;
            }
            if (!arrConnections.Remove(connIdx)) {
                D.Err("Trying to remove a connection that does not exist! {0}->{1}",
                    node1, node2);
            #if DEBUG_GRID
            } else {
                D.Warn("Disconnecting {0} and {1}", node1, node2);
            #endif
            }
            #if DEBUG_GRID
            D.Warn("Removing connection: {0}", conn);
            #endif

            if (node1.IsInitialized) {
                node1.NumConnections--;
                if (node1.NumConnections < 0) {
                    D.Err("NumConnections went into negative! {0}. Conn: {1}",
                        node1, conn);
                    node1.NumConnections = 0;
                }
                if (node1.NumConnections == 0) {
                    node1.Consumed = 0f;
                    node1.Produced = 0f;
                    node1.Deficit = 0f;
                }
                node1.NeedsSync = true;
                arrNodes.Update(node1.Idx, node1);
            } else {
                D.Err("Removing a connection where node1 was not initialized! {0}",
                    conn);
            }
            if (node2.IsInitialized) {
                node2.NumConnections--;
                if (node2.NumConnections < 0) {
                    D.Err("NumConnections went into negative! {0}. Conn: {1}",
                        node2, conn);
                    node2.NumConnections = 0;
                }
                if (node2.NumConnections == 0) {
                    node2.Consumed = 0f;
                    node2.Produced = 0f;
                    node2.Deficit = 0f;
                }
                node2.NeedsSync = true;
                arrNodes.Update(node2.Idx, node2);
            } else {
                D.Err("Removing a connection where node2 was not initialized! {0}",
                    conn);
            }
            needsGridRebuild = true;
            if (!isBulkRemoval) {
                UI.RefreshVFX();
            }
            S.Sig.EnergyConnectionChanged.Send(
                node1.EntityId, node2.EntityId, false);
        }

        public bool AreConnected(int entityId1, int entityId2) {
            var idTuple = Maths.OrderedTuple(entityId1, entityId2);
            return existingConnections.Contains(idTuple);
        }

        private EnergySysData data;
        public int DisconnectedCount;
        public int ConnectedCount;

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Energy ??= data;
            data.TotalMem = TotalMem;
            data.TotalCPU = TotalCPU;
            data.TotalGPU = TotalGPU;
            data.TotalDisk = TotalDisk;
            sd.Energy.Nodes = arrNodes.SaveData(out sd.Energy.NodesCount,
                out sd.Energy.NodesRecycler);
            sd.Energy.Connections = arrConnections.SaveData(out sd.Energy.ConnectionsCount,
                out sd.Energy.ConnectionsRecycler);
            sd.Energy.Grids = new ElectricGrid[GridsCount];
            for (int i = GridsCount - 1; i >= 0; i--) {
                sd.Energy.Grids[i] = grids[i];
            }
            sd.Energy.GridsCount = GridsCount;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd.Energy ?? new();
            data.StateHistory ??= new();
        }

        public ElectricNodeComp GetCompByIdx(int idx) {
            if (nodeComponents.TryGetValue(idx, out var comp)) {
                return comp;
            }
            return null;
        }

        // For components writing their own settings. Connections belong to this system and the
        // component's copy of them can lag behind (see CatchUp), so its count is never written back
        public void Update(in ElectricNodeComp c, in ElectricNode node) {
            if (!CanStore(c, node)) { return; }
            var merged = node;
            if (arrNodes.TryGet(node.Idx, out var current)) {
                merged.NumConnections = current.NumConnections;
            }
            Store(c, in merged);
        }

        private void Store(in ElectricNodeComp c, in ElectricNode node) {
            if (!CanStore(c, node)) { return; }
            var stored = node;
            // A pending sync stays pending, or the component never learns what changed
            if (arrNodes.TryGet(node.Idx, out var current)) {
                stored.NeedsSync = node.NeedsSync || current.NeedsSync;
            }
            arrNodes.Update(node.Idx, stored);
        }

        private bool CanStore(in ElectricNodeComp c, in ElectricNode node) {
            if (!c.Entity.IsActive) {
                D.Warn("Refusing to update electric node for inactive entity! {0}->{1}",
                    c, node);
                return false;
            }
            if (node.EntityId != c.EntityId) {
                D.Err("Refusing to update electric node for: {0}->{1}. Entity id mismatch!",
                    c, node);
                return false;
            }
            if (c.SlotIdx != node.Idx) {
                D.Err("Refusing to update electric node for: {0}->{1}. Node Idx mismatch!",
                    c, node);
                return false;
            }
            return true;
        }

        public void EnqueueRebuild() {
            needsGridRebuild = true;
        }

        public bool IsGridProducing(int gridId) {
            if (gridId == -1) { return false; }
            if (GridsCount == 0) {
                D.Err("Trying to check if grid is producing when grids count is 0!");
                return false;
            }
            return grids[gridId].MaxOutput > 0f;
        }

        public void UpdateAfterWinch(ElectricNodeComp c) {
            var entityId = c.EntityId;
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                if (conn.Connection.Item1 == entityId
                        || conn.Connection.Item2 == entityId) {
                    CheckConnectionAfterWinch(conn);
                }
            }
        }
        public bool CheckConnectionAfterWinch(ElectricConnection conn) {
            var node1 = arrNodes.Array[conn.NodeIdxs.Item1];
            var node2 = arrNodes.Array[conn.NodeIdxs.Item2];
            var maxRange = Maths.Max(node1.ConnectorRangeSqr, node2.ConnectorRangeSqr);
            // Only a link the move stretched past the range breaks. A link that was already
            // longer (made before a connector's range was cut) stays until it is relinked, and
            // a recenter moves the whole ship without stretching anything
            var newLength = Vectors.SqrDistance(node1.Position, node2.Position);
            var oldLength = Vectors.SqrDistance(conn.Pos1, conn.Pos2);
            if (newLength > maxRange && newLength > oldLength + 0.01f) {
                // RemoveConnection leaves the cache to its callers. A stale entry would keep
                // the pair "already connected" for the rest of the session
                existingConnections.Remove(conn.Connection);
                RemoveConnection(ref node1, ref node2, conn.Idx, true);
                return false;
            }
            if (node1.Position != conn.Pos1) {
                conn.Pos1 = node1.Position;
            }
            if (node2.Position != conn.Pos2) {
                conn.Pos2 = node2.Position;
            }
            arrConnections.Update(conn.Idx, conn);
            return true;
        }

        public int ConnectionsFor(int entityId, int[] connectedIds) {
            int found = 0;
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (conn.Connection.Item1 == entityId) {
                    connectedIds[found++] = conn.Connection.Item2;
                    if (found >= connectedIds.Length) {
                        return found;
                    }
                } else if (conn.Connection.Item2 == entityId) {
                    connectedIds[found++] = conn.Connection.Item1;
                    if (found >= connectedIds.Length) {
                        return found;
                    }
                }
            }
            return found;
        }
        public ElectricGrid GetGrid(int gridId) {
            return grids[gridId];
        }

        public bool TryGetGrid(int gridId, out ElectricGrid grid) {
            if (gridId > -1 && GridsCount > gridId) {
                grid = grids[gridId];
                return true;
            }
            grid = default;
            return false;
        }

        public void DisconnectGridConnections(int gridIdx) {
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (conn.GridId == gridIdx && conn.IsInitialized) {
                    var node1 = arrNodes.Array[conn.NodeIdxs.Item1];
                    var node2 = arrNodes.Array[conn.NodeIdxs.Item2];
                    if (!existingConnections.Remove(conn.Connection)) {
                        D.Err("Connection was not in existing connections! {0}",
                            conn);
                    } else {
                        RemoveConnection(ref node1, ref node2, i, true);
                    }
                }
            }
            RebuildGrid(false);
            UI.RefreshVFX();
        }

        public void TryRelocateConnections(ElectricNodeComp from, ElectricNodeComp to) {
            if (to.Entity.IsActive && !to.IsRegistered) {
                AddNode(to, true);
                RebuildGrid(false);
            }
            for (int i = arrConnections.Count - 1; i >= 0; i--) {
                var conn = arrConnections.Array[i];
                if (!conn.IsInitialized) { continue; }
                bool found = false;
                ElectricNodeComp prev = null;
                if (conn.Connection.Item1 == from.EntityId || conn.Connection.Item2 == from.EntityId) {
                    if (nodeComponents.TryGetValue(conn.NodeIdxs.Item2, out prev)) {
                        if (prev != from) { found = true; }
                    }
                    if (!found && nodeComponents.TryGetValue(conn.NodeIdxs.Item1, out prev)) {
                        if (prev != from) { found = true; }
                    }
                }
                if (found) {
                    CatchUp(from);
                    CatchUp(prev);
                    var prevWasConnected = prev.IsConnected;
                    // In place on purpose: announcing the drop on "from" would make a relocated
                    // Ship Computer raise its alert on the entity that is going away
                    if (!TryDisconnect(ref from.Node, ref prev.Node, out var failR)) {
                        D.Warn("Could not disconnect nodes when relocating connections: {0}. Disconnecting from {1}, Old connection other end: {2}. From: {3}, prev: {4}", failR,
                            from.Entity, prev.Entity, from.Node, prev.Node);
                    }
                    if (!TryConnect(prev, to, out failR)) {
                        D.Warn("Could not reconnect nodes when relocating connections: {0}. New device: {1}, Prev connection: {2}. New: {3}, Prev: {4}",
                            failR, to.Entity, prev.Entity, to.Node, prev.Node);
                    }
                    // Left unlinked when the new device is out of reach or full
                    TransmitIfConnectionFlipped(prev, prevWasConnected);
                }
            }
        }

    }
}
