#if !PRODUCTION_BUILD
// #define FAST_WAIT
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Input;
using Game.Systems.Space;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    public sealed class HyperjumpQueue : ISaveableChild {
        private readonly GameState S;
        private readonly ShipNavSys shipNav;
        public readonly LinkedList<ISpaceEntity> Queue = new();
        public readonly BCallback OnChange = new();
        public string QueueLayer;
        private SpaceObject targetBeacon;
        public bool HasTarget => warpTarget != null || Queue.Count > 0;
        private ISpaceEntity warpTarget;
        public ISpaceEntity WarpTarget => warpTarget;
        public Entity Entity => null;
        private WPopupData stopChoice;
        public readonly BBool VisitWaypoints = new();
        public readonly HyperjumpProgress Progress;
        public readonly BBool IsPaused = new();
        public bool IsRecharging;
        public long PausedUntil;
        public struct OpResult {
            public static OpResult Success = new OpResult { IsSuccess = true };
            public static OpResult Forbidden = new OpResult { IsSuccess = false, FailReason = T.OperationForbidden };
            public bool IsSuccess;
            public string FailReason;
        }

        public HyperjumpQueue(GameState s, ShipNavSys sys) {
            S = s;
            shipNav = sys;
            Progress = new HyperjumpProgress();
            S.Query.GetHyperjumpQueue.SetResponder(() => this);
            S.Query.GetWarpTarget.SetResponder(() => warpTarget);
            S.Query.GetHyperjumpProgress.SetResponder(() => Progress);
            S.Query.SetWarpTarget.SetResponder(SetWarpTarget);
            S.Query.JumpToReturnBeacon.SetResponder(JumpToReturnBeacon);
            S.Query.AddToHyperjumpQueue.SetResponder(Enqueue);
            S.Query.RemoveFromHyperjumpQueue.SetResponder(Dequeue);
            S.Query.GetHyperjumpPathPreviewFor.SetResponder(PreviewFor);
            S.Query.GetNextHyperjumpTarget.SetResponder(GetNextJumpTarget);
            S.Sig.HyperspaceLocationChanged.AddListener(OnMoveInHyperspace);
        }

        private void OnMoveInHyperspace(SpaceObject from, SpaceObject to) {
            if (targetBeacon == null && QueueLayer == UniverseLayer.Universe) {
                // inbound region?
                if (to.Layer != QueueLayer) {
                    to = to.Parent;
                }
            }
            bool needsRedraw = false;
            if (to.SpaceEntity == warpTarget) {
                warpTarget = null;
                shipNav.ExitHyperspace(null);
                if (targetBeacon != null) {
                    A.Player.Location = targetBeacon.Location +
                        Rng.UInsideUnitCircle().normalized * Rng.URange(1f, 2f);
                    targetBeacon = null;
                }
                S.Sys.Universe.ShowInStarmap(A.Player);
            } else if (Queue.Count > 0) {
                if (Queue.First.Value == to.SpaceEntity) {
                    // expected
                    Queue.RemoveFirst();
                    needsRedraw = true;
                } else {
                    D.Err("Moved in hyperspace to unexpected location, not in queue! {0} -> {1} | q: {2}",
                        from, to, D.DumpCollection(Queue));
                }
            }
            S.Sig.HyperjumpQueueChanged.Send(this, needsRedraw);
            OnChange.Invoke();
        }

        public ISpaceEntity GetNextJumpTarget() {
            if (warpTarget != null) {
                return warpTarget;
            }
            return Queue.First?.Value;
        }

        /// <summary>
        /// Resolves how many drives are currently charged and ready for the next hop
        /// (<paramref name="ready"/>) and how many OnTick wants charged before resuming a
        /// paused/recharging queue (<paramref name="wanted"/>). Single source of truth for
        /// both the OnTick recharge gate and the hyperjump widget tooltip, so they can never
        /// disagree. A single hop, a relay hop (which deactivates after one jump anyway), or
        /// Visit Waypoints (which exits hyperspace at every stop regardless) only ever want 1
        /// charge; a longer batched journey wants enough drives to finish it in one hyperspace
        /// session, capped at what the ship can actually charge so a dead drive can't stall it
        /// forever.
        /// </summary>
        public void GetChargeState(out int ready, out int wanted) {
            var nextJumpTarget = Progress.To ?? GetNextJumpTarget();
            if (nextJumpTarget == null) {
                ready = 0;
                wanted = 0;
                return;
            }
            var lState = shipNav.HyperspaceStateForLayer(SOUtils.JumpLayerFor(nextJumpTarget.SO));
            bool isRelayHop = false;
            if (nextJumpTarget is SpaceRegion &&
                    S.Universe.IsUsingHyperspaceRelay &&
                    SpaceLink.ExistsFor(nextJumpTarget, A.PlayerSector.Parent.SpaceEntity)) {
                // eligible for a relay jump, we'll use local state
                lState = shipNav.HyperspaceStateFor(A.PlayerSector.Parent);
                isRelayHop = true;
            }
            bool isWarp = nextJumpTarget == warpTarget && targetBeacon == null;
            ready = isWarp ? lState.Warps : lState.Jumps;
            if (isWarp || isRelayHop || VisitWaypoints || Queue.Count <= 1) {
                wanted = 1;
            } else {
                wanted = Math.Min(Queue.Count, Math.Max(1, lState.EnginesChargeable));
            }
        }

        private OpResult JumpToReturnBeacon(SpaceObject beacon) {
            if (A.IsInHyperspace) {
                return OpResult.Forbidden;
            }
            var layer = SOUtils.JumpLayerFor(beacon);
            if (!shipNav.HasEngineForLayer(layer, out var failReason)) {
                return new OpResult {
                    IsSuccess = false,
                    FailReason = failReason,
                };
            }
            QueueLayer = layer;
            warpTarget = beacon.Parent.SpaceEntity;
            targetBeacon = beacon;
            Queue.Clear();
            SetPaused(false);
            S.Sig.HyperjumpQueueChanged.Send(this, true);
            OnChange.Invoke();
            return OpResult.Success;
        }

        /// <summary>
        /// Cancels any pending hyperjump, whether queued waypoints or a direct warp target.
        /// </summary>
        public void CancelAll() {
            warpTarget = null;
            targetBeacon = null;
            Queue.Clear();
            IsRecharging = false;
            PausedUntil = 0;
            SetPaused(false);
            shipNav.SetWantHyperspace(false);
            S.Sig.HyperjumpQueueChanged.Send(this, true);
            OnChange.Invoke();
        }
        private OpResult SetWarpTarget(ISpaceEntity wt) {
            if (wt == null) {
                return new OpResult {
                    IsSuccess = false,
                    FailReason = "Null target",
                };
            }
            if (Progress.IsMoving) {
                return OpResult.Forbidden;
            }
            var layer = SOUtils.JumpLayerFor(wt.SO);
            if (!A.InstantBuild && shipNav.HyperspaceStateForLayer(layer).EnginesWarp == 0) {
                // e.g. a Local Warp Drive can't reach another region
                return new OpResult {
                    IsSuccess = false,
                    FailReason = T.HyperspaceMissingEngine,
                };
            }
            warpTarget = wt;
            QueueLayer = layer;
            Queue.Clear();
            SetPaused(false);
            S.Sig.HyperjumpQueueChanged.Send(this, true);
            OnChange.Invoke();
            return OpResult.Success;
        }

        private LinkedList<ISpaceEntity> PreviewFor(ISpaceEntity so) {
            ISpaceEntity last = Queue.Last?.Value;
            if (so is SpaceRegion) {
                if (last is not SpaceRegion) {
                    last = A.PlayerSector.Parent.SpaceEntity;
                }
            } else {
                if (last is not SpaceSector) {
                    last = A.PlayerSector.SpaceEntity;
                }
            }
            var path = ShortestPath(last, so);
            return new LinkedList<ISpaceEntity>(path);
        }

        public OpResult Dequeue(ISpaceEntity so) {
            if (Progress.IsMoving && so == Progress.ActualTo || so == Progress.To) {
                return OpResult.Forbidden;
            }
            var next = Queue.First;
            bool found = false;
            while (next != null) {
                var cur = next;
                next = next.Next;
                if (found || so == cur.Value) {
                    found = true;
                    Queue.Remove(cur);
                }
            }
            if (!found) {
                return new OpResult {
                    IsSuccess = false,
                    FailReason = T.AdRejTargetUnavailable,
                };
            }
            if (Queue.Count == 0) {
                SetPaused(false);
                if (warpTarget == null && !A.IsInHyperspace) {
                    // Removing the last hop cancels the jump, the same as Clear All. Left
                    // set, the ship kept preparing and jumped with nowhere to go (STA-1783).
                    // In hyperspace OnTick finds no next target and leaves.
                    shipNav.SetWantHyperspace(false);
                }
            }
            S.Sig.HyperjumpQueueChanged.Send(this, true);
            OnChange.Invoke();
            return OpResult.Success;
        }

        public OpResult Enqueue(ISpaceEntity so) {
            ISpaceEntity last = A.PlayerSector.SpaceEntity;
            bool isRegion = false;
            if (so is SpaceRegion) {
                isRegion = true;
                last = last.SO.Parent.SpaceEntity;
            }
            if (Progress.IsMoving && (warpTarget != null || QueueLayer != so.SO.Parent.Layer)) {
                return new OpResult {
                    IsSuccess = false,
                    FailReason = $"{T.NounHyperjump}: {T.GoalStateInprogress}"
                };
            }
            if (Queue.Count > 0 && QueueLayer == so.SO.Parent.Layer) {
                last = Queue.Last.Value;
                if (last == so) {
                    return new OpResult {
                        IsSuccess = false,
                        FailReason = null,
                    };
                }
                if (Queue.Contains(so)) {
                    return new OpResult {
                        IsSuccess = false,
                        FailReason = null,
                    };
                }
            }
            if (isRegion) {
                if (QueueLayer != UniverseLayer.Universe) {
                    QueueLayer = UniverseLayer.Universe;
                    Queue.Clear();
                }
            } else {
                if (QueueLayer != UniverseLayer.Region) {
                    QueueLayer = UniverseLayer.Region;
                    Queue.Clear();
                }
            }
            if (SpaceLink.ExistsFor(so, last)) {
                // adding a node that is reachable for last
                Queue.AddLast(so);
            } else {
                var path = ShortestPath(last, so);
                foreach (var p in path) {
                    Queue.AddLast(p);
                }
            }
            warpTarget = null;
            targetBeacon = null;
            A.S.Sig.HyperjumpQueueChanged.Send(this, true);
            OnChange.Invoke();
            return OpResult.Success;
        }
        // Heuristic function - straight line distance to target
        private float Heuristic(ISpaceEntity entity, ISpaceEntity to) {
            var dist = Vectors.SqrDistance(entity.Position, to.Position);
            if (entity == A.PlayerSector.SpaceEntity || entity == A.PlayerSector.Parent.SpaceEntity) {
                dist *= 100000f;
            } else if (Queue.Contains(entity)) {
                dist *= 100000f;
            }
            return dist;
        }

        private List<ISpaceEntity> ShortestPath(ISpaceEntity from, ISpaceEntity to) {
            // A* algorithm implementation for small graphs
            var gScore = new Dictionary<ISpaceEntity, float>(); // Cost from start to node
            var fScore = new Dictionary<ISpaceEntity, float>(); // gScore + heuristic
            var previous = new Dictionary<ISpaceEntity, ISpaceEntity>();
            var openSet = new SortedDictionary<float, List<ISpaceEntity>>();
            var closedSet = new HashSet<ISpaceEntity>();


            // Initialize starting node
            gScore[from] = 0f;
            fScore[from] = Heuristic(from, to);

            // Add starting node to open set
            if (!openSet.ContainsKey(fScore[from])) {
                openSet[fScore[from]] = new List<ISpaceEntity>();
            }
            openSet[fScore[from]].Add(from);

            while (openSet.Count > 0) {
                // Get node with lowest fScore
                var lowestF = openSet.Keys.First();
                var entitiesAtF = openSet[lowestF];
                var current = entitiesAtF[0];
                entitiesAtF.RemoveAt(0);

                if (entitiesAtF.Count == 0) {
                    openSet.Remove(lowestF);
                }

                // Found target
                if (current == to) {
                    // Reconstruct path (excluding the starting entity since it's already in the queue)
                    var path = new List<ISpaceEntity>();
                    var pathEntity = to;

                    while (previous.ContainsKey(pathEntity)) {
                        path.Insert(0, pathEntity);
                        pathEntity = previous[pathEntity];
                    }

                    return path;
                }

                closedSet.Add(current);

                // Check all neighbors
                foreach (var link in current.Links) {
                    var neighbor = link.OtherSideOf(current);
                    if (neighbor == null || closedSet.Contains(neighbor)) continue;

                    var tentativeGScore = gScore[current] + link.Length;

                    // If neighbor not in gScore, it hasn't been visited yet
                    if (!gScore.ContainsKey(neighbor)) {
                        gScore[neighbor] = float.MaxValue;
                        fScore[neighbor] = float.MaxValue;
                    }

                    // If this path to neighbor is better than any previous one
                    if (tentativeGScore < gScore[neighbor]) {
                        previous[neighbor] = current;
                        gScore[neighbor] = tentativeGScore;
                        fScore[neighbor] = tentativeGScore + Heuristic(neighbor, to);

                        // Add to open set if not already there
                        var neighborF = fScore[neighbor];
                        if (!openSet.ContainsKey(neighborF)) {
                            openSet[neighborF] = new List<ISpaceEntity>();
                        }
                        if (!openSet[neighborF].Contains(neighbor)) {
                            openSet[neighborF].Add(neighbor);
                        }
                    }
                }
            }

            // No path found
            return new List<ISpaceEntity>();
        }

        public void OnTick(long ticks, HyperspaceState hsState) {
            if (IsPaused) {
                if (PausedUntil > 0 && PausedUntil < ticks) {
                    SetPaused(false);
                    PausedUntil = 0;
                }
                return;
            }
            var nextJumpTarget = Progress.To ?? GetNextJumpTarget();
            if (hsState.IsInHyperspace) {
                if (Progress.IsMoving && Progress.EndTick <= ticks) {
                    // arrived
                    CompleteMoveTo(ticks, Progress.ActualTo);
                    return;
                }
                if (nextJumpTarget != null) {
                    string failReason;
                    var hsLayer = SOUtils.JumpLayerFor(nextJumpTarget.SO);
                    if (Progress.IsMoving) {
                        var canStayInHS = shipNav.CanStayInHyperspace(
                            hsLayer, out failReason);
                        if (canStayInHS) {
                            Progress.UpdateProgress(ticks);
                            // S.Sig.HyperjumpQueueChanged.Send(this, false);
                        } else {
                            // abort
                            Progress.Clear();
                            var msg = $"{T.HyperspaceWarpedBack}: {A.PlayerSector.StarmapLink}";
                            msg = $"{msg}<br><br>{Texts.Accented(T.Warning)}: {T.HyperspaceSolarWarning}";
                            WPopup.Create(false, IconId.CWarning, T.HyperjumpFailed, failReason, msg)
                                .Unique("HyperFailPopup")
                                .WithAction(T.ReadyToRetry, () => {
                                    SetPaused(false);
                                })
                                .WithAction2(T.Close, null)
                                .Spawn(center: true);
                            shipNav.SetWantHyperspace(false);
                            SetPaused(true);
                        }
                        return;
                    }
                    var canMove = shipNav.CanMoveInHyperspace(false, hsLayer, out _);
                    if (canMove) {
                        var isWarp = warpTarget == nextJumpTarget;
                        if (targetBeacon != null && warpTarget == targetBeacon.Parent.SpaceEntity) {
                            isWarp = false;
                        }
                        if (!shipNav.TryMoveInHyperspace(hsLayer,
                                isWarp, out failReason)) {

                            D.Err("Expected to be able to move in hyperspace, but couldn't: {0}", failReason);
                            WPopup.Create(false, IconId.CWarning,
                                    T.HyperjumpFailed, failReason)
                                .Unique("HyperFailPopup")
                                .WithAction(T.ReadyToRetry, () => {
                                    SetPaused(false);
                                })
                                .WithAction2(T.Close, null)
                                .Spawn(center: true);
                            shipNav.SetWantHyperspace(false);
                            SetPaused(true);
                            return;
                        }
                        Progress.Start(hsLayer, A.PlayerSector.SpaceEntity, nextJumpTarget, ticks);
                        return;
                    }
                } else {
                    D.Warn("In hyperspace but no next jump target, leaving");
                    shipNav.SetWantHyperspace(false);
                }
            } else {
                // not in hyperspace
                if (Progress.IsMoving) {
                    D.Err("Not in hyperspace, cancelling hyperjump progress! {0}", Progress);
                    Progress.Clear();
                }
            }

            if (nextJumpTarget == null) {
                return;
            }
            var jumpLayer = SOUtils.JumpLayerFor(nextJumpTarget.SO);
            if (warpTarget != null) {
                // The ship may have changed region since the target was set (e.g. a black
                // hole), keep the layer the hyperjump widget checks engines for current
                QueueLayer = jumpLayer;
            }
            if (!shipNav.CanEnterHyperspaceNow(false, out _, jumpLayer)) {
                return;
            }
            // we have a target, but we are not moving, let's see if we can begin
            GetChargeState(out var charges, out var chargesWanted);
            if (IsRecharging) {
                if (charges < chargesWanted) {
                    // still recharging;
                    return;
                }
                IsRecharging = false;
            }

            if (charges < 1) {
                IsRecharging = true;
                return;
            }

            if (!hsState.WantsHyperspace) {
                // D.Warn("Wants hyperspace since can jump to next target: {0}", nextJumpTarget);
                shipNav.SetWantHyperspace(true);
                return;
            }
        }

        private void CompleteMoveTo(long ticks, ISpaceEntity to) {
            D.Warn("Completing hs move to: {0}", to);
            var took = Progress.EndTick - Progress.StartTick;
            var duration = Units.TicksToTime(took);
            var msg = T.FlightArrived_V2.F(to.SO.StarmapLink, duration);
            S.Sys.Log.AddLine(msg);
            msg = T.ArrivedAt_V1.F(to.SO.StarmapLink);
            if (to == warpTarget || Queue.Count < 2) {
                S.Sig.ShowFanfare.Send(Texts.Green(msg));
                UISounds.Play("FX/DingDingDing", 0.5f);
            }
            Progress.Clear();
            if (to == warpTarget) {
                ExecuteMove(to.SO);
                return;
            }
            ExecuteMove(to.SO);
            if (Queue.Count == 0) {
                shipNav.SetWantHyperspace(false);
                return;
            }
            UISounds.Play("FX/Ding", vol: 0.5f);

            var canMove = shipNav.CanMoveInHyperspace(false,
                Queue.First.Value.SO.Parent.Layer, out var failReason);

            msg = $"{msg}. {T.NextStop}: {Queue.First.Value.SO.StarmapLink}";
            if (canMove) {
                if (VisitWaypoints) {
                    S.Sig.ShowFanfare.Send(Texts.Green(msg));
                    shipNav.SetWantHyperspace(false);
                    SetPaused(true);
                    return;
                }
                float delayHours = Tunables.HyperjumpNextWaypointDelayHours;
                #if FAST_WAIT
                delayHours = 0.1f;
                #endif
                PausedUntil = ticks + (int) (Consts.TicksPerHour * delayHours);

                S.Sig.ShowFanfare.Send(Texts.Green(msg));
                SetPaused(true);
            } else {
                shipNav.SetWantHyperspace(false);
                IsRecharging = true;
                if (stopChoice != null) {
                    stopChoice.Despawn();
                }
                stopChoice = WPopup.Create(false, IconId.CHyperspaceEnter,
                    T.HyperdrivesExhausted, T.ExitHyperspaceToRecharge, msg)
                    .WithAction(T.VerbReenter, () => SetPaused(false))
                    .WithAction2(T.VerbStay, () => IsRecharging = false)
                        .Spawn(center: true);
                SetPaused(true);
            }

            S.UIQuery.RebuildStarmap.Ask(false, !UIShowing.Starmap);
        }

        public void SetPaused(bool paused) {
            if (IsPaused != paused) {
                IsPaused.Set(paused);
                // S.Sig.HyperjumpQueueChanged.Send(this, false);
                if (paused && !A.IsInHyperspace) {
                    // Not just bookkeeping: while outside hyperspace, WantsHyperspace is what
                    // hails/keeps the bridge controls operator (ShipSys.NeedsOperator) and drives
                    // CheckEnterHyperspace. If we left it set, a jump could still start while the
                    // queue reports paused. Gated to outside hyperspace because SetPaused(true) is
                    // also used mid-flight (waypoint holds, recharge waits) where WantsHyperspace
                    // must stay true to keep the ship in hyperspace between hops.
                    shipNav.SetWantHyperspace(false, silent: true);
                }
            }
        }

        public void ExecuteMove(SpaceObject to) {
            var originalTo = to;
            var prevLoc = S.Universe.HyperspaceLocation ?? A.PlayerSector;
            #if PRODUCTION_BUILD
            D.Warn("Moving in hyperspace: {0}->{1}", prevLoc, to);
            #endif
            if (to.Region != null) {
                if (!to.IsVisitedHS) {
                    to.Flags |= SOFlags.VisitedHS;
                    to.UI?.Refresh();
                }
                if (to.Children == null) {
                    D.Err("Children is null when moving in hyperspace to region {0}. can't set", to);
                    return;
                }
                D.Warn("Jumping into inbound sector of region: {0} ({1})", to,
                    to.Region.InboundSector);
                to = to.Region.InboundSector.SO;
            }
            to.Flags |= SOFlags.VisitedHS;
            S.Universe.Player.Location = to.Location;
            S.Universe.HyperspaceLocation = to;
            S.Situation.Difficulty = to?.SpaceEntity?.Biome?.Difficulty ?? 0f;
            var hasRadar = S.Sys.Sec.IsRadarActive() || A.InstantBuild;
            if (S.Universe.IsUsingHyperspaceRelay) {
                S.Universe.CurrentRelay = null;
                ExecuteHyperRelayTo(to, hasRadar);
            }
            // shipNav.UpdateHyperspaceUI(true);

            if (originalTo.Sector != null) {
                SOUtils.ScanWithRadar(S, to, hasRadar);
                // S.Sys.Scan.OnHyperspaceAction(0.1f);
            } else if (originalTo.Region != null) {
                // If we were scanning for a resource, we will reduce the scan progress by half
                // S.Sys.Scan.OnHyperspaceAction(0.5f);
            }

            S.Sig.HyperspaceLocationChanged.Send(prevLoc, to);
            to.UI?.Refresh();

            // RebuildStarmap(refocus: false);
        }
        private void ExecuteHyperRelayTo(SpaceObject to, bool hasRadar) {
            S.Sys.Universe.GenerateSector(to, false);
            if (hasRadar) {
                ShipSys.FullScan(to);
                SOUtils.ScanNeighbors(S, to, hasRadar: hasRadar);
            }
        }

        public void OnSave(ComponentData data) {
            data.SetBool("IsPaused", IsPaused);
            data.SetBool("IsRecharging", IsRecharging);
            data.SetBool("VisitWaypoints", VisitWaypoints);
            data.SetLong("PausedUntil", PausedUntil);
            data.SetString("QueueLayer", QueueLayer);
            // By space object id: ISpaceEntity.Id is the sector / region id, a separate id
            // space that TryFind would resolve to an unrelated space object on load
            data.SetInt("WarpTargetSO", warpTarget?.SO.Id ?? 0);
            data.SetInt("TargetBeacon", targetBeacon?.Id ?? 0);
            if (Queue.Count > 0) {
                data.SetIntSet("Queue", Queue.Select(i => i.SO.Id).ToArray());
            } else {
                data.SetIntSet("Queue", null);
            }
            Progress.OnSave(data);
        }

        public void OnLoad(ComponentData data) {
            IsPaused.Set(data.GetBool("IsPaused", false));
            IsRecharging = data.GetBool("IsRecharging", false);
            VisitWaypoints.Set(data.GetBool("VisitWaypoints", false));
            PausedUntil = data.GetLong("PausedUntil", 0);
            QueueLayer = data.GetString("QueueLayer", null);

            // Legacy "WarpTarget" held a sector / region id and is deliberately ignored
            var warpTargetId = data.GetInt("WarpTargetSO", 0);
            if (warpTargetId != 0) {
                if (A.S.Universe.TryFind(warpTargetId, out var wt)) {
                    warpTarget = wt.SpaceEntity;
                } else {
                    D.Err("Failed to load HyperjumpQueue.WarpTarget by id: {0}", warpTargetId);
                }
            }

            var targetBeaconId = data.GetInt("TargetBeacon", 0);
            if (targetBeaconId != 0) {
                if (A.S.Universe.TryFind(targetBeaconId, out var tb)) {
                    targetBeacon = tb;
                } else {
                    D.Err("Failed to load HyperjumpQueue.TargetBeacon by id: {0}", targetBeaconId);
                }
            }

            var queueIds = data.GetIntSet("Queue", null);
            if (queueIds != null) {
                Queue.Clear();
                foreach (var id in queueIds) {
                    if (A.S.Universe.TryFind(id, out var so)) {
                        Queue.AddLast(so.SpaceEntity);
                    } else {
                        D.Err("Failed to load HyperjumpQueue item by id: {0}", id);
                    }
                }
            }

            Progress.OnLoad(data);
        }

        public string GetName() {
            return "HyperjumpQueue";
        }

    }
}
