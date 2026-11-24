using System;
using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Tools;
using Game.UI;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Utils {
    public static class RelocateUtils {
        public static bool Relocate(List<Obj> selectedObj, List<Being> selectedBeings) {
            var comps = new List<IComponent>();
            for (int i = 0; i < selectedObj.Count; i++) {
                Obj o = selectedObj[i];
                if (o.Movable?.IsReachableForCommonAction == true)  {
                    comps.Add(o.Movable);
                }
            }

            for (int i2 = 0; i2 < selectedBeings.Count; i2++) {
                if (selectedBeings[i2].Movable?.IsReachableForCommonAction == true) {
                    comps.Add(selectedBeings[i2].Movable);
                }
            }

            if (comps.Count == 0) {
                UISounds.PlayActionDenied();
                return false;
            }
            ApplyRelocate(comps);
            return true;
        }
        private static void ApplyRelocate(List<IComponent> common) {
            if (Arrays.IsEmpty(common)) { return; }
            var relocatables = new List<MovableComp>();
            var sources = new List<Vector2>();
            for (int i = 0; i < common.Count; i++) {
                var c = common[i];
                if (c.Entity.IsActive && c is MovableComp m) {
                    relocatables.Add(m);
                    sources.Add(m.Entity.Position);
                }
            }
            Func<Vector2, (bool, Vector2)> validator = null;
            if (relocatables.Count == 1) {
                validator = relocatables[0].CanMoveTo;
            } else {
                validator = (potentialV) => (EntityUtils.IsEmptyFloorAt(EntityUtils.ToPos(potentialV), true), potentialV);
            }

            ToolChoosePos.BeginObj(sources, (v) => {
                if (Arrays.IsEmpty(relocatables)) {
                    return new ChoosePosResult {
                        IsSuccess = false,
                        ClearTool = false,
                    };
                }
                var ordered = 0;
                foreach (var r in relocatables) {
                    if (r.Entity.IsActive && r.OrderMove(v + Rng.UInsideUnitCircle() * 0.7f)) {
                        ordered++;
                    }
                }
                The.R.Query.CameraHideLink.Ask();
                return ordered > 0 ? ChoosePosResult.Success : ChoosePosResult.Fail;
            }, validator);
        }
        public static bool Relocate(List<Tile> tiles, bool dryRun, out int cnt) {
            if (!BaseEntityAction.ValidateLimit(tiles.Count, T.Relocate, dryRun)) {
                cnt = 0;
                return false;
            }
            var comps = new List<ReinstallableComp>();
            foreach (var t in tiles) {
                var p = EntityUtils.ParentObj(t);
                if (p.IsActive && p.Reinstallable?.IsReachableForCommonAction == true) {
                    comps.Add(p.Reinstallable);
                }
            }
            if (Arrays.IsEmpty(comps)) {
                cnt = 0;
                return false;
            }
            if (!dryRun) {
                RelocateMulti(comps);
            }
            cnt = comps.Count;
            return true;
        }

        private static void RelocateMulti(List<ReinstallableComp> common) {
            var allPositions = new HashSet<int>();
            var sources = new List<Vector2>();
            for (int i = 0; i < common.Count; i++) {
                var c = common[i];
                var cnt = c.Tile.Transform.GetSubtilePos(Caches.PosIdx);
                for (int j = 0; j < cnt; j++) {
                    allPositions.Add(Caches.PosIdx[j]);
                }
                sources.Add(c.Tile.Position);
            }

            ToolChoosePos.BeginTiles(allPositions, Consts.BlueRectColor,
                (p, v) => OnSelectMulti(common, p, v), ValidatePos, sources);

        }
        private static ChoosePosResult OnSelectMulti(
                List<ReinstallableComp> relocatables, int p, Vector2 vector) {
            var tool = The.Toolbox.Get<ToolChoosePos>(ToolChoosePos.ToolId);
            Pos.ToXY(p, out var x, out var y);
            int motionX = (int) vector.x - x;
            int motionY = (int) vector.y - y;
            // Snapshot rotated destinations and rotations up front. BeginRelocation
            // activates ToolPlaceTiles, which deactivates ToolChoosePos and clears
            // its rotation state — so any per-tile math must happen before the
            // first BeginRelocation call.
            var plan = new List<(ReinstallableComp r, int posTo, Facing.Type? rot)>(
                relocatables.Count);
            foreach (var r in relocatables) {
                var tr = r.Tile.Transform;
                var rotatedAnchor = tool.RotatedTileAnchor(r.Tile.PosIdx,
                    tr.RotatedWidth, tr.RotatedHeight);
                var posTo = Pos.NeighborAt(rotatedAnchor, motionX, motionY);
                Facing.Type? rot = tr.IsRotatable
                    ? tool.RotateTileRotation(tr.Rotation, tr.ValidRotations)
                    : (Facing.Type?) null;
                plan.Add((r, posTo, rot));
            }
            var ordered = 0;
            foreach (var (r, posTo, rot) in plan) {
                D.Warn("Relocating {1}: {0}", r.Tile,
                    Pos.StringFromTo(r.Tile.PosIdx, posTo));
                r.BeginRelocation(posTo, true, rot);
                ordered++;
            }
            The.R.Query.CameraHideLink.Ask();
            return ordered > 0 ? ChoosePosResult.Success : ChoosePosResult.Fail;
        }

        private static bool ValidatePos(int p, Vector2 motion, HashSet<int> set) {
            if (set.Contains(p)) {
                return true;
            }
            var tool = The.Toolbox.Get<ToolChoosePos>(ToolChoosePos.ToolId);
            // Undo motion first, then undo the selection rotation to find
            // where the object actually lives in the world.
            var rotatedPos = Pos.NeighborAt(p, -Mathf.FloorToInt(motion.x),
                    -Mathf.FloorToInt(motion.y));
            var origPos = tool.UnrotatePos(rotatedPos);
            var obj = EntityUtils.GetTopmostTile(origPos);
            if (obj == null) {
                D.Err("Obj null - a bug in ToolChoosePos.ValidateMulti!");
                return false;
            }
            if (obj.Transform.IsChild) {
                obj = obj.Transform.Parent;
            }
            var tr = obj.Transform;
            var rotatedAnchor = tool.RotatedTileAnchor(tr.PosIdx,
                tr.RotatedWidth, tr.RotatedHeight);
            var newPos = Pos.NeighborAt(rotatedAnchor,
                Mathf.FloorToInt(motion.x), Mathf.FloorToInt(motion.y));

            // If the tool is rotating the selection, check CanPlace against
            // the rotated orientation. Swap the tile's rotation temporarily;
            // no world-state side effects follow.
            bool canPlace;
            if (tool.RotationStep != 0 && tr.IsRotatable) {
                var oldRot = tr.Rotation;
                var newRot = tool.RotateTileRotation(oldRot, tr.ValidRotations);
                tr.Rotation = newRot;
                canPlace = CmdPlaceTile.CanPlace(newPos, obj,
                    obj.Definition.LayerId, warn: false, obj, true);
                tr.Rotation = oldRot;
            } else {
                canPlace = CmdPlaceTile.CanPlace(newPos, obj,
                    obj.Definition.LayerId, warn: false, obj, true);
            }
            if (!canPlace) {
                return false;
            }
            return true;
        }
    }
}
