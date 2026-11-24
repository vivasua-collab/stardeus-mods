using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

public class CenteredHorizontalScroll : MonoBehaviour {
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform content;
    [SerializeField] private RectTransform centerer;
    [SerializeField] private HorizontalLayoutGroup layout;
    private float lastPref;

    private void LateUpdate() {
        float pref = layout.minWidth;
        if (pref == lastPref) { return; }
        lastPref = pref;
        float vp = viewport.rect.width;
        if (pref <= vp) {
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, vp);
            centerer.anchoredPosition = Vector2.zero;
        } else {
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, pref);
            centerer.anchoredPosition = Vector2.zero;
            scroll.horizontalNormalizedPosition = 0f;
        }
    }
}
