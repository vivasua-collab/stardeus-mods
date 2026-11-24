#if !PRODUCTION_BUILD
//#define DEBUG_WINCH
#endif
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Procgen;
using Game.UI;
using Game.Systems.UIUX;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using KL.Clock;
using Game.Tools;
using Game.CodeGen;

namespace Game.Commands {
    public abstract class CmdMoveSectionBase : AsyncCommand {
        protected CmdMoveSectionBase(string id) : base(id, unique: true) { }
        protected abstract bool isInitiatedByPlayer { get; set; }
        protected int anchor;
        protected int targetPos;
        protected virtual bool FocusCamera => true;
        protected virtual bool Instant => false;
        // Winch and push ram through built floors in their path; false makes them an obstacle
        protected virtual bool BreaksFloorsInPath => true;
        public bool IsGenStage;
        #if DEBUG_WINCH
        private bool _hasFailedMoving;
        protected bool hasFailedMoving {
            get => _hasFailedMoving;
            set => D.SetFieldWithDebug(ref _hasFailedMoving, value, "HasFailedMoving");
        }
        #else
        protected bool hasFailedMoving;
        #endif
        public int StepsMade;
        public bool IsDone;
        protected int startIslandCount;
        protected int endIslandCount;
        protected int islandSizeBeforeMove;
        private int startCountWithinMargin;
        public bool CaptureCollision;
        public Vector2 LastMotion;
        public List<int> LastCollision;
        public HashSet<int> LastIsland;
        public ShipSpec ShipSpec;
        public Vector2 ExecutedMotion = Consts.V2Zero;
        private readonly Dictionary<int, float> oxyTemp = new();
        private readonly Dictionary<int, float> heatTemp = new();
        // Floors
        private readonly Dictionary<int, Tile> fTemp = new();
        // Walls
        private readonly Dictionary<int, Tile> wTemp = new();
        // Objects
        private readonly Dictionary<int, Tile> oTemp = new();
        // Decals
        private readonly Dictionary<int, Tile> dTemp = new();
        // Decals Above
        private readonly Dictionary<int, Tile> daTemp = new();
        private readonly Dictionary<int, Color> tintFloorTemp = new();
        private readonly Dictionary<int, Color> tintWallTemp = new();
        private readonly Dictionary<int, List<Obj>> objTemp = new();
        private readonly Dictionary<int, List<Being>> bTemp = new();
        private readonly Dictionary<int, List<DirtData>> dirtTemp = new();
        private readonly List<ElectricNodeComp> connectors = new();
        private readonly HashSet<int> invincibleTemp = new();

        public override void OnForbidden(GameState s) {
            UISounds.PlayActionDenied();
            s.Query.CameraHideLink.Ask();
        }

        protected virtual int GetTarget() {
            return targetPos;
        }

        protected void GetDirection(out int dx, out int dy) {
            if (Instant) {
                Pos.Delta(anchor, GetTarget(), out dx, out dy);
            } else {
                Pos.Direction(anchor, GetTarget(), out dx, out dy);
            }
        }

        protected virtual IEnumerator PrepareForMove() {
            yield break;
        }
        protected abstract HashSet<int> GetIslandAndMoveDir(out int islandId,
            out int x, out int y);
        protected virtual int GetIslandId() {
            return S.Sys.Areas.IslandAt(anchor);
        }

        private static readonly int moveLock = Hashes.S("Winch");
        public override IEnumerator ExecuteAsync() {
            int waitAttempts = 1000;
            var areas = S.Sys.Areas;
            if (!GameState.IsCurrent(S)) {
                D.Err("Game state is not current in winch! {0}", S);
            }
            if (anchor < 1 || Pos.IsOOB(anchor)) {
                D.Err("Cannot use anchor that is non-positive or OOB! {0}",
                    Pos.StringD(anchor));
                yield break;
            }
            S.Clock.ForceStop(ForceStopReason.BulkOperation);
            while (!Ready.AtmoO2 || !Ready.AtmoTemp || !Ready.Areas) {
                yield return null;
                if (G.ClockSpeed > ClockSpeed.Fast2) {
                    break;
                }
                if (!GameState.IsCurrent(S)) { yield break; }
                if (waitAttempts-- < 0) {
                    D.Err("Could not wait for atmo, temp or areas to get ready!");
                    yield break;
                }
            }
            while (!S.Clock.TryLock(moveLock, "Winch") && waitAttempts-- > 0) {
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
            }
            S.SetWinchStarted();
            yield return PrepareForMove();
            if (!GameState.IsCurrent(S)) { yield break; }
            if (!IsGenStage) {
                if (UIShowing.Overlay) {
                    S.Sig.HideOverlay.Send();
                }
                #if !PRODUCTION_BUILD
                if (!The.TestsAreRunning) {
                    S.UIQuery.ToggleGameUI.Ask(false, false);
                }
                #else
                S.UIQuery.ToggleGameUI.Ask(false, false);
                #endif
                Ready.WaitForSaveFull(this);
                var ticks = G.Ticks;
                S.LastWinchTick = ticks;
                //S.Clock.SetSpeed(ClockSpeed.Normal);
            }
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }
            S.Sig.ForceAreas.Send();
            startIslandCount = areas.IslandCount;
            if (!IsGenStage) {
                Ready.SetGen(false, "Winching");
                Ready.SetSave(false, "Winching");
            }
            var isleId = areas.IslandAt(anchor);
            islandSizeBeforeMove = areas.IslandSize(isleId);
            startCountWithinMargin = CountWithinMargin();
            GetDirection(out var dx, out var dy);
            S.Sig.WinchStarted.Send();

            var focus = FocusCamera;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                focus = false;
            }
            #endif
            if (!UIShowing.ScreenBlock && focus) {
                var target = GetTarget();
                if (Pos.IsOOB(anchor) || Pos.IsOOB(target) || anchor == 0 || target == 0) {
                    D.Err("Cannot focus, anchor or target invalid: {0} / {1}",
                        Pos.String(anchor), Pos.String(target));
                } else {
                    var anchorIslandId = areas.IslandAt(anchor);
                    var targetIslandId = areas.IslandAt(target);
                    var rektIsland = new List<int>();
                    if (areas.Islands.TryGetValue(anchorIslandId, out var anchorIsland)) {
                        rektIsland.AddRange(anchorIsland);
                    }
                    if (areas.Islands.TryGetValue(targetIslandId, out var targetIsland)) {
                        rektIsland.AddRange(targetIsland);
                    }
                    if (rektIsland.Count > 0) {
                        var rekt = Pos.Encapsulate(rektIsland);
                        S.Sig.CameraFocus.Send(rekt.Center(),
                            rekt.Extents * 1.1f, 0);
                    }
                    var startTime = GameTimer.realtimeSinceStartupAsDouble;
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                    while (CameraDolly.HasMotion) {
                        if (GameTimer.realtimeSinceStartupAsDouble - startTime > 5f) {
                            break;
                        }
                        yield return null;
                        if (!GameState.IsCurrent(S)) { yield break; }
                    }
                    if (GameState.IsCurrent(S)) {
                        #if !PRODUCTION_BUILD
                        if (The.TestsAreRunning) {
                            yield return null;
                        } else {
                            yield return Consts.Wait1Sec;
                        }
                        #else
                        yield return Consts.Wait1Sec;
                        #endif
                    }
                }
            }

            // for vfx
            // Time.timeScale = 1f;
            while (!hasFailedMoving && CanMove(dx, dy) /* && !IsMovingDeeperIntoMargin()*/) {
                if (!Ready.AtmoO2 || !Ready.AtmoTemp || !Ready.Areas) {
                    D.Err("Atmo / temp / areas not ready during winch!");
                }
                var stepStartTime = Time.realtimeSinceStartupAsDouble;
                yield return Move();
                if (Instant) { break; }
                if (!GameState.IsCurrent(S)) { yield break; }
                bool addDelay = true;
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    addDelay = false;
                }
                #endif
                if (addDelay) {
                    int attempts = 1000;
                    while (Time.realtimeSinceStartupAsDouble - stepStartTime < 0.1d && attempts-- > 0) {
                        yield return null;
                        if (!GameState.IsCurrent(S)) { yield break; }
                    }
                }
            }
            S.Sig.ShakeCamera.Send(0f, 0.5f);
            S.Sig.WinchEnded.Send(StepsMade > 0);
            #if DEBUG_WINCH
            D.Log("Done Winching");
            #endif
            S.Sys.Dirt.RebuildMap();
            if (!IsGenStage) {
                S.Sig.ResetTileGraphics.Send();
                yield return Ready.WaitForRendering(this);
                if (!GameState.IsCurrent(S)) { yield break; }
                //Ready.SetUserInput(true, "Winching");
                Ready.SetSave(true, "Winching");
                Ready.SetGen(true, "Winching");
            }


            areas.DoorCheck.RebuildDoors();
            S.Sig.ForceAreas.Send();
            areas.DoorCheck.ValidateDoors();
            // no need anymore
            //yield return S.Sys.Path.Recalculate();
            if (!GameState.IsCurrent(S)) { yield break; }
            endIslandCount = areas.IslandCount;
            #if DEBUG_WINCH
            D.Log("Sections before {0} after {1}",
                startIslandCount, endIslandCount);
            #endif
            if (!IsGenStage) {
                #if !PRODUCTION_BUILD
                if (!The.TestsAreRunning) {
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                    S.UIQuery.ToggleGameUI.Ask(true, false);
                }
                #else
                S.Clock.SetSpeed(ClockSpeed.Normal);
                S.UIQuery.ToggleGameUI.Ask(true, false);
                #endif
                S.Query.CameraHideLink.Ask();
            }
            connectors.Clear();
            S.Components.FindByInterface(connectors);
            try {
                foreach (var c in connectors) {
                    A.Grid.UpdateAfterWinch(c);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failure when validating connections after winch!");
            }
            AfterMove();
            IsDone = true;
            S.Clock.ForceResume(ForceStopReason.BulkOperation);
            S.Clock.Unlock(moveLock);
            S.SetWinchEnded();
        }

        private bool IsMovingDeeperIntoMargin() {
            var cntMargin = CountWithinMargin();
            if (cntMargin > startCountWithinMargin) {
                #if DEBUG_WINCH
                D.Warn("Can't move: going deeper into margin {0} > {1}",
                    startCountWithinMargin, cntMargin);
                #endif
                return true;
            }
            return false;
        }

        protected virtual void AfterMove() {
        }

        protected abstract bool CanMove(int x, int y);

        private readonly HashSet<int> outlineTemp = new();

        private void ValidateMove(HashSet<int> island, int x, int y) {
            PosRect.InnerOutlineOf(island, outlineTemp);
            if (!ValidateOutlineBeforeMove(outlineTemp, island)) {
                return;
            }
            outlineTemp.Clear();
            PosRect.OuterDirectionalOutlineOf(island, outlineTemp, x, y);
            PosRect.Remove(outlineTemp, island);
            CheckMoveOutline(outlineTemp);
        }

        private bool ValidateOutlineBeforeMove(HashSet<int> outline, HashSet<int> island) {
            foreach (var p in outline) {
                // skip parts of outline that are outside of the map
                if (Pos.IsOOB(p)) { continue; }
                var obj = EntityUtils.ParentObjAt(p);
                if (obj != null && !obj.IsConstructed && obj.Transform.IsMulti) {
                    int subPosCount = obj.Transform.GetSubtilePos(Caches.PosIdx);
                    for (int i = 0; i < subPosCount; i++) {
                        var sp = Caches.PosIdx[i];
                        if (!island.Contains(sp)) {
                            // We have a partial object that would get fucked if we move the island
                            hasFailedMoving = true;
                            ShowObstacleWarning(sp);
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private void CheckMoveOutline(HashSet<int> outline) {
            if (CaptureCollision) {
                LastCollision ??= new List<int>();
                LastCollision.Clear();
            }
            foreach (var t in outline) {
                if (Pos.IsOOBPadded(t, padding: 1)) {
                    hasFailedMoving = true;
                    break;
                }
                if (EntityUtils.IsFloorAt(t)) {
                    var f = EntityUtils.FloorAt(t);
                    if (f.IsConstructed && BreaksFloorsInPath) {
                        f.Constructable.BreakDown(0.5f, immediate: true);
                    } else {
                        //f.Constructable.CancelAdvert("Winching");
                        hasFailedMoving = true;
                        ShowObstacleWarning(t);
                        break;
                    }
                    if (CaptureCollision) {
                        LastCollision.Add(t);
                    }
                }
                if (EntityUtils.IsObjAt(t)) {
                    #if DEBUG_WINCH
                    D.Log("Cannot move, outline has obj: {0}", Pos.String(t));
                    #endif
                    ShowObstacleWarning(t);
                    if (CaptureCollision) {
                        LastCollision.Add(t);
                    }
                    hasFailedMoving = true;
                    break;
                }
            }
        }

        private void ShowObstacleWarning(int pos) {
            if (!isInitiatedByPlayer) { return; }
            Ready.SetGen(true, "Winching");
            WPopup.Create(false, IconId.CWarning, T.OperationAborted,
                T.WinchObstacle_V1.F(StepsMade))
                .WithAction2(T.Show, () => {
                    var tt = EntityUtils.GetTopmostParentTile(pos);
                    if (tt != null) {
                        S.Sig.SelectEntity.Send(tt);
                    }
                    EntityUtils.CameraFocusOn(pos);
                })
                .WithAction(T.Ok, null)
                .Spawn();
            EntityUtils.CameraFocusOn(pos);
        }

        private IEnumerator Move() {
            if (!IsGenStage) {
                S.Clock.SetSpeed(ClockSpeed.Stopped);
            }
            startCountWithinMargin = CountWithinMargin();
            yield return PrepareForMove();

            var island = GetIslandAndMoveDir(out var islandId, out var x, out var y);
            if (Instant) {
                // move direction is actually the full jump
                GetDirection(out x, out y);
                UISounds.PlayHyperjump();
            }
            // 1983956049
            if (island == null) {
                hasFailedMoving = true;
                yield break;
            }
            #if DEBUG_WINCH
            D.Warn("Moving section with size of {0} towards: {1}:{2}",
                island.Count, x, y);
            #endif
            island = new HashSet<int>(island);
            if (CaptureCollision) {
                LastIsland = island;
            }

            // add an outline so we can check for attached objects
            island = SectionMoveSet.Build(S, islandId, island, false, out var moveSetFailure);
            if (moveSetFailure.Failed) {
                hasFailedMoving = true;
                if (!Pos.IsOOB(moveSetFailure.ObstaclePos)) {
                    ShowObstacleWarning(moveSetFailure.ObstaclePos);
                    // A space object touching both islands (a hull solar panel) is where
                    // the sections meet, so it counts as the collision point
                    if (CaptureCollision) {
                        LastCollision ??= new List<int>();
                        LastCollision.Clear();
                        LastCollision.Add(moveSetFailure.ObstaclePos);
                    }
                }
            }

            if (hasFailedMoving) {
                yield break;
            }

            ValidateMove(island, x, y);

            if (hasFailedMoving) {
                yield break;
            }

            fTemp.Clear();
            wTemp.Clear();
            oTemp.Clear();
            dTemp.Clear();
            daTemp.Clear();
            objTemp.Clear();
            bTemp.Clear();
            dirtTemp.Clear();
            heatTemp.Clear();
            oxyTemp.Clear();
            invincibleTemp.Clear();
            tintFloorTemp.Clear();
            tintWallTemp.Clear();

            var tint = S.Sys.Tint;
            var areas = S.Sys.Areas;

            // go through each pos and grab temporary objects
            foreach (var p in island) {
                MoveToTemp(p, WorldLayer.Floor, fTemp);
                MoveToTemp(p, WorldLayer.Walls, wTemp);
                MoveToTemp(p, WorldLayer.Objects, oTemp);
                MoveToTemp(p, WorldLayer.Decals, dTemp);
                MoveToTemp(p, WorldLayer.DecalsAbove, daTemp);

                if (tint != null) {
                    if (tint.TryGetFloorTint(p, out var fTint)) {
                        tintFloorTemp.Add(p, fTint);
                        tint.RemoveTint(WorldLayer.Floor, p, false);
                    }
                    if (tint.TryGetWallTint(p, out var wTint)) {
                        tintWallTemp.Add(p, wTint);
                        tint.RemoveTint(WorldLayer.Walls, p, false);
                    }
                }

                if (IsGenStage) {
                    if (ShipSpec.InvincibleParts.Contains(p)) {
                        invincibleTemp.Add(p);
                    }
                    if (ShipSpec.HasCPU && p == ShipSpec.CPUPos) {
                        ShipSpec.SetCPUPos(
                            Pos.NeighborAt(ShipSpec.CPUPos, x, y));
                    }
                }

                heatTemp.Add(p, S.GetTemp(p));
                oxyTemp.Add(p, S.GetO2(p));
                S.SetTemp(p, 0);
                S.SetO2(p, 0);

                var dirt = S.Sys.Dirt.RemoveAt(p);
                if (!Arrays.IsEmpty(dirt)) {
                    dirtTemp.Add(p, dirt);
                }

                if (S.Beings.ByPos.TryGetValue(p, out var beingsCache)) {
                    if (beingsCache.Count > 0) {
                        bTemp.Add(p, new List<Being>(beingsCache));
                    }
                }

                if (S.Objs.ByPos.TryGetValue(p, out var objCache)) {
                    if (objCache.Count > 0) {
                        objTemp.Add(p, new List<Obj>(objCache));
                    }
                }

            }

            foreach (var i in invincibleTemp) {
                ShipSpec.InvincibleParts.Remove(i);
            }
            foreach (var i in invincibleTemp) {
                ShipSpec.InvincibleParts.Add(Pos.NeighborAt(i, x, y));
            }

            foreach (var d in dirtTemp) {
                var newPos = Pos.NeighborAt(d.Key, x, y);
                foreach (var dirt in d.Value) {
                    dirt.MoveTo(newPos);
                    S.Sys.Dirt.Add(dirt, false);
                }
            }
            foreach (var b in bTemp) {
                var newPos = Pos.NeighborAt(b.Key, x, y);
                foreach (var be in b.Value) {
                    if (be.IsActive) {
                        bool isSleeping = be.Data.HasCondition(ConditionIdH.Sleeping);
                        be.UpdatePosIdx(newPos, x, y);
                        if (!isSleeping) {
                            be.Motion.Abort(T.AdRejInterrupted);
                        }
                        if (be.TryGetComponent<MovableComp>(out var movable) && movable.IsPendingMove) {
                            var targetPos = movable.MoveTargetPos;
                            if (island.Contains(targetPos)) {
                                movable.UpdateProxyAndTarget(Pos.ToVector(Pos.NeighborAt(targetPos, x, y)));
                            }
                        }
                    }
                }
            }
            // Off-section pawns walking toward a target on the moved section: their
            // path leads to a now-vacated space position. Abort so they re-path to the
            // target's new location. On-section beings were already aborted above.
            var alive = S.Beings.Alive;
            for (int i = 0; i < alive.Count; i++) {
                var be = alive[i];
                if (!be.IsActive) { continue; }
                var mp = be.Motion.Progress;
                if (mp.IsMoving && island.Contains(mp.GoalPosIdx)) {
                    be.Motion.Abort(T.AdRejInterrupted);
                }
            }
            foreach (var o in objTemp) {
                var newPos = Pos.NeighborAt(o.Key, x, y);
                foreach (var ob in o.Value) {
                    // Skip asteroids, capsules underneath or above the ship
                    if (ob.TryGetComponent<IWithTrajectory>(out var wt)) {
                        if (wt.IsMidFlight) { continue; }
                    }
                    ob.UpdatePosIdx(newPos, x, y);
                    if (ob.TryGetComponent<MovableComp>(out var movable) && movable.IsPendingMove) {
                        var targetPos = movable.MoveTargetPos;
                        if (island.Contains(targetPos)) {
                            movable.UpdateProxyAndTarget(Pos.ToVector(Pos.NeighborAt(targetPos, x, y)));
                        }
                    }
                }
            }
            foreach (var h in heatTemp) {
                var newPos = Pos.NeighborAt(h.Key, x, y);
                S.SetTemp(newPos, h.Value);
            }
            foreach (var o in oxyTemp) {
                var newPos = Pos.NeighborAt(o.Key, x, y);
                S.SetO2(newPos, o.Value);
            }

            // Everything is in temp, and map should be clear, let's move
            foreach (var t in fTemp) {
                MoveFromTemp(t.Key, WorldLayer.Floor, t.Value, x, y);
            }
            foreach (var t in wTemp) {
                MoveFromTemp(t.Key, WorldLayer.Walls, t.Value, x, y);
            }
            foreach (var t in oTemp) {
                MoveFromTemp(t.Key, WorldLayer.Objects, t.Value, x, y);
            }
            foreach (var t in dTemp) {
                MoveFromTemp(t.Key, WorldLayer.Decals, t.Value, x, y);
            }
            foreach (var t in daTemp) {
                MoveFromTemp(t.Key, WorldLayer.DecalsAbove, t.Value, x, y);
            }

            foreach (var ft in tintFloorTemp) {
                var newPos = Pos.NeighborAt(ft.Key, x, y);
                tint.SetTint(WorldLayer.Floor, newPos, ft.Value, false);
            }
            foreach (var wt in tintWallTemp) {
                var newPos = Pos.NeighborAt(wt.Key, x, y);
                tint.SetTint(WorldLayer.Walls, newPos, wt.Value, false);
            }

            MoveAnchorAndTarget(x, y);
            LastMotion = new Vector2(x, y);
            ExecutedMotion += LastMotion;

            if (Instant) {
                StepsMade = Mathf.CeilToInt(LastMotion.magnitude);
            } else {
                StepsMade++;
            }

            S.AddWinchStep();
            if (!IsGenStage) {
                S.Sig.ShipMoved.Send(x, y);
            }
            //S.Sig.ForceAreas.Send();
            S.Sig.RebuildMap.Send();
            S.Sig.ForceAreas.Send();
            //S.Sig.ResetTileGraphics.Send();
            VerifyMultiTileObjects(S, null);
            var objsSnapshot = new List<Obj>(S.Objs.All);
            for (int i = 0; i < objsSnapshot.Count; i++) {
                var obj = objsSnapshot[i];
                if (obj.IsActive) {
                    obj.Transform.UpdateInSpace(false);
                }
            }
            if (!IsGenStage) {
                S.Sys.Dirt.RebuildMap();
            }
            S.Clock.OnNextUpdate(S.Sys.Rend.Reinit);

            var newIslandId = GetIslandId();
            var islandSize = areas.IslandSize(newIslandId);
            var delta = islandSize - islandSizeBeforeMove;
            if (delta != 0) {
                if (delta < 0) {
                    D.Err("Island got smaller after move! {0} -> {1}",
                        islandSizeBeforeMove, islandSize);
                }
                // To allow moving out of corners without pausing
                startCountWithinMargin += delta;
                islandSizeBeforeMove = islandSize;
            }
        }
        private static TileTransformComp[] transforms;


        private void VerifyMultiTileObjects(GameState S, ShipSpec spec) {
            transforms ??= new TileTransformComp[32];
            var found = S.Components.FindAllMatching<TileTransformComp>(
                    transforms, (tt) => tt.IsMulti);
            for (int i = 0; i < found; i++) {
                var t = transforms[i];
                if (spec != null && spec.HasGenArea && !spec.IsWithinGenArea(t.PosIdx)) {
                    continue;
                }
                bool isIntact = true;
                var cnt = t.GetSubtilePos(Caches.PosIdx);
                for (int ti = 0; ti < cnt; ti++) {
                    var p = Caches.PosIdx[ti];
                    if (!EntityUtils.IsObjAt(p)) {
                        isIntact = false;
                    }
                };
                if (!isIntact) {
                    t.Tile.Constructable.BreakDown(0.5f);
                }
            }
        }

        protected abstract void MoveAnchorAndTarget(int x, int y);

        private void MoveFromTemp(int p, int layer, Tile tile, int x, int y) {
            var newPos = Pos.NeighborAt(p, x, y);
            if (!tile.IsActive) {
                D.Err("Trying to move inactive tile! {0} [{1}:{2}]", tile, x, y);
                return;
            }
            var g = S.Map.Grids[layer];
            var old = g.Data[newPos];
            if (old != null) {
                // Could be a diagonal single tile in there, ok for floor / wall layers
                #if DEBUG_WINCH
                D.Warn("There was something in old position: {0} -> {1}. Destroying and replacing with {2}",
                    Pos.String(p), old, tile);
                #endif
                if (old.IsActive) {
                    old.Constructable.BreakDown(0.1f, immediate: true);
                    //S.RemoveEntity(old, true, tile.Transform.IsChild);
                }
            }
            g.Set(newPos, tile);
            tile.UpdatePosIdx(newPos, x, y);
            S.Sys.Info.MoveTileInfo(tile, x, y);
        }

        private void MoveToTemp(int p, int layer, Dictionary<int, Tile> temp) {
            if (temp == null) {
                D.Err("Trying to MoveToTemp in winch with temp being null!");
                return;
            }
            var g = S.Map.Grids[layer];
            var tile = g.Set(p, null);
            if (tile?.IsActive == true) {
                temp.Add(p, tile);
            }
            tile?.Teleporter?.RemoveFromPath();
        }

        protected int CountWithinMargin() {
            var areas = S.Sys.Areas;
            var island = areas.IslandAt(anchor);
            if (island == 0) {
                if (Ready.Gen) {
                    D.ErrSoft("Trying to get count within margin for anchor at {0} - there is no island! Islands count: {1}. Steps made: {2}",
                        Pos.String(anchor), areas.IslandCount, StepsMade);
                    #if !PRODUCTION_BUILD
                    The.ScreenBlock.HideImmediate();
                    S.Sig.ToggleRendering.Send(true);
                    #endif
                }
                return 0;
            }
            int countInMargin = 0;
            foreach (var pos in areas.Islands[island]) {
                if (!EntityUtils.IsWithinMargin(pos, 2, 2,
                        ignoreMargin: false, ignoreCornerAreas: true)) {
                    countInMargin++;
                }
            }
            return countInMargin;
        }
    }
}
