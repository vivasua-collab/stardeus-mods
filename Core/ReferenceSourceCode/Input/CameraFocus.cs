using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Input {
    public sealed class CameraFocus {
        private Entity followedEntity;
        private readonly GameState S;
        private readonly CameraControls Controls;
        private Entity linkSource;
        private Entity linkDest;
        private readonly UIGuidelines linkGuidelines;
        private readonly UIGuidelines focusGuidelines;
        private Vector2 linkPos;
        public Vector2 FocusedPos;
        private Color linkColor;
        private readonly Camera cam;
        private bool hasLink;
        private bool isLinkSticky;
        private bool wasShowingLink;
        private bool skipMoveToFocus;
        private Vector2 focusObjSize;
        private float focusTimeLeft;
        private float focusTimeStart;
        private const float maxFocusTime = 5f;
        // What the focus flash outlines when it is not a link (a focused tile, a followed being)
        private Entity focusEntity;
        // A tile set (a room, a ship section) a link points at, or the focus flash outlines
        private ICollection<int> linkTiles;
        private ICollection<int> focusTiles;
        private readonly FocusOutlineRenderer outline;
        // A link target's rectangle, counter-clockwise, for its tangent corners
        private readonly List<Vector2> boxCorners = new(4);

        public CameraFocus(GameState s, CameraControls controls, Camera cam) {
            Controls = controls;
            S = s;
            this.cam = cam;
            linkGuidelines = UIGuidelines.Create("GuidelineUI");
            focusGuidelines = UIGuidelines.Create("GuidelineUI", lineWidth: 3f);
            outline = new FocusOutlineRenderer(s, cam);
            S.Sig.TilesSelected.AddListener(OnTilesSelected);
            S.Sig.ObjSelected.AddListener(OnObjSelected);
            S.Sig.FollowEntity.AddListener(OnFollowEntity);
            S.Sig.CameraFocus.AddListener(OnCameraFocus);
            S.Sig.CameraFocusEntity.AddListener(OnCameraFocusEntity);
            S.Sig.CameraJump.AddListener(OnCameraJumpTo);
            S.Sig.CameraFocusRooted.AddListener(OnCameraFocusRooted);
            S.Sig.CameraShowLink.AddListener(OnCameraShowLink);
            S.Sig.CameraShowPosLink.AddListener(OnCameraShowPosLink);
            S.Sig.CameraShowTilesLink.AddListener(OnCameraShowTilesLink);
            S.Sig.CameraFocusTiles.AddListener(OnCameraFocusTiles);
            S.Query.CameraHideLink.SetResponder(OnCameraHideLink);
            S.Query.CameraSetLinkSticky.SetResponder(OnCameraSetLinkSticky);
        }

        public void Unsubscribe() {
            S?.Sig?.TilesSelected.RemoveListener(OnTilesSelected);
            S?.Sig?.ObjSelected.RemoveListener(OnObjSelected);
            S?.Sig?.FollowEntity.RemoveListener(OnFollowEntity);
            S?.Sig?.CameraFocus.RemoveListener(OnCameraFocus);
            S?.Sig?.CameraFocusEntity.RemoveListener(OnCameraFocusEntity);
            S?.Sig?.CameraJump.RemoveListener(OnCameraJumpTo);
            S?.Sig?.CameraFocusRooted.RemoveListener(OnCameraFocusRooted);
            S?.Sig?.CameraShowLink.RemoveListener(OnCameraShowLink);
            S?.Sig?.CameraShowPosLink.RemoveListener(OnCameraShowPosLink);
            S?.Sig?.CameraShowTilesLink.RemoveListener(OnCameraShowTilesLink);
            S?.Sig?.CameraFocusTiles.RemoveListener(OnCameraFocusTiles);
            S?.Query?.CameraHideLink.UnsetResponder(OnCameraHideLink);
            outline.Dispose();
        }

        // The focus flash and link color, a theme color of its own: over the map the
        // magenta reads better than the UI accent
        public static Color FlashColor() {
            return The.UITheme?.GetColor("FocusFlash", nameof(CameraFocus))
                ?? Color.magenta;
        }

        public void FocusOnFollowedBeings() {
            if (followedEntity != null) {
                var fp = followedEntity.Position;
                var lockPos = Vectors.SetZ(fp, Controls.PrecisePosition.z);
                Controls.WantPosition = lockPos;
                // EnsurePositionIsVisible(fp);
            }
        }

        public void UnfollowEntity() {
            followedEntity = null;
        }
        private bool isHidden;

        /// <summary>
        /// Mutes focus rects and link cones, e.g. during a cutscene that must not have
        /// a pulsing selection outline flashing over it
        /// </summary>
        public void SetHidden(bool hidden) {
            isHidden = hidden;
            if (hidden) {
                ClearGuidelines();
            }
        }

        private void ClearGuidelines() {
            if (wasShowingLink) {
                linkGuidelines.Dispose();
                wasShowingLink = false;
            }
            hasLink = false;
            linkSource = null;
            linkDest = null;
            linkTiles = null;
            focusEntity = null;
            focusTiles = null;
            outline.Hide();
            if (focusTimeLeft > 0f) {
                focusTimeLeft = 0f;
                focusGuidelines.ClearAll();
            }
        }

        public void UpdateGuidelines() {
            if (isHidden) {
                // Focus requests keep arriving while hidden (e.g. the next cutscene step),
                // so they have to be dropped here too, or they'd flash once unhidden
                ClearGuidelines();
                return;
            }
            Vector2 to;
            Vector2 size;
            if (linkDest != null) {
                to = linkDest.Position;
            } else if (focusEntity?.IsActive == true) {
                // A followed being moves, so a fallback rectangle has to move with it
                to = focusEntity.Position;
            } else {
                to = FocusedPos;
            }
            var isOutlined = UpdateOutline();
            if (hasLink) {
                Vector2 from;
                if (linkSource == null) {
                    // It's a link to mouse cursor
                    from = The.Bindings.Mouse.WorldPosition;
                } else {
                    from = linkSource.Position;
                }
                // Two lines that just touch the target's shape, as seen from the source
                if (isOutlined) {
                    // The outline marks the target, the lines only point at it. None until
                    // the shape is read back, or while the source is over the target.
                    if (outline.TryGetTangents(from, out var a, out var b)) {
                        linkGuidelines.ShowFan(from, a, b, linkColor, cam);
                    } else {
                        linkGuidelines.HideLines();
                    }
                } else {
                    if (linkDest == null) {
                        to = linkPos;
                        size = focusObjSize;
                    } else {
                        to = linkDest.Position;
                        size = linkDest.Size;
                    }
                    var box = new Rect(to - size * 0.5f, size);
                    boxCorners.Clear();
                    boxCorners.Add(new Vector2(box.xMin, box.yMin));
                    boxCorners.Add(new Vector2(box.xMax, box.yMin));
                    boxCorners.Add(new Vector2(box.xMax, box.yMax));
                    boxCorners.Add(new Vector2(box.xMin, box.yMax));
                    if (FocusOutlineRenderer.TryTangents(from, boxCorners,
                            out var a, out var b)) {
                        linkGuidelines.ShowFan(from, a, b, linkColor, cam, box);
                    } else {
                        linkGuidelines.ShowRect(to, size, linkColor, cam);
                    }
                }
                wasShowingLink = true;
            } else if (wasShowingLink) {
                linkGuidelines.Dispose();
                wasShowingLink = false;
            }
            if (focusTimeLeft > 0f) {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    focusTimeLeft = 0f;
                }
                #endif
                focusTimeLeft -= GameTimer.unscaledDeltaTime;
                if (isOutlined) {
                    focusGuidelines.ClearAll();
                } else {
                    var color = hasLink ? linkColor : FlashColor();
                    color.a = (0.25f + RenderUtils.PulseFast * 0.75f)
                        * (focusTimeLeft / maxFocusTime);
                    focusGuidelines.ShowRect(to, focusObjSize, color, cam);
                }
                if (focusTimeLeft <= 0f) {
                    focusGuidelines.Dispose();
                }
            }
        }

        // The silhouette outline of the link target for as long as the link shows (as the
        // cone's rectangle did), or of the focused entity or tile set while the focus flash
        // fades out. False when there is nothing to outline: positions have no shape, and a
        // hidden being has no sprites, so those keep the rectangle.
        private bool UpdateOutline() {
            Entity target = null;
            ICollection<int> tiles = null;
            var color = default(Color);
            var pulse = 0.25f + RenderUtils.PulseFast * 0.75f;
            if (hasLink && (linkDest != null || linkTiles != null)) {
                target = linkDest;
                tiles = linkTiles;
                color = linkColor;
                color.a *= pulse;
            } else if (!hasLink && (focusEntity != null || focusTiles != null)
                    && focusTimeLeft > 0f) {
                target = focusEntity;
                tiles = focusTiles;
                color = FlashColor();
                color.a *= pulse * (focusTimeLeft / maxFocusTime);
            }
            // Only a link needs the shape read back, for its lines
            if (target != null && outline.Show(target, color, trackEdges: hasLink)) {
                return true;
            }
            if (tiles != null && outline.ShowTiles(tiles, color)) {
                return true;
            }
            outline.Hide();
            return false;
        }

        public bool TryFollowEntity(float camOrtho) {
            if (followedEntity == null) { return false; }
            var pp = Controls.PrecisePosition;
            // var wp = followedEntity.Position;
            Controls.ZoomWithChildren(camOrtho);
            Controls.MoveWithChildren(new Vector3(pp.x, pp.y, -camOrtho));
            return true;
        }

        public void Reset() {
            UnfollowEntity();
            OnCameraHideLink();
        }
        private void OnFollowEntity(Entity ntt) {
            // D.Warn("Following entity: {0}", ntt);
            if (G.ClockSpeed > ClockSpeed.Fast2) {
                // no point in trying to focus the camera when unlimited speed is running,
                // it will be 1FPS
                return;
            }
            followedEntity = ntt;
            if (ntt != null) {
                var wo = Consts.CameraFocusZoomLevel * 0.5f;
                if (RenderUtils.CameraOrtho < wo) {
                    var pp = Controls.PrecisePosition;
                    var nttp = ntt.Position;
                    wo = Mathf.Lerp(RenderUtils.CameraOrtho, wo,
                        Vector2.Distance(pp, nttp) / 128f);
                }
                Controls.WantOrtho = wo;
                Controls.WantPosition = ntt.Position;
                if (Ready.Gen) {
                    hasLink = false;
                    linkSource = null;
                    linkDest = null;
                    linkTiles = null;
                    focusTiles = null;
                    focusEntity = ntt;
                    focusObjSize = ntt.Size;
                    focusTimeLeft = maxFocusTime;
                    focusTimeStart = GameTimer.unscaledDeltaTime;
                }
            }
        }

        private void OnCameraJumpTo(Vector2 point, Vector2 objSize, float dist) {
            followedEntity = null;
            var focus = Vectors.SetZ(point, Controls.WantPosition.z);
            bool autoDist = dist == 0;
            if (autoDist) {
                dist = SetAutoDist(objSize);
            }
            Controls.TeleportTo(focus, dist / 2f);
            focusGuidelines.ClearAll();
            hasLink = false;
            linkSource = null;
            linkDest = null;
            linkTiles = null;
            focusEntity = null;
            focusTiles = null;
            focusTimeLeft = 0f;
            focusTimeStart = 0f;
        }

        private void OnCameraFocus(Vector2 point, Vector2 objSize, float dist) {
            FocusOn(point, objSize, dist);
        }

        // A tile (EntityUtils.CameraFocusOn): the flash outlines its silhouette
        private void OnCameraFocusEntity(Entity entity) {
            if (entity == null) { return; }
            if (FocusOn(entity.Position, entity.Size, Consts.CameraFocusZoomLevel)) {
                focusEntity = entity;
            }
        }

        // True when the focus flash started
        private bool FocusOn(Vector2 point, Vector2 objSize, float dist) {
            if (G.ClockSpeed > ClockSpeed.Fast2) {
                // no point in trying to focus the camera when unlimited speed is running,
                // it will be 1FPS
                return false;
            }
            skipMoveToFocus = ShipFactory.IsGenDebug;
            FocusedPos = point;
            followedEntity = null;
            focusEntity = null;
            focusTiles = null;
            var focus = Vectors.SetZ(FocusedPos, Controls.WantPosition.z);
            bool autoDist = dist == 0;
            if (autoDist) {
                dist = SetAutoDist(objSize);
            }
            if (!skipMoveToFocus) {
                Controls.WantPosition = AdjustFocusedPos(focus, dist);
                Controls.WantOrtho = dist / 2f;
            }
            if (objSize.x > 0 && objSize.y > 0) {
                hasLink = false;
                linkSource = null;
                linkDest = null;
                linkTiles = null;
                if (Ready.Gen) {
                    focusTimeLeft = maxFocusTime;
                    focusObjSize = objSize;
                    focusTimeStart = GameTimer.unscaledDeltaTime;
                    return true;
                }
            }
            return false;
        }

        // Public for a focus that frames an area without flashing an outline around it:
        // send the result as the distance with a zero object size
        public static float SetAutoDist(Vector2 objSize) {
            float dist;
            dist = objSize.y * 1.1f;
            if (dist < Consts.CameraFocusZoomLevel * 0.5f) {
                return Consts.CameraFocusZoomLevel * 0.5f;
            }
            var aspect = objSize.y / objSize.x;
            var dAspect = Screen.height / (float)Screen.width;
            if (aspect < dAspect) {
                dist *= dAspect / aspect;
            }
            return dist;
        }

        private Vector3 AdjustFocusedPos(Vector3 posInWorld, float dist) {
            /*
            1. get current world pos of game cam
            2. check if current world pos is behind UI (if not, just return pos)
            3. find position on screen that is not behind ui (try bottom left)
            4. ask game cam where in world the new screen pos is
            5. get the vector between current cam pos and pos from step 4 (v1)
            6. see the ortho size ratio between current game cam pos and future pos (r1)
            7. adj focus = posInWorld + (v1 / r1)
            */
            var curWorldPos = Controls.PrecisePosition;
            var isBehindUI = MouseUtils.IsUIAt(new Vector2(
                Screen.width / 2f, Screen.height / 2f));
            if (!isBehindUI) { return posInWorld; }
            // try bottom left
            var screenSafePos = new Vector3(Screen.width / 3f, Screen.height / 3f);
            if (MouseUtils.IsUIAt(screenSafePos)) {
                // try other corner (bottom right)
                screenSafePos = new Vector3(Screen.width * 0.666f, Screen.height / 3f);
                if (MouseUtils.IsUIAt(screenSafePos)) {
                    // give up -
                    return posInWorld;
                }
            }

            var screenSafePosInWorld = cam.ScreenToWorldPoint(screenSafePos);
            var v1 = curWorldPos - screenSafePosInWorld;
            var curOrtho = Controls.PreciseOrtho;
            var newOrtho = dist / 2f;
            var r1 = newOrtho / curOrtho;
            return posInWorld + (v1 * r1);
        }

        private void OnCameraShowLink(Entity root, Entity target, Color color) {
            linkTiles = null;
            if (target == null) {
                D.Err("Target is null for camera show link from root {0}", root);
                hasLink = false;
                linkSource = null;
                linkDest = null;
                return;
            }
            if (!target.IsActive) {
                hasLink = false;
                linkSource = null;
                linkDest = null;
                return;
            }
            hasLink = true;
            linkSource = root;
            linkDest = target;
            linkColor = color;
            focusObjSize = target.Size;
            focusTimeLeft = maxFocusTime;
            focusTimeStart = GameTimer.unscaledDeltaTime;
        }
        private void OnCameraShowPosLink(Entity root, Vector2 target,
                Vector2 scale, Color color) {
            hasLink = true;
            linkSource = root;
            linkDest = null;
            linkTiles = null;
            linkPos = target;
            linkColor = color;
            focusObjSize = scale;
            focusTimeLeft = maxFocusTime;
            focusTimeStart = GameTimer.unscaledDeltaTime;
        }

        // A link to the tiles' outline; their bounding rectangle when it cannot be drawn
        private void OnCameraShowTilesLink(Entity root, ICollection<int> tiles, Color color) {
            if (tiles == null || tiles.Count == 0) { return; }
            var r = Pos.Encapsulate(tiles);
            OnCameraShowPosLink(root, r.Center(), r.Extents, color);
            linkTiles = tiles;
        }

        // CameraFocus on the tiles' bounding rectangle, flashing their outline
        private void OnCameraFocusTiles(ICollection<int> tiles) {
            if (tiles == null || tiles.Count == 0) { return; }
            var r = Pos.Encapsulate(tiles);
            if (FocusOn(r.Center(), r.Extents, 0)) {
                focusTiles = tiles;
            }
        }

        private bool OnCameraHideLink() {
            if (hasLink) {
                if (isLinkSticky) {
                    return false;
                }
                focusGuidelines.ClearAll();
                focusTimeLeft = 0;
                focusTimeStart = 0;
                hasLink = false;
                linkSource = null;
                linkDest = null;
                linkTiles = null;
                focusEntity = null;
                focusTiles = null;
                return true;
            }
            return false;
        }
        private bool OnCameraSetLinkSticky(bool sticky) {
            isLinkSticky = sticky;
            return hasLink;
        }

        private void OnCameraFocusRooted(Entity root, Entity point) {
            if (root == null || point == null) { return; }
            if (G.ClockSpeed > ClockSpeed.Fast2) {
                // no point in trying to focus the camera when unlimited speed is running,
                // it will be 1FPS
                return;
            }
            var p1 = root.PosIdx;
            var p2 = point.PosIdx;
            var posRect = Pos.Encapsulate(p1, p2);
            if (!skipMoveToFocus) {
                Controls.WantPosition = posRect.Center();
                var dist = Mathf.Max(posRect.Width, posRect.Height) * 1.1f;
                dist = Mathf.Max(dist, Consts.CameraFocusZoomLevel * 0.5f);
                Controls.WantOrtho = dist / 2f;
                Controls.PlayerZooming = false;
            }
            if (point.Size.x > 0 && point.Size.y > 0) {
                hasLink = true;
                focusObjSize = point.Size;
                linkSource = root;
                linkDest = point;
                linkTiles = null;
                linkColor = FlashColor();
                focusTimeLeft = maxFocusTime;
                focusTimeStart = GameTimer.unscaledDeltaTime;
            }
        }

        private void OnTilesSelected(List<Tile> tiles) {
            followedEntity = null;
        }

        private void OnObjSelected(List<Obj> objs) {
            followedEntity = null;
        }
    }
}
