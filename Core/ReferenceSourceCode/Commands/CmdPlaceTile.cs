using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.Commands;
using KL.Grid;
using KL.Utils;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;
using KL.Randomness;
using Game.UI;
using Game.Input;
using System.Net.WebSockets;

namespace Game.Commands {
    public class CmdPlaceTile : Command {
        private readonly Tile tileToPlace;
        public Tile PlacedTile;
        private readonly int layer;
        private readonly int pos;
        private readonly bool warn;
        private readonly bool instant;
        private readonly bool skipValidation;
        public bool IsSuccess;
        private Action<Tile> afterPlace;
        public CmdPlaceTile(int pos, Tile tile, bool warn, bool instant,
                bool skipValidation = false, Action<Tile> afterPlace = null) : base("place_tile") {
            if (tile == null) {
                D.Err("CmdPlaceTile taking Null tile as input! {0}", Pos.String(pos));
                return;
            }
            tileToPlace = tile;
            this.pos = pos;
            layer = tile.Transform.LayerId;
            this.warn = warn;
            this.instant = instant;
            this.skipValidation = skipValidation;
            this.afterPlace = afterPlace;
        }

        protected override void Execute() {
            if (tileToPlace == null) {
                D.Err("Can't place empty tile");
                return;
            }
            IsSuccess = true;
            if (!skipValidation) {
                if (!CanPlace(pos, tileToPlace, layer, warn)) {
                    ToolPlaceTiles.ClearDebrisFor(tileToPlace, pos, null);
                    IsSuccess = false;
                    return;
                }
            }
            if (tileToPlace.TryGetComponent<TemplateComp>(out var tpl)) {
                IsSuccess = tpl.Spawn(pos);
                return;
            }
            tileToPlace.PlacementCheckResult = null;
            tileToPlace.Transform.PosIdx = pos;
            if (tileToPlace.Transform.IsMulti) {
                var cnt = EntityUtils.LoadMultitile(tileToPlace, Caches.Tiles);
                for (int i = 0; i < cnt; i++) {
                    PlaceTileWithChecks(Caches.Tiles[i], pos, layer, instant);
                }
            } else {
                PlaceTileWithChecks(tileToPlace, pos, layer, instant);
            }
            if (tileToPlace.IsActive) {
                PlacedTile = tileToPlace;
            }
            if (!instant && layer == WorldLayer.Floor && PlacedTile != null
                    && S.Map.Grids[layer].Get(pos) == PlacedTile) {
                FloorConstructionWavePlanner.AssignAfterPlacement(PlacedTile);
            }
            if (afterPlace != null) {
                try {
                    afterPlace(tileToPlace);
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed CmdPlaceTile afterplace for: {0}", tileToPlace);
                }
                afterPlace = null;
            }
        }

        public static void PlaceTileWithChecks(Tile tile, int pos, int layer, bool instant) {
            var g = tile.S.Map.Grids;
            var previousTile = g[layer].Get(pos);
            if (tile.Transform.IsChild) {
                // if we are replacing, let's only replace the root tile
                if (previousTile == null || instant ||
                        !EntityUtils.ParentObj(previousTile).IsConstructed) {
                    g[layer].Set(tile.PosIdx, tile);
                }
                return;
            }
            if (previousTile == null || instant || !previousTile.IsConstructed) {
                if (instant && Ready.Gen && previousTile != null && tile.Constructable
                        .CanReplaceWith(pos, out var repTile)) {
                    // Replace tile in sandbox mode
                    if (repTile.Constructable.ReplaceWith(tile)) {
                        repTile.PendingReplacement.FinishReplacement();
                    } else {
                        UISounds.PlayActionDenied();
                    }
                } else {
                    EntityUtils.PlaceTile(tile, pos, instant);
                }
                return;
            }
            if (tile.Reinstallable != null && tile.Reinstallable.SourceId
                    == EntityUtils.ParentObj(previousTile).Id) {
                return;
            }
            D.Ass(tile.Constructable.CanReplaceWith(pos, out var _),
                "We can replace the old tile");
            // At this point we have and old tile and it's not insta-build
            if (!previousTile.Constructable.ReplaceWith(tile)) {
                D.Err("Failed replacing {0} with: {1}", previousTile, tile);
            }
        }

        private static bool CheckSpacingGap(Tile tile, int position, int layer,
                Tile reinstalling, bool warn) {
            var spacingGap = tile.Transform.SpacingCategoryMinGap;
            if (spacingGap == 0) { return true; }
            var cat = tile.Transform.SpacingCategory;
            if (cat == null) {
                D.Err("SpacingCategoryMinGap has null SpacingCategory! {0}",
                    tile.DefinitionId);
            } else {
                var center = tile.Transform.RotatedCenter(position);
                var centerPos = EntityUtils.ToPos(center);
                for (int x = -spacingGap; x <= spacingGap; x++) {
                    for (int y = -spacingGap; y <= spacingGap; y++) {
                        if (x == 0 && y == 0) { continue; }
                        var p = Pos.NeighborAt(centerPos, x, y);
                        if (Pos.SqrDistance(centerPos, p) > spacingGap * spacingGap) {
                            continue;
                        }
                        // check if there's an object of same category here
                        var t = EntityUtils.GetTileInLayer(p, layer);
                        if (t != null) {
                            // We're relocating ourselves
                            if (reinstalling != null && t == reinstalling) {
                                continue;
                            }
                            if (t.Transform.SpacingCategory == cat) {
                                if (warn) {
                                    ShowWarning(T.OperationForbidden, T.PlacementTipSpacingGap_V1.F(spacingGap));
                                }
                                // no!
                                D.Warn("Cannot place {0}, there is a spacing conflict with {1}. Tile: {2}",
                                    Pos.String(centerPos), Pos.String(p), t.DefinitionId);
                                return false;
                            } else if (t.PendingReplacement?.TargetSpacingCategory == cat) {
                                if (warn) {
                                    ShowWarning(T.OperationForbidden, T.PlacementTipSpacingGap_V1.F(spacingGap));
                                }
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        }

        private static void ShowWarning(string prefix, string msg) {
            A.S.Sig.ShowErrorMessage.Send($"{prefix}: {msg}");
        }

        // Derelicts can bring extra ship computers, placement accepts any of them
        private static ShipComputerComp NearestShipComputer(int posIdx) {
            var cpus = A.S.Components.AllByType<ShipComputerComp>();
            if (cpus == null) { return null; }
            ShipComputerComp nearest = null;
            var minDist = int.MaxValue;
            foreach (ShipComputerComp cpu in cpus) {
                var dist = Pos.SqrDistance(posIdx, cpu.Tile.PosIdx);
                if (dist < minDist) {
                    minDist = dist;
                    nearest = cpu;
                }
            }
            return nearest;
        }

        public static bool CanPlace(int position, Tile tile, int layer, bool warn,
                Tile reinstalling = null, bool isMassReinstall = false) {

            // Allow placing objects as close as 1 tile to the edge of the map
            // but disallow walls / floors
            // The no-build zone squares on the sides will be fully buildable
            var ignoreMargin = !Ready.Gen || layer == WorldLayer.Objects
                    || A.InstantBuild;
            if (!EntityUtils.IsWithinMargin(position,
                    tile.Transform.RotatedWidth,
                    tile.Transform.RotatedHeight,
                    ignoreMargin, true)) {
                if (warn) {
                    ShowWarning(T.OperationForbidden, T.WarningTooFar);
                }
                return false;
            }

            if (!CheckSpacingGap(tile, position, layer, reinstalling, warn)) {
                return false;
            }

            if (layer == WorldLayer.Floor && A.InstantBuild) {
                return true;
            }

            Tile replacementTarget = null;
            bool canReplace = reinstalling == null &&
                    tile.Constructable?.CanReplaceWith(
                        position, out replacementTarget) == true;
            var placement = tile.Constructable != null ?
                tile.Constructable.Placement : PlacementConstraint.OnFloor;
            var tr = tile.Transform;
            if (tr.IsMulti) {
                var posCount = EntityUtils.SubtilePositions(
                    position, tile, Caches.PosIdx);
                for (int i = 0; i < posCount; i++) {
                    if (!CanPlacePartial(tile, position,
                            Caches.PosIdx[i], layer,
                            canReplace, replacementTarget,
                            placement, warn, reinstalling,
                            isMassReinstall)) {
                        return false;
                    }
                }
                return CanPlacePorts(tr, position, reinstalling, warn);
            } else {
                if (CanPlacePartial(tile, position, position, layer,
                        canReplace, replacementTarget,
                        placement, warn, reinstalling, isMassReinstall)) {
                    return CanPlacePorts(tr, position, reinstalling, warn);
                } else {
                    return false;
                }
            }
        }

        private static bool CanPlacePort(int portPos, Tile reinstalling, bool allowInWorkSpotCompatible) {
            if (EntityUtils.IsWallAt(portPos)) { return false; }
            // Disallow placing near angled floors or structures
            var floorAt = EntityUtils.FloorAt(portPos);
            if (floorAt?.Passability.IsWalkableDefault != true) {
                return false;
            }
            var obj = EntityUtils.ObjAt(portPos);
            if (obj == null) {
                return true;
            }
            if (allowInWorkSpotCompatible && obj.Transform.IsWorkSpotCompatible) {
                return true;
            }
            if (reinstalling != null) {
                if (EntityUtils.ParentObj(obj) == reinstalling) {
                    return true;
                }
            }
            return false;
        }

        private static bool CanPlacePorts(TileTransformComp tr, int pos, Tile reinstalling, bool warn) {
            bool success = true;
            if (tr.HasWorkSpot) {
                var ws = tr.RotatedWorkSpot(pos);
                if (!CanPlacePort(ws, reinstalling, true)) {
                    if (ws == pos) {
                        // port is within the object itself
                    } else {
                        success = false;
                    }
                }
            }
            if (success && tr.HasInPort) {
                var ws = tr.RotatedInPort(pos);
                if (!CanPlacePort(ws, reinstalling, false)) {
                    if (ws == pos) {
                        // port is within the object itself
                    } else {
                        success = false;
                    }
                }
            }
            if (success && tr.HasOutPort) {
                var ws = tr.RotatedOutPort(pos);
                if (!CanPlacePort(ws, reinstalling, false)) {
                    if (ws == pos) {
                        // port is within the object itself
                    } else {
                        success = false;
                    }
                }
            }

            if (!success && warn) {
                ShowWarning(T.OperationFailed, T.PlacementTipPorts);
            }

            return success;
        }

        private static bool CanPlacePartial(Tile tile, int rootPos,
                int posIdx, int layer, bool canReplace, Tile replacementTarget,
                PlacementConstraint placement, bool warn, Tile reinstalling,
                bool isMassReinstall) {
            var floorTile = tile.S.Map.Grids[WorldLayer.Floor].Get(posIdx);

            bool canPlace;

            if (floorTile == null) {
                // We can place if floor is missing and constraint is anywhere or inspace
                canPlace = placement == PlacementConstraint.Anywhere ||
                    placement == PlacementConstraint.InSpace ||
                    placement == PlacementConstraint.InSpaceAttached ||
                    placement == PlacementConstraint.InSpaceAttachedChained;
                if (!canPlace && warn) {
                    // D.Warn("Cannot place object in open space: {0}, Layer {1}",
                    //    posIdx, WorldLayer.Name(layer));
                    ShowWarning(T.OperationFailed, T.PlacementTipOnfloor);
                }

                if (!canPlace) { return false; }

                var spaceObjTile = EntityUtils.ParentObjAt(posIdx);
                if (spaceObjTile != null) {
                    // Unconstructed tile that is not supposed to be built in space,
                    // let's allow adding a floor underneath
                    if (!spaceObjTile.Constructable.IsBuiltInSpace) {
                        return true;
                    }
                    if (!canReplace && warn) {
                        // D.Warn("Cannot replace space object tile: {0} at {1}",
                        //    spaceObjTile, posIdx);
                    }
                    if (spaceObjTile == reinstalling) {
                        return true;
                    }
                    bool canBuild = true;
                    if (canReplace && spaceObjTile != replacementTarget) {
                        canBuild = false;
                    }
                    if (canBuild) {
                        canBuild = canReplace && tile.Definition.LayerId == WorldLayer.Objects;
                    }
                    if (warn && !canBuild) {
                        ShowWarning(T.OperationForbidden,
                            T.WarningBlockedBy_V1.F(spaceObjTile.NameLink));
                    }
                    return canBuild;
                    //D.Log("Can replace spaceob: {0}", canReplace);
                }

                if (placement == PlacementConstraint.InSpaceAttached
                        || placement == PlacementConstraint.InSpaceAttachedChained) {
                    // We need to check adjacency to a floor
                    // tile.Transform.IsChild is not set at this point, but
                    // we can figure it out by checking rootPos == position
                    if (rootPos != posIdx) { return true; }
                    var hasAdjFloor = EntityUtils.HasAdjacentFloor(tile, posIdx,
                        requireConstructed: false);
                    if (hasAdjFloor) { return true; }
                    if (placement == PlacementConstraint.InSpaceAttachedChained) {
                        if (isMassReinstall || EntityUtils.HasAdjacentSamePlacementGroup(tile, posIdx)) {
                            return true;
                        }
                        if (warn) {
                            ShowWarning(T.OperationFailed, T.PlacementTipInspaceattachedchained);
                        }
                        return false;
                    } else {
                        // There's no adjacent floor
                        if (warn) {
                            ShowWarning(T.OperationFailed, T.PlacementTipInspaceattached);
                        }
                        return false;
                    }
                }
                return true;
            }

            // Can't overwrite existing floor tile? Maybe
            if (layer == WorldLayer.Floor) {
                if (canReplace) {
                    /*
                    // We want to allow replacing!!!!!111!!
                    if (!A.InstantBuild) {
                        if (EntityUtils.IsWallAt(posIdx)) { return false; }
                        if (EntityUtils.IsObjAt(posIdx)) { return false; }
                    }
                    */
                    if (floorTile.Definition.Id != tile.Definition.Id) {
                        return true;
                    }
                }
                if (warn) {
                    ShowWarning(T.OperationForbidden, $"{T.WarningReplacementNotAllowed}: {floorTile.NameLink}");
                }
                return false;
            }

            if (floorTile.Goal != null && floorTile.Goal.JobIdH == JobTypeIdH.Demolition) {
                if (warn) {
                    ShowWarning(T.OperationForbidden, T.WarningBlockedBy_V1.F(floorTile.NameLink));
                }
                return false;
            }

            // While we are generating, let's allow placing stuff on damaged
            // floors and floor lights for convenience
            if (Ready.Gen) {
                if (!floorTile.Health.IsOperational) {
                    if (warn) {
                        ShowWarning(T.OperationFailed, $"{T.Damaged}: {floorTile.NameLink}");
                    }
                    return false;
                }
            }

            if (!floorTile.Integrity.CanHaveFullIntegrity) {
                if (warn) {
                    ShowWarning(T.OperationForbidden, $"{floorTile.NameLink} {GlyphMap.ArrowRight} {T.HullIntegrity}: {T.ValueTooLow}");
                }
                return false;
            }

            // Let's allow more
            // if (!tile.Passability.IsPassableDefault) {
            //     // Won't allow placing a tile unless the space is occupied by haulable shit
            //     if (tile.S.Objs.HasNonMatObjInPos(posIdx)) {
            //         return false;
            //     }

            //     // ignore dead beings
            //     if (tile.S.Beings.HasDeadBeingsInPos(posIdx)) {
            //         return false;
            //     }
            // }

            if (layer >= WorldLayer.Walls) {
                var wallTile = tile.S.Map.Grids[WorldLayer.Walls].Get(posIdx);
                // A door being relocated may land on itself, like objects do below
                if (wallTile != null && reinstalling != null
                        && EntityUtils.ParentObj(wallTile) == reinstalling) {
                    wallTile = null;
                }
                if (wallTile != null) {
                    bool doWarn = warn && layer != WorldLayer.Walls && !canReplace;
                    if (layer == WorldLayer.Walls) {
                        if (canReplace) {
                            return true;
                        } else if (warn) {
                            doWarn = true;
                        }
                    }
                    if (doWarn) {
                        ShowWarning(T.OperationForbidden, T.WarningBlockedBy_V1.F(wallTile.NameLink));
                    }
                    // We're placing an object, so no replacing
                    return false;
                }
            }

            var objTile = EntityUtils.ObjAt(posIdx);
            if (objTile != null) {
                var parentObj = EntityUtils.ParentObj(objTile);
                if (reinstalling == null || parentObj != reinstalling) {
                    if (canReplace && parentObj == replacementTarget) {
                        return true;
                    }
                    if (warn && parentObj.IsConstructed) {
                        ShowWarning(T.OperationForbidden, T.WarningBlockedBy_V1.F(objTile.NameLink));
                    }
                    return false;
                }
                // We won't ever make object layer items replaceable
                /*
                if (!canReplace && warn) {
                    // D.Warn("Cannot replace object tile: {0} at {1}",
                        objTile, posIdx);
                }
                return canReplace;
                */
            }

            Tile wsOwner = null;
            Tile portOwner = null;
            // Check if we are not in some work spot
            Pos.ForEachAround(posIdx, 1, false, false, (n) => {
                if (!tile.S.Grid.IsWithinBounds(n)) { return true; }
                var nTile = EntityUtils.ObjAt(n);
                if (reinstalling != null) {
                    if (EntityUtils.ParentObj(nTile) == reinstalling) {
                        return true;
                    }
                }
                if (nTile != null) {
                    if (nTile.Transform.IsChild) {
                        nTile = nTile.Transform.Parent;
                    }
                    if (nTile.Transform.HasWorkSpot) {
                        if (posIdx == nTile.Transform.WorkSpot) {
                            wsOwner = nTile;
                            return portOwner == null;
                        }
                    }
                    if (nTile.Transform.HasInPort) {
                        if (posIdx == nTile.Transform.InPort) {
                            portOwner = nTile;
                            return false;
                        }
                    }
                    if (nTile.Transform.HasOutPort) {
                        if (posIdx == nTile.Transform.OutPort) {
                            portOwner = nTile;
                            return false;
                        }
                    }
                }
                return true;
            });

            if (portOwner != null) {
                if (warn) {
                    ShowWarning(T.OperationForbidden,
                        T.WarningBlockedBy_V1.F($"{portOwner.NameLink} {GlyphMap.ArrowRight} {T.NounIoPort}"));
                }
                return false;
            }

            if (wsOwner != null) {
                if (!tile.Transform.IsWorkSpotCompatible) {
                    if (warn) {
                        ShowWarning(T.OperationForbidden,
                            T.WarningBlockedBy_V1.F($"{wsOwner.NameLink} {GlyphMap.ArrowRight} {T.NounWorkSpot}"));
                    }
                    return false;
                }
            }

            // If placement is near ship computer, now is the time to check if it is close
            if (placement == PlacementConstraint.NearShipComputer) {
                var outline = tile.Transform.OutlineAtWithGarbage(posIdx);
                foreach (var pos in outline) {
                    var nObj = EntityUtils.ParentObjAt(pos);
                    if (nObj == null) { continue; }
                    if (nObj.HasComponent<ShipComputerComp>()) {
                        return true;
                    }
                    /*
                    // Chaining skipped for now because it would be more difficult to traverse the modules
                    if (nObj.TryGetComponent<ConstructableComp>(out var c)) {
                        // Allow building a chain
                        if (c.Placement == PlacementConstraint.NearShipComputer) {
                            return true;
                        }
                    }
                    */
                }

                if (warn) {
                    var cpu = NearestShipComputer(posIdx);
                    ShowWarning(T.OperationFailed, cpu != null
                        ? Texts.AutoLink(T.PlacementTipNearshipcomputer, cpu.Tile)
                        : T.PlacementTipNearshipcomputer);
                }

                return false;
            }

            // If we're placing and object that has constraint: InSpace
            // it will be handled in the beginning when checking for floor presence
            // But here we have to check if the object that we are placing
            // requires a floor or has to be placed in space. We have a floor already.
            // So valid options are: OnFloor and Anywhere
            if (placement == PlacementConstraint.OnFloor
                || placement == PlacementConstraint.Anywhere) {
                return true;
            }
            if (warn) {
                switch (placement) {
                    case PlacementConstraint.InSpaceAttached: {
                        ShowWarning(T.OperationFailed, T.PlacementTipInspaceattached);
                        break;
                    }
                    case PlacementConstraint.InSpaceAttachedChained: {
                        ShowWarning(T.OperationFailed, T.PlacementTipInspaceattachedchained);
                        break;
                    }
                    case PlacementConstraint.InSpace: {
                        ShowWarning(T.OperationFailed, T.PlacementTipInspace);
                        break;
                    }
                    default: {
                        ShowWarning(T.OperationFailed, T.WarningInvalidLocation);
                        break;
                    }
                }
            }
            return false;
        }
    }
}
