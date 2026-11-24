using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public sealed class TMPDropdownExpandedWidthHelper : MonoBehaviour {
        [SerializeField] private TMP_Dropdown dropdown;
        [SerializeField] private float extraTextPadding = 12f;
        [SerializeField] private float maxWidth = 1000f;

        private RectTransform templateRect;
        private RectTransform itemRect;
        private RectTransform itemTextRect;

        private float collapsedTemplateWidth;
        private float collapsedItemTextWidth;

        private bool wasExpanded;
        private bool pendingExpandedApply;

        private void Reset() {
            dropdown = GetComponent<TMP_Dropdown>();
        }

        private void Awake() {
            dropdown ??= GetComponent<TMP_Dropdown>();
            CacheTemplateReferences();
        }

        private void OnValidate() {
            dropdown ??= GetComponent<TMP_Dropdown>();
            if (maxWidth < 0f) {
                maxWidth = 0f;
            }
        }

        private void LateUpdate() {
            if (dropdown == null) {
                return;
            }

            if (dropdown.IsExpanded != wasExpanded) {
                wasExpanded = dropdown.IsExpanded;
                if (wasExpanded) {
                    pendingExpandedApply = true;
                } else {
                    pendingExpandedApply = false;
                }
            }

            if (dropdown.IsExpanded && pendingExpandedApply) {
                pendingExpandedApply = !ApplyWidthToExpandedDropdown();
            }
        }

        [ContextMenu("Resize Expanded Width")]
        public void ResizeExpandedWidth() {
            if (dropdown == null || !dropdown.IsExpanded) {
                return;
            }
            pendingExpandedApply = !ApplyWidthToExpandedDropdown();
        }

        private bool ApplyWidthToExpandedDropdown() {
            if (!CacheTemplateReferences()) {
                return false;
            }

            float widestOptionText = GetWidestOptionTextWidth();
            float extraWidthNeeded = Mathf.Max(0f,
                widestOptionText + extraTextPadding - collapsedItemTextWidth);
            float targetWidth = collapsedTemplateWidth + extraWidthNeeded;
            if (maxWidth > 0f) {
                targetWidth = Mathf.Min(targetWidth, maxWidth);
            }

            var expandedRect = FindExpandedDropdownList();
            if (expandedRect == null) {
                return false;
            }

            ExpandToWidthFromLeft(expandedRect, targetWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(expandedRect);
            return true;
        }

        private bool CacheTemplateReferences() {
            if (dropdown == null || dropdown.template == null || dropdown.itemText == null) {
                return false;
            }

            if (templateRect != null && itemRect != null && itemTextRect != null) {
                collapsedTemplateWidth = Mathf.Max(GetRectWidth(templateRect), GetCollapsedDropdownWidth());
                collapsedItemTextWidth = GetAvailableItemTextWidth(collapsedTemplateWidth);
                return true;
            }

            templateRect = dropdown.template;
            itemTextRect = dropdown.itemText.rectTransform;
            itemRect = itemTextRect.parent as RectTransform;
            if (itemRect == null) {
                return false;
            }

            collapsedTemplateWidth = Mathf.Max(GetRectWidth(templateRect), GetCollapsedDropdownWidth());
            collapsedItemTextWidth = GetAvailableItemTextWidth(collapsedTemplateWidth);
            return true;
        }

        private RectTransform FindExpandedDropdownList() {
            var rootCanvas = dropdown.GetComponentInParent<Canvas>()?.rootCanvas;
            var searchRoot = rootCanvas != null ? rootCanvas.transform : transform.root;
            var allRects = searchRoot.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < allRects.Length; i++) {
                if (allRects[i].name == "Dropdown List" && allRects[i].gameObject.activeInHierarchy) {
                    return allRects[i];
                }
            }
            return null;
        }

        private float GetWidestOptionTextWidth() {
            if (dropdown == null || dropdown.itemText == null || dropdown.options.Count == 0) {
                return 0f;
            }

            float widest = 0f;
            var itemText = dropdown.itemText;
            itemText.ForceMeshUpdate();
            for (int i = 0; i < dropdown.options.Count; i++) {
                var optionText = dropdown.options[i]?.text;
                if (string.IsNullOrWhiteSpace(optionText)) {
                    continue;
                }

                float width = itemText.GetPreferredValues(optionText,
                    float.PositiveInfinity, float.PositiveInfinity).x;
                widest = Mathf.Max(widest, width);
            }
            return widest;
        }

        private static float GetRectWidth(RectTransform rectTransform) {
            return rectTransform.rect.width > 0f
                ? rectTransform.rect.width
                : rectTransform.sizeDelta.x;
        }

        private float GetCollapsedDropdownWidth() {
            return dropdown.transform is RectTransform dropdownRect
                ? GetRectWidth(dropdownRect)
                : 0f;
        }

        private float GetAvailableItemTextWidth(float fallbackItemWidth = 0f) {
            if (itemTextRect == null || itemRect == null) {
                return 0f;
            }

            if (itemTextRect.anchorMin.x != itemTextRect.anchorMax.x) {
                float itemWidth = itemRect.rect.width > 0f ? itemRect.rect.width : fallbackItemWidth;
                return itemWidth + itemTextRect.sizeDelta.x;
            }
            return GetRectWidth(itemTextRect);
        }

        private static void ExpandToWidthFromLeft(RectTransform rectTransform, float width) {
            if (rectTransform == null || width <= 0f) {
                return;
            }

            float currentWidth = GetRectWidth(rectTransform);
            if (width <= currentWidth) {
                return;
            }

            if (!Mathf.Approximately(rectTransform.anchorMin.x, rectTransform.anchorMax.x)) {
                if (rectTransform.parent is not RectTransform parentRect) {
                    return;
                }

                float parentWidth = GetRectWidth(parentRect);
                Vector2 offsetMin = rectTransform.offsetMin;
                Vector2 offsetMax = rectTransform.offsetMax;
                offsetMin.x = 0f;
                offsetMax.x = width - parentWidth;
                rectTransform.offsetMin = offsetMin;
                rectTransform.offsetMax = offsetMax;
                return;
            }

            rectTransform.pivot = new Vector2(0f, rectTransform.pivot.y);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            var anchoredPosition = rectTransform.anchoredPosition;
            anchoredPosition.x = 0f;
            rectTransform.anchoredPosition = anchoredPosition;
        }
    }
}
