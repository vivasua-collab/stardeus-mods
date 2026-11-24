using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class WrapLayoutGroup : LayoutGroup {
        public float spacingX = 4;
        public float spacingY = 4;

        public override void CalculateLayoutInputHorizontal() {
            base.CalculateLayoutInputHorizontal();
        }

        public override void CalculateLayoutInputVertical() {
            LayoutChildren(false, false, true);
        }

        public override void SetLayoutHorizontal() {
            LayoutChildren(true, false, false);
        }

        public override void SetLayoutVertical() {
            LayoutChildren(false, true, false);
        }

        private void LayoutChildren(bool applyHorizontal, bool applyVertical, bool reportVertical) {
            float maxWidth = rectTransform.rect.width - padding.left - padding.right;

            float x = padding.left;
            float y = padding.top;
            float rowHeight = 0;

            for (int i = 0; i < rectChildren.Count; i++) {
                RectTransform child = rectChildren[i];

                float childWidth = LayoutUtility.GetPreferredWidth(child);
                float childHeight = LayoutUtility.GetPreferredHeight(child);

                if (x > padding.left && x + childWidth > maxWidth + padding.left) {
                    x = padding.left;
                    y += rowHeight + spacingY;
                    rowHeight = 0;
                }

                if (applyHorizontal) {
                    SetChildAlongAxis(child, 0, x, childWidth);
                }
                if (applyVertical) {
                    SetChildAlongAxis(child, 1, y, childHeight);
                }

                x += childWidth + spacingX;
                rowHeight = Mathf.Max(rowHeight, childHeight);
            }

            if (reportVertical) {
                float totalHeight = y + rowHeight + padding.bottom;
                SetLayoutInputForAxis(totalHeight, totalHeight, -1, 1);
            }
        }
    }
}
