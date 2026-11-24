using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Systems.Ships {
    public enum ShipMarkerMode {
        NPCShip,
        OurShip,
        CornerSection,
        // A single entity, e.g. a landed capsule (UnseenObjectsSys)
        Entity,
    }

    /// <summary>
    /// Handles the marker pointing to an NPC ship when it's off-screen
    /// </summary>
    public sealed class ShipOffScreenMarker : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, ICustomPoolable {
        public ShipView shipView;
        private Transform shipViewTransform;
        [SerializeField] private SpriteRenderer arrow;
        [SerializeField] private Collider col;
        [SerializeField] private Color colorEnemy = Consts.RedMarkedTileColor;
        [SerializeField] private Color colorUs = Consts.GreenMarkedTileColor;
        [SerializeField] private Color colorDerelict = Consts.YellowMarkedTileColor;
        public ShipMarkerMode Mode;
        // Markers drawing an arrow at the screen edge right now, in the order they
        // appeared. A marker only ever steps aside for the ones before it, so there
        // is no feedback loop between two markers avoiding each other. An arrow next
        // to its on-screen target is not here: it is anchored to its own object.
        private static readonly List<ShipOffScreenMarker> edgeArrows = new();
        private static int layoutFrame = -1;
        // How fast an arrow eases into the position the layout gave it
        private const float moveSpeed = 16f;
        // Bob toward the target while next to it, as a fraction of the arrow size
        private const float nearBob = 0.15f;
        private PosRect cornerBounds;
        private bool hasCornerTarget;
        private Entity entityTarget;
        private Action<Entity> onEntityClick;
        private bool wasOffScreen;
        private bool isShowingCursorOverride;
        // Where this marker would sit with no separation at all. Depends only on
        // the camera and this marker's own target, never on another marker, which
        // is what keeps the layout from chasing its own output
        private Vector2 rawScreenPos;
        private bool hasRawScreenPos;
        private bool isClampedX;
        private float rawScreenDepth;
        private Vector2 wantScreenPos;
        private Vector2 screenPos;
        private bool hasScreenPos;
        // Entity arrow sitting next to its on-screen target, offset in screen pixels
        private bool isNearTarget;
        private Vector2 nearOffset;
        private float targetHalfHeight;
        private GameState S;

        private void Start() {
            if (Mode == ShipMarkerMode.NPCShip) {
                shipViewTransform = shipView.transform;
            }
            ApplyArrowColor();
            IsShipVisible = true;
            wasOffScreen = false;
            SetShowing(false);
        }

        // Whether the marker logic wants the arrow out. What is drawn also depends
        // on the UI being hidden: the arrow is a world sprite, not part of the
        // canvas that ToggleUI fades out
        private bool isShown;
        internal bool IsArrowShown => isShown;

        private void SetShowing(bool on) {
            isShown = on;
            ApplyVisibility();
            SetInEdgeLayout(on);
            if (!on) {
                hasScreenPos = false;
                isNearTarget = false;
            }
        }

        private void ApplyVisibility() {
            var visible = isShown && !ToggleUI.IsUIHidden;
            if (arrow.enabled != visible) {
                arrow.enabled = visible;
                col.enabled = visible;
            }
        }

        private void SetInEdgeLayout(bool on) {
            // The set changed, so whatever layout this frame already produced is
            // stale - a marker joining must get a resolved position on its very
            // first frame, not a leftover one
            layoutFrame = -1;
            if (on) {
                if (edgeArrows.Contains(this)) { return; }
                if (Mode == ShipMarkerMode.OurShip) {
                    // Our own ship stays exactly where it points, everything
                    // else slides around it
                    edgeArrows.Insert(0, this);
                } else {
                    edgeArrows.Add(this);
                }
            } else {
                edgeArrows.Remove(this);
                hasRawScreenPos = false;
            }
        }

        private void ApplyArrowColor() {
            switch (Mode) {
                case ShipMarkerMode.OurShip:
                    arrow.color = colorUs;
                    break;
                case ShipMarkerMode.CornerSection:
                case ShipMarkerMode.Entity:
                    arrow.color = colorDerelict;
                    break;
                default:
                    arrow.color = shipView.Ship.IsCombatAlly ? colorUs : colorEnemy;
                    break;
            }
        }

        // Pooled markers only run Start() once in their lifetime, so a recycled
        // instance would keep the color of whatever it pointed at before. Corner
        // markers are set up explicitly instead.
        public void SetupAsCornerMarker() {
            Mode = ShipMarkerMode.CornerSection;
            ApplyArrowColor();
            ClearCornerTarget();
        }

        public void SetCornerTarget(PosRect bounds) {
            cornerBounds = bounds;
            hasCornerTarget = true;
        }

        public void ClearCornerTarget() {
            hasCornerTarget = false;
            wasOffScreen = false;
            SetShowing(false);
        }

        // Pooled like the corner markers, so everything is set explicitly
        public void SetupAsEntityMarker(Entity target, Action<Entity> onClick) {
            Mode = ShipMarkerMode.Entity;
            entityTarget = target;
            onEntityClick = onClick;
            // Read once: Obj.Size goes through its graphics, which can be gone for
            // a few ticks before a destroyed target is dismissed
            targetHalfHeight = target.Size.y * 0.5f;
            ApplyArrowColor();
            wasOffScreen = false;
            SetShowing(false);
        }

        public void ClearEntityTarget() {
            entityTarget = null;
            onEntityClick = null;
            wasOffScreen = false;
            SetShowing(false);
        }

        public bool IsShipVisible;

        private void OnEnable() {
            S = The.R?.State;
            IsShipVisible = true;
        }

        private void LateUpdate() {
            if (!GameState.IsCurrent(S)) { return; }
            if (Mode == ShipMarkerMode.CornerSection && !hasCornerTarget) { return; }
            if (Mode == ShipMarkerMode.Entity && entityTarget == null) { return; }
            ApplyVisibility();
            var mainCam = RenderUtils.Camera;
            bool isOffScreen = CheckOffScreen(mainCam);
            IsShipVisible = !isOffScreen;
            Vector3 snapPos;
            switch (Mode) {
                case ShipMarkerMode.OurShip:
                    snapPos = S.Sys.Areas.ShipBounds.Center();
                    RenderUtils.IsShipInCameraView = IsShipVisible;
                    break;
                case ShipMarkerMode.CornerSection:
                    snapPos = cornerBounds.Center();
                    break;
                case ShipMarkerMode.Entity:
                    snapPos = entityTarget.Position;
                    break;
                default:
                    snapPos = shipViewTransform.position;
                    break;
            }

            if (isOffScreen) {
                if (!wasOffScreen) {
                    // Coming from next to the target keeps the screen position,
                    // so the arrow eases back out to the edge
                    isNearTarget = false;
                    SetShowing(true);
                    wasOffScreen = true;
                    if (Mode == ShipMarkerMode.NPCShip) {
                        S.Sig.ShipViewVisibilityChanged.Send(shipView, false);
                    } else if (Mode == ShipMarkerMode.OurShip) {
                        RenderUtils.IsShipInCameraView = false;
                    }
                }
                PublishRawScreenPos(mainCam, snapPos);
                EnsureLayout();
                if (hasScreenPos) {
                    screenPos = Vector2.Lerp(screenPos, wantScreenPos,
                        1f - Mathf.Exp(-moveSpeed * GameTimer.unscaledDeltaTime));
                } else {
                    // Freshly shown, so there is nothing to ease from
                    screenPos = wantScreenPos;
                    hasScreenPos = true;
                }
                PlaceArrow(mainCam, snapPos, screenPos);
            } else if (Mode == ShipMarkerMode.Entity) {
                UpdateNearTarget(mainCam, snapPos);
            } else {
                if (wasOffScreen) {
                    SetShowing(false);
                    wasOffScreen = false;
                    if (Mode == ShipMarkerMode.NPCShip) {
                        S.Sig.ShipViewVisibilityChanged.Send(shipView, true);
                    }
                }
            }
        }

        private void PlaceArrow(Camera mainCam, Vector3 snapPos, Vector2 drawPos) {
            var tr = arrow.transform;
            Vector3 worldPos = mainCam.ScreenToWorldPoint(
                new Vector3(drawPos.x, drawPos.y, rawScreenDepth));
            tr.position = new Vector3(worldPos.x, worldPos.y, tr.position.z);
            Vector3 direction = snapPos - tr.position;
            tr.rotation = Quaternion.LookRotation(Vector3.forward, direction);
        }

        // A capsule on screen looks like everything around it, so an arrow that
        // vanished the moment it came into view left players asking what it had
        // pointed at. A ship needs no such help, which is why only entities stay.
        private void UpdateNearTarget(Camera mainCam, Vector3 snapPos) {
            Vector3 target = mainCam.WorldToScreenPoint(snapPos);
            rawScreenDepth = target.z;
            var targetPos = new Vector2(target.x, target.y);
            var want = NearTargetOffset(targetPos);
            if (!isNearTarget) {
                isNearTarget = true;
                if (wasOffScreen) {
                    wasOffScreen = false;
                    SetInEdgeLayout(false);
                } else {
                    isShown = true;
                    ApplyVisibility();
                }
                // Slide over from wherever the edge arrow was
                nearOffset = hasScreenPos ? screenPos - targetPos : want;
            }
            // Easing the offset rather than the screen position keeps the arrow
            // pinned to its target while the camera pans, instead of trailing it.
            // Angle and length ease apart, so a flip from above to below swings the
            // arrow around its target instead of dragging it across it.
            var k = 1f - Mathf.Exp(-moveSpeed * GameTimer.unscaledDeltaTime);
            var angle = Mathf.LerpAngle(Mathf.Atan2(nearOffset.y, nearOffset.x) * Mathf.Rad2Deg,
                Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
            var length = Mathf.Lerp(nearOffset.magnitude, want.magnitude, k);
            nearOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
            screenPos = targetPos + nearOffset;
            hasScreenPos = true;
            var bob = -nearOffset.normalized
                * (ArrowScreenSize() * nearBob * RenderUtils.PulseMid);
            PlaceArrow(mainCam, snapPos, screenPos + bob);
        }

        // Above the target pointing down, or below it when the top of the screen
        // has no room, always clear of its sprite and fully on screen
        private Vector2 NearTargetOffset(Vector2 target) {
            var arrowSize = ArrowScreenSize();
            var half = arrowSize * 0.5f;
            var ortho = RenderUtils.CameraOrtho;
            var pxPerUnit = ortho > 0f ? Screen.height / (2f * ortho) : 0f;
            var dist = targetHalfHeight * pxPerUnit + arrowSize * 0.75f;
            var y = target.y + dist + half <= Screen.height
                ? target.y + dist
                : target.y - dist;
            var pos = new Vector2(
                Mathf.Clamp(target.x, half, Screen.width - half),
                Mathf.Clamp(y, half, Screen.height - half));
            return pos - target;
        }

        // The position this marker wants before any other marker is considered
        private void PublishRawScreenPos(Camera mainCam, Vector3 snapPos) {
            Vector3 raw = mainCam.WorldToScreenPoint(snapPos);
            rawScreenDepth = raw.z;
            GetClampBounds(out var clampX, out var clampY,
                out var maxX, out var maxY);
            isClampedX = raw.x <= clampX || raw.x >= maxX;
            rawScreenPos = new Vector2(
                Mathf.Clamp(raw.x, clampX, maxX),//- edgePadding);
                Mathf.Clamp(raw.y, clampY, maxY)); //- edgePadding);
            hasRawScreenPos = true;
        }

        private static void GetClampBounds(out float clampX, out float clampY,
                out float maxX, out float maxY) {
            clampX = RenderUtils.ScreenWidth * 0.20f;
            clampY = RenderUtils.ScreenHeight * 0.10f;
            // Mostly because of quests widget
            var scale = Video.GetCurrentUIScale(out _);
            var minX = 420 * scale;
            var minY = 200 * scale;
            if (clampX < minX) { clampX = minX; }
            if (clampY < minY) { clampY = minY; }
            maxX = Screen.width - clampX;
            maxY = Screen.height - clampY;
        }

        // Places every edge marker in one deterministic pass, once per frame,
        // reading only raw positions. A marker resolving against the others one at
        // a time would be reading placements that depend on its own, which is what
        // made arrows flip between overlapping and separated every frame.
        private static void EnsureLayout() {
            if (layoutFrame == GameTimer.frameCount) { return; }
            layoutFrame = GameTimer.frameCount;
            GetClampBounds(out var clampX, out var clampY,
                out var maxX, out var maxY);
            var midX = Screen.width * 0.5f;
            var midY = Screen.height * 0.5f;
            for (int i = 0; i < edgeArrows.Count; i++) {
                var m = edgeArrows[i];
                if (!m.hasRawScreenPos) { continue; }
                var pos = m.rawScreenPos;
                var minDist = m.ArrowScreenSize() * 1.15f;
                if (minDist > 0f) {
                    // Stack toward the middle of the screen, so the edge clamp
                    // never pushes an arrow back onto the one it just cleared
                    var tangent = m.isClampedX ? Consts.V2Up : Consts.V2Right;
                    if (m.isClampedX ? pos.y > midY : pos.x > midX) {
                        tangent = -tangent;
                    }
                    var minDistSqr = minDist * minDist;
                    for (int k = 0; k < i; k++) {
                        var other = edgeArrows[k];
                        if (!other.hasRawScreenPos) { continue; }
                        var delta = pos - other.wantScreenPos;
                        if (delta.sqrMagnitude >= minDistSqr) { continue; }
                        pos += tangent
                            * (minDist - Mathf.Abs(Vector2.Dot(delta, tangent)));
                    }
                }
                m.wantScreenPos = new Vector2(
                    Mathf.Clamp(pos.x, clampX, maxX),
                    Mathf.Clamp(pos.y, clampY, maxY));
            }
        }

        // FixedScaleOnZoom keeps the arrow at a constant fraction of screen height,
        // so the gap between arrows tracks their actual size at any zoom level
        private float ArrowScreenSize() {
            var sprite = arrow.sprite;
            if (sprite == null) { return 0f; }
            var ortho = RenderUtils.CameraOrtho;
            if (ortho <= 0f) { return 0f; }
            return sprite.bounds.size.y * arrow.transform.lossyScale.y
                * Screen.height / (2f * ortho);
        }

        private bool CheckOffScreen(Camera mainCam) {
            switch (Mode) {
                case ShipMarkerMode.OurShip:
                    return IsRectInvisible(mainCam, S.Sys.Areas.ShipBounds);
                case ShipMarkerMode.CornerSection:
                    return IsRectInvisible(mainCam, cornerBounds);
                case ShipMarkerMode.Entity:
                    return IsPointInvisible(mainCam, entityTarget.Position, 1f);
                default:
                    return IsPointInvisible(mainCam, shipViewTransform.position,
                        shipView.Ship.Width);
            }
        }

        private bool IsRectInvisible(Camera mainCam, PosRect b) {
            if (!b.IsValid) { return false; }
            var bl = b.BottomLeftV2;
            var br = b.BottomRightV2;
            var tl = b.TopLeftV2;
            var tr = b.TopRightV2;

            var w = Screen.width;
            var h = Screen.height;
            var sbl = mainCam.ScreenToWorldPoint(new Vector2(0f, 0f));
            var sbr = mainCam.ScreenToWorldPoint(new Vector2(w, 0f));
            var str = mainCam.ScreenToWorldPoint(new Vector2(w, h));
            var stl = mainCam.ScreenToWorldPoint(new Vector2(0f, h));

            return !Maths.AreTwoRectsIntersecting(
                // rect one corners
                bl, br, tl, tr,
                // rect two corners
                sbl, sbr, stl, str);

        }

        private bool IsPointInvisible(Camera mainCam, Vector3 point, float radius) {
            var viewPos = mainCam.WorldToViewportPoint(point);
            var screenRadius = radius / Mathf.Max(mainCam.pixelWidth, mainCam.pixelHeight);
            return viewPos.x < -screenRadius || viewPos.x > 1 + screenRadius
                || viewPos.y < -screenRadius || viewPos.y > 1 + screenRadius;
            // return viewPos.x < 0 || viewPos.x > 1
            //     || viewPos.y < 0 || viewPos.y > 1;
        }

        private bool IsViewPosInvisible(Vector2 viewPos) {
            return viewPos.x < 0 || viewPos.x > 1
                || viewPos.y < 0 || viewPos.y > 1;
        }

        private void OnDisable() {
            // Covers pool despawn and scene teardown, so a hidden or destroyed
            // marker can never push the live ones away from a ghost position
            SetShowing(false);
            wasOffScreen = false;
        }

        private void OnDestroy() {
            S = null;
        }

        public void PoolDeactivate() {
            HideCursorOverride();
            gameObject.SetActive(false);
            S = null;
        }

        public void PoolActivate() {
            gameObject.SetActive(true);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            isShowingCursorOverride = true;
            The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            UISounds.PlayHover(UISounds.HoverType.Low);
            switch (Mode) {
                case ShipMarkerMode.OurShip:
                    The.SysSig.ShowTooltip.Send(T.OurShip, true);
                    break;
                case ShipMarkerMode.CornerSection:
                    The.SysSig.ShowTooltip.Send(T.SpaceObjectShipderelict, true);
                    break;
                case ShipMarkerMode.Entity:
                    The.SysSig.ShowTooltip.Send(entityTarget?.Definition?.NameT, true);
                    break;
                default:
                    The.SysSig.ShowTooltip.Send(shipView.Ship.NameLinked, true);
                    break;
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            HideCursorOverride();
            The.SysSig.HideTooltip.Send();
        }

        private void HideCursorOverride() {
            if (isShowingCursorOverride) {
                isShowingCursorOverride = false;
                The.SysSig.SetCursorOverride.Send(0);
            }
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (eventData.button == PointerEventData.InputButton.Middle) { return; }
            if (MouseUtils.FirstUnderCursor != gameObject) { return; }
            UISounds.PlayClick(UISounds.ClickType.Big, true);
            switch (Mode) {
                case ShipMarkerMode.OurShip:
                    EntityUtils.CameraJumpToShip();
                    break;
                case ShipMarkerMode.CornerSection:
                    EntityUtils.CameraFocusOn(cornerBounds.Center(), cornerBounds.Extents);
                    break;
                case ShipMarkerMode.Entity:
                    // The callback usually despawns this marker, so it goes last
                    HideCursorOverride();
                    The.SysSig.HideTooltip.Send();
                    onEntityClick?.Invoke(entityTarget);
                    break;
                default:
                    shipView.JumpCamera();
                    break;
            }
        }
    }
}
