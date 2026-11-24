using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public sealed class UITextTitleSubtitleResizer : MonoBehaviour {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private RectTransform container;

        [ContextMenu("Auto Resize")]
        public void AutoResize() {
            if (titleText == null || subtitleText == null || container == null) {
                return;
            }

            // Force title to calculate its preferred height
            titleText.ForceMeshUpdate();
            float titlePreferredHeight = titleText.preferredHeight;

            RectTransform titleRT = titleText.rectTransform;
            RectTransform subtitleRT = subtitleText.rectTransform;

            // Title anchored to top, takes its preferred height
            titleRT.anchorMin = new Vector2(0, 1);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.pivot = new Vector2(0.5f, 1);
            titleRT.sizeDelta = new Vector2(titleRT.sizeDelta.x, titlePreferredHeight);
            titleRT.anchoredPosition = new Vector2(titleRT.anchoredPosition.x, 0);

            // Subtitle fills remaining space below title
            float subtitleTop = titlePreferredHeight;
            subtitleRT.anchorMin = new Vector2(0, 0);
            subtitleRT.anchorMax = new Vector2(1, 1);
            subtitleRT.pivot = new Vector2(0.5f, 0.5f);
            subtitleRT.offsetMin = new Vector2(subtitleRT.offsetMin.x, 0);
            subtitleRT.offsetMax = new Vector2(subtitleRT.offsetMax.x, -subtitleTop);
        }
    }
}
