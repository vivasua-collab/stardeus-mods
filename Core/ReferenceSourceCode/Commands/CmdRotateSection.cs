#if !PRODUCTION_BUILD
//#define DEBUG_WINCH
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using KL.Clock;
using Game.Systems.TileAnim;
using Game.CodeGen;

namespace Game.Commands {
    public sealed class CmdRotateSection : AsyncCommand {
        public const string CmdId = "rotate_ship";
        private int center;
        private int sectionId;
        private bool even;
        private sbyte dir;
        public bool IsGenStage;
        public bool IsDone;
        private bool hasFailedMoving;
        private bool isWholeMap;
        private bool rotatesShip;
        public Action OnSuccess;
        public override bool TrySameFrame => !Ready.Gen || G.ClockSpeed >= ClockSpeed.Unlimited;

        public CmdRotateSection(int sectionId, int center, sbyte dir, bool even) : base(CmdId) {
            if (dir != 1 && dir != -1) {
                throw new ArgumentException("Direction can only be -1 or 1!");
            }
            this.sectionId = sectionId;
            this.center = center;
            this.dir = dir;
            this.even = even;
        }

        public CmdRotateSection(sbyte dir) : base(CmdId) {
            if (dir != 1 && dir != -1) {
                throw new ArgumentException("Direction can only be -1 or 1!");
            }
            this.dir = dir;
            even = true;
        }

        public ShipSpec ShipSpec;
        private readonly Dictionary<int, float> oxyTemp = new();
        private readonly Dictionary<int, float> heatTemp = new();
        // Floors
        private readonly Dictionary<int, Tile> fTemp = new();
        // Walls
        private readonly Dictionary<int, Tile> wTemp = new();
        // Objects
        private readonly Dictionary<int, Tile> oTemp = new();
        // Multi-tile roots for updating positions. New Pos => Tile
        // key is shifted tile root pos, tile is tile root
        private readonly Dictionary<int, Tile> oMulti = new();
        // key is root tile, value is children
        private readonly Dictionary<Tile, List<Tile>> oMultiChildren = new();
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

        private HashSet<int> GetIsland(out int islandId) {
            if (sectionId > 0) {
                islandId = sectionId;
                if (S.Sys.Areas.Islands.TryGetValue(sectionId, out var isle)) {
                    return isle;
                }
            }
            islandId = S.Sys.Areas.IslandAt(center);
            if (S.Sys.Areas.Islands.TryGetValue(islandId, out var isle2)) {
                return isle2;
            }
            return null;
        }

        /// <summary>
        /// Returns the full set of source positions that rotation will actually
        /// move: the floor island plus all attached space objects (engines,
        /// thrusters, solar panels, etc). Used by both validation and execution
        /// so they always agree on what's moving. Sets hasFailedMoving on
        /// genuine cross-island attachment problems (work spot / foreign isle).
        /// </summary>
        private HashSet<int> BuildRotationSet(out int islandId) {
            var isle = GetIsland(out islandId);
            if (isle == null) { return null; }
            var set = new HashSet<int>(isle);
            var result = SectionMoveSet.Build(S, islandId, set, true, out var failure);
            if (failure.Failed) {
                hasFailedMoving = true;
            }
            return result;
        }

        private static readonly int moveLock = Hashes.S("Rotate");
        public override IEnumerator ExecuteAsync() {
            S.Toolbox.FreeSelect.Cancel();
            int waitAttempts = 1000;
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
                if (G.ClockSpeed > ClockSpeed.Fast2) {
                    break;
                }
                if (!GameState.IsCurrent(S)) { yield break; }
            }
            S.SetWinchStarted();
            if (!IsGenStage) {
                if (UIShowing.Overlay) {
                    S.Sig.HideOverlay.Send();
                }
                S.UIQuery.ToggleGameUI.Ask(false, false);
                Ready.WaitForSaveFull(this);
                var ticks = G.Ticks;
                S.LastWinchTick = ticks;
                //S.Clock.SetSpeed(ClockSpeed.Normal);
            }
            yield return null;
            if (!GameState.IsCurrent(S)) { yield break; }
            S.Sig.ForceAreas.Send();
            if (!IsGenStage) {
                Ready.SetGen(false, "Winching");
                Ready.SetSave(false, "Winching");
            }

            // Validate collision with other islands before rotating.
            // Whole-map rotation (center == 0) has nothing external to collide with.
            if (center != 0 && !ValidateRotationCollision()) {
                D.Warn("Rotation validation failed - would collide with an obstacle!");
                if (!IsGenStage) {
                    ShowRotationObstacleWarning();
                    Ready.SetSave(true, "Winching");
                    Ready.SetGen(true, "Winching");
                    S.Clock.SetSpeed(ClockSpeed.Normal);
                    S.UIQuery.ToggleGameUI.Ask(true, false);
                }
                IsDone = true;
                S.Clock.ForceResume(ForceStopReason.BulkOperation);
                S.Clock.Unlock(moveLock);
                yield break;
            }

            S.Sig.WinchStarted.Send();
            //yield return
            Rotate();
            if (!GameState.IsCurrent(S)) { yield break; }

            S.Sig.WinchEnded.Send(true);
            #if DEBUG_WINCH
            D.Log("Done Winching");
            #endif
            S.Sig.ResetTileGraphics.Send();
            S.Sys.Dirt.RebuildMap();
            if (!IsGenStage) {
                yield return Ready.WaitForRendering(this);
                if (!GameState.IsCurrent(S)) { yield break; }
                //Ready.SetUserInput(true, "Winching");
                Ready.SetSave(true, "Winching");
                Ready.SetGen(true, "Winching");
            }

            S.Sig.ForceAreas.Send();
            // no need anymore
            //yield return S.Sys.Path.Recalculate();
            if (!GameState.IsCurrent(S)) { yield break; }
            #if DEBUG_WINCH
            D.Log("Sections before {0} after {1}",
                startIslandCount, endIslandCount);
            #endif
            if (!IsGenStage) {
                S.Clock.SetSpeed(ClockSpeed.Normal);
                S.UIQuery.ToggleGameUI.Ask(true, false);
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
            IsDone = true;
            S.Clock.ForceResume(ForceStopReason.BulkOperation);
            S.Clock.Unlock(moveLock);
            S.SetWinchEnded();
            OnSuccess?.Invoke();
        }


        private readonly HashSet<int> outlineTemp = new();

        private void Rotate() {
            if (!IsGenStage) {
                S.Clock.SetSpeed(ClockSpeed.Stopped);
            }
            HashSet<int> island;
            if (center == 0) {
                isWholeMap = true;
                island = new HashSet<int>(S.GridSize);
                for (int i = 0; i < S.GridSize; i++) {
                    island.Add(i);
                }
                center = Pos.FromXY(S.GridWidth / 2, S.GridHeight / 2);
            } else {
                isWholeMap = false;
                island = BuildRotationSet(out var rotatingIslandId);
                // Local maneuvering turns the ship itself, so its forward turns with it
                rotatesShip = !IsGenStage && rotatingIslandId > 0
                    && S.Sys.Areas.TryGetShipIsland(out var shipIsland)
                    && shipIsland == rotatingIslandId;
                if (island == null) {
                    hasFailedMoving = true;
                    return;
                    //yield break;
                }
                D.Warn("Rotating section with size of {0} with dir: {1}",
                    island.Count, dir);

                if (hasFailedMoving) {
                    return;
                    //yield break;
                }
            }

            Cleanup();

            var tint = S.Sys.Tint;

            // go through each pos and grab temporary objects
            foreach (var p in island) {
                MoveToTemp(p, WorldLayer.Floor, fTemp);
                MoveToTemp(p, WorldLayer.Walls, wTemp);
                //MoveToTemp(p, WorldLayer.Objects, wTemp);
                MoveToTempObj(p);
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

/*
                var ads = S.Adverts.AtPosRef(p, false);
                if (!Arrays.IsEmpty(ads)) {
                    adsTemp.Add(p, new List<Advert>(ads));
                }
                    */
            }

            if (IsGenStage) {
                if (ShipSpec.HasStasisArray) {
                    for (int i = 0; i < ShipSpec.StasisPos.Count; i++) {
                        var stasisPos = ShipSpec.StasisPos[i];
                        if (!island.Contains(stasisPos)) { continue; }
                        if (Pos.TryRotateAround(center, stasisPos, even, dir,
                                out var newPos, out _, out _)) {
                            ShipSpec.StasisPos[i] = newPos;
                        } else {
                            D.Err("FATAL: failed rotating stasis point at {0}",
                                Pos.String(stasisPos));
                        }
                    }
                }
                if (ShipSpec.HasCPU && island.Contains(ShipSpec.CPUPos)) {
                    var cpuPos = ShipSpec.CPUPos;
                    if (Pos.TryRotateAround(center, cpuPos, even, dir,
                            out var newPos, out _, out _)) {
                        ShipSpec.SetCPUPos(newPos);
                    } else {
                        D.Err("FATAL: failed rotating CPU point at {0}",
                            Pos.String(cpuPos));
                    }
                }
            }

            foreach (var i in invincibleTemp) {
                ShipSpec.InvincibleParts.Remove(i);
            }
            foreach (var i in invincibleTemp) {
                if (Pos.TryRotateAround(center, i, even, dir, out var newPos,
                        out _, out _)) {
                    ShipSpec.InvincibleParts.Add(newPos);
                } else {
                    D.Err("FATAL: failed rotating invincible pats at {0}", Pos.String(i));
                }
            }

            //S.Sig.ResetTileGraphics.Send();

            foreach (var d in dirtTemp) {
                if (Pos.TryRotateAround(center, d.Key, even, dir, out var newPos,
                        out _, out _)) {
                    foreach (var dirt in d.Value) {
                        dirt.MoveTo(newPos);
                        S.Sys.Dirt.Add(dirt, false);
                    }
                } else {
                    D.Err("FATAL: failed rotating dirt at {0}", Pos.String(d.Key));
                }
            }
            foreach (var b in bTemp) {
                if (Pos.TryRotateAround(center, b.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    foreach (var be in b.Value) {
                        bool isSleeping = be.Data.HasCondition(ConditionIdH.Sleeping);
                        be.UpdatePosIdx(newPos, dx, dy);
                        if (!isSleeping) {
                            be.Motion.Abort(T.AdRejInterrupted);
                        }
                        if (be.TryGetComponent<MovableComp>(out var movable) && movable.IsPendingMove) {
                            var targetPos = movable.MoveTargetPos;
                            if (island.Contains(targetPos) &&
                                    Pos.TryRotateAround(center, targetPos, even, dir, out var newTargetPos, out _, out _)) {
                                movable.UpdateProxyAndTarget(Pos.ToVector(newTargetPos));
                            }
                        }
                    }
                } else {
                    D.Err("FATAL: failed rotating being at {0}", Pos.String(b.Key));
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
                if (Pos.TryRotateAround(center, o.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    foreach (var ob in o.Value) {
                        // Skip asteroids, capsules underneath or above the ship
                        if (ob.TryGetComponent<IWithTrajectory>(out var wt)) {
                            if (wt.IsMidFlight) { continue; }
                        }
                        ob.UpdatePosIdx(newPos, dx, dy);
                        if (ob.TryGetComponent<MovableComp>(out var movable) && movable.IsPendingMove) {
                            var targetPos = movable.MoveTargetPos;
                            if (island.Contains(targetPos) &&
                                    Pos.TryRotateAround(center, targetPos, even, dir, out var newTargetPos, out _, out _)) {
                                movable.UpdateProxyAndTarget(Pos.ToVector(newTargetPos));
                            }
                        }
                    }
                } else {
                    D.Err("FATAL: failed rotating obj at {0}", Pos.String(o.Key));
                }
            }
            foreach (var h in heatTemp) {
                if (Pos.TryRotateAround(center, h.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    S.SetTemp(newPos, h.Value);
                } else {
                    D.Err("FATAL: failed rotating heat at {0}", Pos.String(h.Key));
                }
            }
            foreach (var o in oxyTemp) {
                if (Pos.TryRotateAround(center, o.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    S.SetO2(newPos, o.Value);
                } else {
                    D.Err("FATAL: failed rotating oxygen at {0}", Pos.String(o.Key));
                }
            }

            // Everything is in temp, and map should be clear, let's move
            foreach (var t in fTemp) {
                MoveFromTemp(t.Key, WorldLayer.Floor, t.Value);
            }
            foreach (var t in wTemp) {
                MoveFromTemp(t.Key, WorldLayer.Walls, t.Value);
            }
            foreach (var t in oTemp) {
                MoveFromTemp(t.Key, WorldLayer.Objects, t.Value);
            }
            foreach (var m in oMulti) {
                MoveFromTempMulti(m.Key, m.Value);
            }
            foreach (var t in dTemp) {
                MoveFromTemp(t.Key, WorldLayer.Decals, t.Value);
            }
            foreach (var t in daTemp) {
                MoveFromTemp(t.Key, WorldLayer.DecalsAbove, t.Value);
            }

            foreach (var ft in tintFloorTemp) {
                if (Pos.TryRotateAround(center, ft.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    tint.SetTint(WorldLayer.Floor, newPos, ft.Value, false);
                } else {
                    D.Err("FATAL: failed rotating floor tint at {0}", Pos.String(ft.Key));
                }
            }
            foreach (var wt in tintWallTemp) {
                if (Pos.TryRotateAround(center, wt.Key, even, dir, out var newPos,
                        out var dx, out var dy)) {
                    tint.SetTint(WorldLayer.Walls, newPos, wt.Value, false);
                } else {
                    D.Err("FATAL: failed rotating wall tint at {0}", Pos.String(wt.Key));
                }
            }

            // TODO S.AddRotation(dir);
            //S.AddWinchStep();
            //S.Sig.ForceAreas.Send();
            S.Sig.RebuildMap.Send();
            S.Sys.Areas.DoorCheck.RebuildDoors();
            S.Sig.ForceAreas.Send();
            S.Sys.Areas.DoorCheck.ValidateDoors();
            S.Sig.ShipMoved.Send(-1, -1);
            S.Sig.ShipRotated.Send();
            //S.Sig.ResetTileGraphics.Send();
            if (!IsGenStage) {
                S.Sys.Dirt.RebuildMap();
            }

            Cleanup();

            if (isWholeMap || rotatesShip) {
                D.Warn("Changing ship direction from rotate section command!");
                S.Sys.ShipNav.SetForwardDir(Facing.Rotate(S.Sys.ShipNav.ShipDirection,
                    dir == 1 ? Facing.Type.Right : Facing.Type.Left));
            }
        }

        private void Cleanup() {
            fTemp.Clear();
            wTemp.Clear();
            oTemp.Clear();
            oMulti.Clear();
            oMultiChildren.Clear();
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
        }

        // Set by ValidateRotationCollision on failure, so the obstacle warning
        // popup can focus the actual offending position.
        private int collisionPos;
        private bool collisionIsOob;
        private bool hasCollisionPos;

        /// <summary>
        /// Checks whether rotating would land any moving tile on a cell that's
        /// occupied by something NOT part of the rotation. Tiles colliding with
        /// other parts of the *same* moving set are fine - that cell will be
        /// vacated by the time the move happens. Only genuinely external
        /// obstacles (another island, debris, an unconstructed foreign floor)
        /// or rotating off the edge of the map should fail this check.
        /// </summary>
        private bool ValidateRotationCollision() {
            // Build the exact same set that Rotate() will move, so validation
            // and execution can never disagree about what's "ours".
            var movingSet = BuildRotationSet(out var rotatingIslandId);
            if (movingSet == null) {
                D.ErrSoft("Cannot validate rotation - island not found");
                return false;
            }
            if (hasFailedMoving) {
                // BuildRotationSet already logged the specific reason
                // (foreign attachment / split work spot, etc) but doesn't
                // surface a single offending position.
                hasCollisionPos = false;
                return false;
            }

            foreach (var pos in movingSet) {
                // Calculate where this tile would end up after rotation
                if (!Pos.TryRotateAround(center, pos, even, dir, out var newPos, out _, out _)) {
                    collisionPos = pos;
                    collisionIsOob = true;
                    hasCollisionPos = true;
                    D.Warn("Rotation would move tile at {0} out of bounds!", Pos.String(pos));
                    return false;
                }

                var tileAtNewPos = EntityUtils.GetTopmostParentTile(newPos);
                if (tileAtNewPos?.IsActive != true) { continue; }
                if (movingSet.Contains(tileAtNewPos.PosIdx)) {
                    // The occupant is itself part of this rotation - it will
                    // vacate before this tile lands there. Not a collision.
                    continue;
                }
                if (tileAtNewPos.TryGetComponent<IWithTrajectory>(out var wt) && wt.IsMidFlight) {
                    // Asteroids/capsules flying through aren't real obstacles.
                    continue;
                }

                collisionPos = newPos;
                collisionIsOob = false;
                hasCollisionPos = true;
                D.Warn("Rotation collision detected! Tile from island {0} would land on {1} occupied by {2}",
                    rotatingIslandId, Pos.String(newPos), tileAtNewPos);
                return false;
            }

            return true;
        }

        private void ShowRotationObstacleWarning() {
            if (collisionIsOob) {
                WPopup.Create(false, IconId.CWarning, T.OperationAborted,
                        T.WinchRotate_Oob)
                    .WithAction(T.Ok, null)
                    .Spawn();
                return;
            }
            if (!hasCollisionPos) {
                WPopup.Create(false, IconId.CWarning, T.OperationAborted,
                        T.WinchRotate_Obstacle)
                    .WithAction(T.Ok, null)
                    .Spawn();
                return;
            }
            var pos = collisionPos;
            WPopup.Create(false, IconId.CWarning, T.OperationAborted,
                    T.WinchRotate_Obstacle)
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


        private void MoveFromTempMulti(int p, Tile tile) {
            foreach (var sub in oMultiChildren[tile]) {
                var adjSubPos = sub.Transform.AdjustMultiPosBeforeRotation(
                    sub.Transform.PosIdx, dir);
                sub.Transform.ParentPos = p;
                MoveFromTemp(adjSubPos, WorldLayer.Objects, sub, true);
            }
            // p is adjRootPos, tile is root tile (unchanged)
            MoveFromTemp(p, WorldLayer.Objects, tile);
        }

        private void MoveFromTemp(int p, int layer, Tile tile, bool subTile = false) {
            if (!Pos.TryRotateAround(center, p, even, dir, out var newPos, out var dx, out var dy)) {
                D.Err("FATAL: Failed rotating tile with layer {0} at {1}: {2}",
                    layer, Pos.String(p), tile);
                return;
            }
            if (!tile.IsActive) {
                D.Err("Trying to move inactive tile! {0} [{1}:{2}]", tile, dx, dy);
                return;
            }
            var g = S.Map.Grids[layer];
            var old = g.Data[newPos];
            #if DEBUG_WINCH
            if (old != null) {
                // Could be a diagonal single tile in there, ok for floor / wall layers
                D.Warn("There was something in old position: {0} -> {1}. Destroying and replacing with {2}",
                    Pos.String(p), old, tile);
            }
            #endif
            if (tile.Transform.IsRotatable) {
                if (!tile.Transform.IsChild) {
                    var newRot = Facing.Rotate(
                        tile.Transform.Rotation, dir, tile.Transform.ValidRotations);
                    if (tile.TryGetComponent<TileAnimationComp>(out var anim)) {
                        anim.BeforeRotate(tile.Transform.Rotation, newRot);
                    }
                    tile.Transform.Rotation = newRot;
                }
            } else {
                if (tile.Transform.IsMulti && !tile.Transform.IsChild &&
                        (tile.Transform.HasWorkSpot || tile.Transform.Width != tile.Transform.Height)) {
                    D.Err("FATAL: Multi tile that is not rotatable / non square / with work spot! {0}", tile);
                }
            }
            g.Set(newPos, tile);
            var oldPosition = tile.Position;
            tile.Transform.UpdatePosIdx(newPos, dx, dy);
            tile.UpdatePosIdx(newPos, dx, dy);
            var newPosition = tile.Position;
            var diff = newPosition - oldPosition;
            if (!subTile) {
                S.Sys.Info.MoveTileInfo(tile, diff.x, diff.y);
            }
            var beingsSnapshot = new List<Being>(S.Beings.All);
            for (int i = 0; i < beingsSnapshot.Count; i++) {
                beingsSnapshot[i].Motion.UpdateInSpace(false);
            }
            var objsSnapshot = new List<Obj>(S.Objs.All);
            for (int i = 0; i < objsSnapshot.Count; i++) {
                objsSnapshot[i].Transform.UpdateInSpace(true);
            }
            foreach (var comp in tile.Components) {
                if (comp is IRotationDependent rot) {
                    rot.AfterRotationUpdated();
                }
            }
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
        private void MoveToTempObj(int p) {
            var g = S.Map.Grids[WorldLayer.Objects];
            var tile = g.Set(p, null);

            tile?.Teleporter?.RemoveFromPath();
            if (tile?.IsActive != true) { return; }

            if (tile.Transform.IsMulti || tile.Transform.IsChild) {
                var parent = EntityUtils.ParentObj(tile);
                if (parent == null) {
                    D.Err("Parent is null for tile: {0}", tile);
                    return;
                }
                // We need to make sure that parent tile is always moved first
                // and all necessary shit is created
                var adjParPos = parent.Transform.AdjustMultiPosBeforeRotation(
                    parent.Transform.PosIdx, dir);
                List<Tile> childrenContainer;
                if (oMulti.TryAdd(adjParPos, parent)) {
                    // we just added the parent for the first time
                    childrenContainer = new List<Tile>();
                    oMultiChildren.Add(parent, childrenContainer);
                } else if (!oMultiChildren.TryGetValue(parent, out childrenContainer)) {
                    D.Err("FATAL: you fucked up");
                }
                if (parent != tile) {
                    childrenContainer.Add(tile);
                }
            } else {
                oTemp.Add(p, tile);
            }
        }

    }
}
