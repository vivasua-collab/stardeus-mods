using System;
using Game.Rendering;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Space {
    public sealed class UISpaceObjectSelected : WBase {
        private RectTransform tr;
        private RectTransform scaleParent;
        [SerializeField] private RectTransform arrowBL;
        [SerializeField] private RectTransform arrowBR;
        [SerializeField] private RectTransform arrowTL;
        [SerializeField] private RectTransform arrowTR;
        private UISpaceObject obj;
        public UISpaceObject UISO => obj;
        private readonly Vector3 posTL = new Vector3(-0.5f, 0.5f, 0f);
        private readonly Vector3 posTR = new Vector3(0.5f, 0.5f, 0f);
        private readonly Vector3 posBL = new Vector3(-0.5f, -0.5f, 0f);
        private readonly Vector3 posBR = new Vector3(0.5f, -0.5f, 0f);
        private readonly Quaternion rotTL = Quaternion.Euler(0f, 0f, 45f + 180f);
        private readonly Quaternion rotBL = Quaternion.Euler(0f, 0f, 135f + 180f);
        private readonly Quaternion rotBR = Quaternion.Euler(0f, 0f, -135f + 180f);
        private readonly Quaternion rotTR = Quaternion.Euler(0f, 0f, -45f + 180f);
        private static readonly Vector3 scale = new Vector3(0.5f, 0.5f);
        private static readonly Vector3 arrowScale = new Vector3(0.5f, 0.5f);
        private const float pulseSpeed = 5f;
        public const float ShiftFrom = 1.0f;
        public const float ShiftTo = 1.15f;
        public float ObjectSize = 1f;
        private Vector2 lastParentPosition;
        public void ResetScale() {
            lastScale = 0;
            arrowBL.localScale = unscaledSize;
            arrowBR.localScale = unscaledSize;
            arrowTL.localScale = unscaledSize;
            arrowTR.localScale = unscaledSize;
        }

        private float lastScale;

        private Vector3 unscaledSize = Vector3.one;


        private void Awake() {
            if (tr == null) {
                Initialize();
            }
        }

        private void Initialize() {
            tr = GetComponent<RectTransform>();
            tr.localScale = scale;

            arrowBL.transform.localScale = arrowScale;
            arrowBR.transform.localScale = arrowScale;
            arrowTR.transform.localScale = arrowScale;
            arrowTL.transform.localScale = arrowScale;

            arrowBL.transform.SetLocalPositionAndRotation(posBL, rotBL);
            arrowBR.transform.SetLocalPositionAndRotation(posBR, rotBR);
            arrowTR.transform.SetLocalPositionAndRotation(posTR, rotTR);
            arrowTL.transform.SetLocalPositionAndRotation(posTL, rotTL);
        }

        private void Update() {
            if (obj.UIPosition != lastParentPosition) {
                lastParentPosition = obj.UIPosition;
                transform.position = lastParentPosition;
            }
            if (obj.IsConstantSizeInStarmap) {
                // sync the object size
                AdjustScale();
            } else {
                DoAutoScale();
            }

            var shift = ObjectSize * math.lerp(ShiftFrom, ShiftTo,
                Maths.FastSin(GameTimer.unscaledTime * pulseSpeed));

            if (lastScale < 0.33f) {
                shift /= Mathf.Lerp(0.1f, 1f, lastScale * 3f);
            }

            ShiftArrows(shift);
        }

        private void ShiftArrows(float shift) {
            arrowBL.transform.localPosition = posBL * shift;
            arrowBR.transform.localPosition = posBR * shift;
            arrowTL.transform.localPosition = posTL * shift;
            arrowTR.transform.localPosition = posTR * shift;
        }

        public void SetUnscaledSize(float size) {
            unscaledSize = new Vector3(size, size, size);
            lastScale = 0f;
        }

        public void DoAutoScale() {
            var newScale = scaleParent.localScale.x * tr.localScale.x;
            if (newScale > 0f && math.abs(lastScale - newScale) > 0.0001f) {
                lastScale = newScale;
                var sc = unscaledSize / newScale;
                arrowBL.localScale = sc;
                arrowBR.localScale = sc;
                arrowTL.localScale = sc;
                arrowTR.localScale = sc;
            }
        }

        public void LoadFor(UISpaceObject obj) {
            if (tr == null) {
                Initialize();
            }
            this.obj = obj;
            scaleParent = tr.parent as RectTransform;
            lastParentPosition = obj.UIPosition;
            tr.position = lastParentPosition;
            SetUnscaledSize(1);
            AdjustScale();
            DoAutoScale();
            Update();
            arrowBL.SetActiveFast(true);
            arrowBR.SetActiveFast(true);
            arrowTL.SetActiveFast(true);
            arrowTR.SetActiveFast(true);
        }

        private void OnEnable() {
            if (obj == null) {
                arrowBL.SetActiveFast(false);
                arrowBR.SetActiveFast(false);
                arrowTL.SetActiveFast(false);
                arrowTR.SetActiveFast(false);
            }
        }

        private void OnDisable() {
            ShiftArrows(0f);
            obj = null;
        }

        private void AdjustScale() {
            // rekt.localScale may be stale on cold spawn (LateUpdate/selfAutoscale not yet run)
            var rektScaleX = obj.IsConstantSizeInStarmap && scaleParent != null && scaleParent.localScale.x > 0f
                ? 1f / scaleParent.localScale.x
                : obj.Rekt.localScale.x;
            var scale = rektScaleX * obj.Image.rectTransform.localScale.x;
            if (scale < 0.2f) {
                scale = 0.2f;
            }
            tr.localScale = new Vector3(scale, scale, scale);
        }

        public void FocusOn(Transform transform) {
            tr.localPosition = transform.localPosition;
        }
    }
}
