using System;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Grid;
using UnityEngine;
using UnityEditor;
using KL.Utils;
using Game.Components;

namespace Game.Tools {
    public sealed class ToolChoosePos : TileTool, IRanged {
        [Flags]
        public enum ChoiceMode {
            None = 0,
            Obj = 1 << 0,
            Tiles = 1 << 1,
            ShowBounds = 1 << 2,
            AllowRotation = 1 << 3,
        }
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolChoosePos());
        }

        public const string ToolId = "choose_pos";
        public override string Id => ToolId;
        public override bool SkipMenu => true;
        public override bool IsConfigurable => false;

        public Func<Vector2, (bool, Vector2)> Validator;
        /// <summary>
        /// new pos, shift, initial positions, valid?
        /// </summary>
        public Func<int, Vector2, HashSet<int>, bool> ValidatorMulti;
        /// <summary>
        /// center pos, new center, result?
        /// </summary>
        public Func<int, Vector2, ChoosePosResult> OnClickMulti;
        public Func<Vector2, ChoosePosResult> OnClick;
        protected override Color markedColor => Consts.ColorChartRed;
        private Vector2 lastPos;
        private int lastPosIdx;
        private ChoiceMode choiceMode;
        private Color selectRectColor;
        private bool hasValidationError;
        private int range;
        private Vector2 currentMotion;
        private bool isOutOfRange;
        private readonly List<Vector2> sources = new();
        private Vector2 origin;
        private HashSet<int> positions;
        private readonly List<PosRect> multiRects = new();

        // --- Rotation state ---
        // Cumulative clockwise rotation steps (0..3) applied to the selection.
        private int rotationStep;
        // Captured bounding box of the original (unrotated) selection.
        private int origMinX, origMinY, origW, origH;
        // Bounding box of the rotated selection (integer placement).
        private int newMinX, newMinY;
        // Original (unrotated) data captured on BeginTiles.
        private HashSet<int> origPositions;
        private readonly List<Vector2> origSources = new();

        /// <summary>
        /// Current rotation of the selection expressed as a facing type.
        /// Equivalent to Down + rotationStep (CW).
        /// </summary>
        public Facing.Type Rotation => (Facing.Type) rotationStep;
        public int RotationStep => rotationStep;
        public override string MenuIcon => null;
        private SelectionVFX selVFX;
        public override void UpdateVFX(ParticlesSys ps) {
            selVFX ??= ps.SelectionVFX;
            if (!isHoverHidden) {
                if (choiceMode.HasFlag(ChoiceMode.Obj)) {
                    var pos = The.Bindings.Mouse.WorldPosition;
                    selVFX.CreateManyToOneLines(sources, pos);
                } else {
                    selVFX.CreateManyShiftedLines(origSources, sources, currentMotion);
                }
            }
            // The range and the map bounds do not follow the cursor, they stay
            int rangeCount = 0;
            if (range > 0) {
                rangeCount = selVFX.AddRange(rangeCount, Pos.FromVector(origin), 1f, origin, this);
            }
            selVFX.ShowRange(rangeCount);
            base.UpdateVFX(ps);
        }

        protected override int AddRectParticles(int offset, XParticles rectP) {
            ParticleSystem.Particle particle;
            rectP.ResizeIfNecessary(offset + multiRects.Count + 4);
            if (choiceMode.HasFlag(ChoiceMode.Tiles)) {
                var color = hasValidationError ? Consts.RedRectColor : selectRectColor;
                foreach (var rect in multiRects) {
                    particle = ParticlesSys.ParticleAt(rect.Center(),
                        color, rect.Extents, 0.1f);
                    rectP.Particles[offset++] = particle;
                }
            }
            if (choiceMode.HasFlag(ChoiceMode.ShowBounds)) {
                offset = AddBoundsRect(offset, rectP);
            }
            return offset;
        }

        private static int AddBoundsRect(int offset, XParticles rectP) {
            ParticleSystem.Particle particle;
            // bottom
            var boundsRect = new PosRect(0, 0, Pos.GridW - 1, Consts.MapMargin - 1);
            particle = ParticlesSys.ParticleAt(boundsRect.Center(),
                Consts.RedRectColor, boundsRect.Extents, 0.1f);
            rectP.Particles[offset++] = particle;

            // top
            boundsRect = new PosRect(0, Pos.GridH - Consts.MapMargin, Pos.GridW - 1, Pos.GridH - 1);
            particle = ParticlesSys.ParticleAt(boundsRect.Center(),
                Consts.RedRectColor, boundsRect.Extents, 0.1f);
            rectP.Particles[offset++] = particle;

            // left
            boundsRect = new PosRect(0, Consts.MapMargin, Consts.MapMargin - 1, Pos.GridH - Consts.MapMargin - 1);
            particle = ParticlesSys.ParticleAt(boundsRect.Center(),
                Consts.RedRectColor, boundsRect.Extents, 0.1f);
            rectP.Particles[offset++] = particle;

            // right
            boundsRect = new PosRect(Pos.GridW - Consts.MapMargin, Consts.MapMargin,
                Pos.GridW - 1, Pos.GridH - Consts.MapMargin - 1);
            particle = ParticlesSys.ParticleAt(boundsRect.Center(),
                Consts.RedRectColor, boundsRect.Extents, 0.1f);
            rectP.Particles[offset++] = particle;
            return offset;
        }

        public override bool Cancel() {
            selVFX?.HideAll();
            selVFX?.ShowRange(0);
            return base.Cancel();
        }

        public override void Deactivate() {
            Cancel();
            rotationStep = 0;
            origPositions = null;
            origSources.Clear();
            base.Deactivate();
        }

        public override void Activate() {
            choiceMode = ChoiceMode.Obj;
            hasValidationError = false;
            multiRects.Clear();
            rotationStep = 0;
        }

        public static void BeginTiles(HashSet<int> positions, Color rectColor, Func<int, Vector2, ChoosePosResult> onClick,
                Func<int, Vector2, HashSet<int>, bool> validatorMulti, List<Vector2> src = null, int range = 0,
                bool allowRotation = true) {
            var t = The.Toolbox.Get<ToolChoosePos>(ToolId);
            t.range = range;
            t.selectRectColor = rectColor;
            t.sources.Clear();
            var intialRekt = Pos.Encapsulate(positions);
            t.origMinX = intialRekt.MinX;
            t.origMinY = intialRekt.MinY;
            t.origW = intialRekt.Width;
            t.origH = intialRekt.Height;
            t.origPositions = new HashSet<int>(positions);
            t.origSources.Clear();
            var choiceMode = ChoiceMode.Tiles | ChoiceMode.ShowBounds;
            if (allowRotation) {
                choiceMode |= ChoiceMode.AllowRotation;
            }
            if (src == null) {
                t.origSources.Add(intialRekt.Center());
            } else {
                t.origSources.AddRange(src);
            }
            t.positions = positions;
            t.OnClickMulti = onClick;
            t.OnClick = null;
            t.ValidatorMulti = validatorMulti;
            t.Validator = null;
            t.rotationStep = 0;
            The.Toolbox.SetActive(t);
            t.choiceMode = choiceMode;
            // Seed the rotated view with the identity rotation so positions,
            // sources and origin are consistent with the original selection.
            t.ApplyRotation();
        }

        public static void BeginObj(List<Vector2> src, Func<Vector2, ChoosePosResult> onClick,
                Func<Vector2, (bool, Vector2)> validator = null) {
            var t = The.Toolbox.Get<ToolChoosePos>(ToolId);
            t.range = 0;
            t.positions = null;
            t.sources.Clear();
            t.sources.AddRange(src);
            t.OnClick = onClick;
            t.OnClickMulti = null;
            t.Validator = validator;
            t.ValidatorMulti = null;
            The.Toolbox.SetActive(t);
            t.choiceMode = ChoiceMode.Obj | ChoiceMode.ShowBounds;
        }

        public override bool HandlesRotate => true;
        public override void OnRotate(int direction) {
            if (!choiceMode.HasFlag(ChoiceMode.AllowRotation)) { return; }
            if (origPositions == null) { return; }
            rotationStep = ((rotationStep + direction) % 4 + 4) % 4;
            ApplyRotation();
            // Force OnHover to refresh marked tiles / rects for the new layout.
            lastPosIdx = Pos.Invalid;
            OnHover(The.Bindings.Mouse.WorldPosition);
        }

        /// <summary>
        /// Rotates a point expressed in local coordinates (0..w-1, 0..h-1)
        /// clockwise by <paramref name="step"/> 90° steps. Returns the new
        /// local coordinates in a rectangle whose size is swapped on odd steps.
        /// </summary>
        private static void RotateLocalPoint(int lx, int ly, int w, int h,
                int step, out int nlx, out int nly) {
            switch (step & 3) {
                case 1: nlx = ly; nly = w - 1 - lx; break;       // CW 90
                case 2: nlx = w - 1 - lx; nly = h - 1 - ly; break; // 180
                case 3: nlx = h - 1 - ly; nly = lx; break;       // CCW 90 (i.e. CW 270)
                default: nlx = lx; nly = ly; break;
            }
        }

        private static void RotateVec(float dx, float dy, int step,
                out float ndx, out float ndy) {
            switch (step & 3) {
                case 1: ndx = dy; ndy = -dx; break;      // CW 90 (y-up)
                case 2: ndx = -dx; ndy = -dy; break;
                case 3: ndx = -dy; ndy = dx; break;      // CCW 90
                default: ndx = dx; ndy = dy; break;
            }
        }

        private void ApplyRotation() {
            int newW = (rotationStep & 1) == 0 ? origW : origH;
            int newH = (rotationStep & 1) == 0 ? origH : origW;
            // Center the rotated bbox on the original bbox center (floor to
            // nearest integer cell for consistency across steps).
            newMinX = origMinX + Mathf.FloorToInt((origW - newW) / 2f);
            newMinY = origMinY + Mathf.FloorToInt((origH - newH) / 2f);

            // Rebuild the rotated position set.
            if (positions == null) {
                positions = new HashSet<int>(origPositions.Count);
            } else {
                positions.Clear();
            }
            foreach (var p in origPositions) {
                Pos.ToXY(p, out var px, out var py);
                RotateLocalPoint(px - origMinX, py - origMinY, origW, origH,
                    rotationStep, out var nlx, out var nly);
                positions.Add(Pos.FromXY(newMinX + nlx, newMinY + nly));
            }

            // Rebuild the rotated source points used by the selection VFX.
            var origCx = origMinX + origW * 0.5f;
            var origCy = origMinY + origH * 0.5f;
            var newCx = newMinX + newW * 0.5f;
            var newCy = newMinY + newH * 0.5f;
            sources.Clear();
            foreach (var s in origSources) {
                RotateVec(s.x - origCx, s.y - origCy, rotationStep,
                    out var ndx, out var ndy);
                sources.Add(new Vector2(newCx + ndx, newCy + ndy));
            }

            origin = new Vector2(newCx, newCy);
        }

        /// <summary>
        /// Returns the anchor position of a tile with the given current anchor
        /// and rotated dimensions, after the selection's current rotation is
        /// applied. Caller is expected to add <see cref="CurrentMotion"/> to
        /// reach the final destination.
        /// </summary>
        public int RotatedTileAnchor(int origAnchor, int curRotatedW, int curRotatedH) {
            if (rotationStep == 0 || origPositions == null) {
                return origAnchor;
            }
            Pos.ToXY(origAnchor, out var ax, out var ay);
            int lx1 = ax - origMinX;
            int ly1 = ay - origMinY;
            int lx2 = lx1 + curRotatedW - 1;
            int ly2 = ly1 + curRotatedH - 1;
            RotateLocalPoint(lx1, ly1, origW, origH, rotationStep,
                out var nlx1, out var nly1);
            RotateLocalPoint(lx2, ly2, origW, origH, rotationStep,
                out var nlx2, out var nly2);
            int newLx = Mathf.Min(nlx1, nlx2);
            int newLy = Mathf.Min(nly1, nly2);
            return Pos.FromXY(newMinX + newLx, newMinY + newLy);
        }

        /// <summary>
        /// Given a rotated-and-shifted destination position produced during
        /// hover, returns the position in the original (unrotated) selection
        /// that maps to it. Used by validators to locate the tile that will
        /// occupy <paramref name="rotatedPos"/> in the world.
        /// </summary>
        public int UnrotatePos(int rotatedPos) {
            if (rotationStep == 0 || origPositions == null) {
                return rotatedPos;
            }
            Pos.ToXY(rotatedPos, out var rx, out var ry);
            int lrx = rx - newMinX;
            int lry = ry - newMinY;
            int lx, ly;
            switch (rotationStep & 3) {
                // newLx=ly, newLy=W-1-lx  =>  ly=lrx, lx=W-1-lry
                case 1: lx = origW - 1 - lry; ly = lrx; break;
                // newLx=W-1-lx, newLy=H-1-ly  =>  lx=W-1-lrx, ly=H-1-lry
                case 2: lx = origW - 1 - lrx; ly = origH - 1 - lry; break;
                // newLx=H-1-ly, newLy=lx  =>  lx=lry, ly=H-1-lrx
                case 3: lx = lry; ly = origH - 1 - lrx; break;
                default: lx = lrx; ly = lry; break;
            }
            return Pos.FromXY(origMinX + lx, origMinY + ly);
        }

        /// <summary>
        /// Applies the current selection rotation to a single tile's rotation,
        /// honoring the tile's allowed rotations list.
        /// </summary>
        public Facing.Type RotateTileRotation(Facing.Type origRot,
                Facing.Type[] validRots) {
            if (rotationStep == 0) { return origRot; }
            return Facing.Rotate(origRot, rotationStep, validRots);
        }

        public Vector2 CurrentMotion => currentMotion;

        protected override void HideHover() {
            base.HideHover();
            multiRects.Clear();
            markedTiles.Clear();
            // Makes the next hover rebuild the outline, also on the same tile
            lastPosIdx = Pos.Invalid;
            selVFX?.GoalLinks.Stop();
        }

        public override void OnHover(Vector2 pos) {
            isHoverHidden = false;
            The.SysSig.SetCursor.Send(Cursor);
            var newPos = EntityUtils.ToSafe(pos);
            var isMulti = choiceMode.HasFlag(ChoiceMode.Tiles);
            var newPosIdx = EntityUtils.ToPos(newPos);
            if (isMulti) {
                if (newPosIdx == lastPosIdx) {
                    return;
                }
                lastPosIdx = newPosIdx;
            }
            lastPos = newPos;
            if (!isMulti) {
                isOutOfRange = !EntityUtils.IsWithinMargin(newPosIdx, false, true);
                return;
            }
            var moved = Caches.IntList;
            moved.Clear();
            var motion = pos - origin;
            int x = (int)lastPos.x - (int)origin.x;
            int y = (int)lastPos.y - (int)origin.y;
            currentMotion = new Vector2(x, y);
            markedTiles.Clear();
            hasValidationError = false;
            isOutOfRange = range > 0f && motion.magnitude > range;
            var w = Pos.GridW;
            foreach (var p in positions) {
                Pos.ToXY(p, out var px, out var py);
                px += x;
                py += y;
                // A tile that would leave the map can't be validated or previewed, but it
                // must fail the move - Pos.NeighborAt would wrap it into another column
                if (px < 0 || px >= w) {
                    hasValidationError = true;
                    continue;
                }
                if (py < 0 || py >= w) {
                    hasValidationError = true;
                    continue;
                }
                var mP = Pos.FromXY(px, py);
                moved.Add(mP);
                if (ValidatorMulti?.Invoke(mP, currentMotion, positions) == false) {
                    hasValidationError = true;
                    if (!isOutOfRange) {
                        AddMarkedTile(mP);
                    }
                }
            }
            multiRects.Clear();
            if (!isOutOfRange) {
                if (positions.Count < 100) {
                    PosRect.Rectangulate(moved, multiRects);
                } else {
                    PosRectBurstExtensions.RectangulateBurst(moved, multiRects);
                }
            }
        }

        public override void OnClickDown(Vector2 clickPos) {
            if (clickPos.x < 1f && clickPos.y < 1f) {
                UISounds.PlayActionDenied();
                return;
            }
            var targetPos = lastPos;
            if (Validator != null) {
                var val = Validator(targetPos);
                if (!val.Item1) {
                    UISounds.PlayActionDenied();
                    // Wrong spot
                    return;
                }
                targetPos = val.Item2;
            }
            ChoosePosResult res = default;
            if (hasValidationError || isOutOfRange) {
                S.Sig.ShowErrorMessage.Send(
                    $"{T.OperationForbidden}: {T.WarningInvalidLocation}");
                return;
            }
            if (choiceMode.HasFlag(ChoiceMode.Tiles)) {
                if (OnClickMulti != null) {
                    res = OnClickMulti.Invoke(Pos.FromVector(origin), targetPos);
                }
            } else if (OnClick != null) {
                res = OnClick.Invoke(targetPos);
            }
            if (res.IsSuccess) {
                UISounds.PlayButtonClick();
            } else {
                UISounds.PlayActionDenied();
            }
            if (res.ClearTool) {
                S.Toolbox.Clear();
            }
        }

        public override int Cursor =>
            (!(lastPos.x < 1f && lastPos.y < 1f) || Validator == null || Validator(lastPos).Item1) ?
                CursorTypeH.CursorTarget : CursorTypeH.CursorCross;

        public float Range => range;
        public Color RadiusColor => isOutOfRange ? Consts.ColorChartRed : Consts.GreenRectColor;
        public bool RadiusSameArea => false;

        public override string ToString() {
            return Id;
        }
    }
}
