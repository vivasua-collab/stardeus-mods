using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public sealed class UITextCollisionResolver : MonoBehaviour {
        [SerializeField] private TMP_Text leftText;
        [SerializeField] private TMP_Text rightText;
        private const float spacing = 16f;
        private float? originalTextRightMargin;

        private void Awake() {
            if (leftText == null || rightText == null) {
                D.Err("UICollisionTextResolve has null left or right text! {0} | {1} => {2}",
                    leftText, rightText, D.GOPath(transform));
            }
        }

        public static void AutoResize(GameObject go) {
            if (go.TryGetComponent<UITextCollisionResolver>(out var res)) {
                res.AutoResize();
            }
        }
        public static void AutoResize(RectTransform rekt) {
            if (rekt.TryGetComponent<UITextCollisionResolver>(out var res)) {
                res.AutoResize();
            }
        }
        public static void AutoResize(MonoBehaviour mb) {
            if (mb.TryGetComponent<UITextCollisionResolver>(out var res)) {
                res.AutoResize();
            }
        }

        [ContextMenu("Auto Resize")]
        public void AutoResize() {
            if (leftText == null || rightText == null) {
                D.Err("Can't auto resize, some text is null! {0}", D.GOPath(transform));
                return;
            }

            var leftRect = leftText.rectTransform;

            originalTextRightMargin ??= leftRect.sizeDelta.x;
            #if !PRODUCTION_BUILD
            if (leftRect.pivot.x == 0.5f) {
                D.Err("Set pivot to 0 or 1 in widget: {0}", D.GOPath(leftRect));
            }
            #endif

            float rightTextWidth;
            if (string.IsNullOrWhiteSpace(rightText.text)) {
                rightTextWidth = 0f;
            } else {
                rightText.ForceMeshUpdate();
                // Get the actual rendered width of the right text
                var rw = rightText.GetPreferredValues().x;
                if (rw < 1f) {
                    D.Warn("Render width < 1 for tex: {0}", rightText.text);
                    rightTextWidth = 0f;
                } else {
                    rightTextWidth = rw + spacing;
                }
            }

            float leftWidgetSizeDeltaX = originalTextRightMargin.Value
                - rightTextWidth;
            // D.Warn("right text width: {0}->{1} | sd: {2} | orig: {3}",
            //     rightText.text, rightTextWidth, leftWidgetSizeDeltaX,
            //     originalTextRightMargin);

            Vector2 leftSizeDelta = leftRect.sizeDelta;
            leftSizeDelta.x = leftWidgetSizeDeltaX;
            leftRect.sizeDelta = leftSizeDelta;
        }
    }
}
