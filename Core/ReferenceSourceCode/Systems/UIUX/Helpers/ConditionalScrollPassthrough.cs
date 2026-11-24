using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ConditionalScrollPassthrough : MonoBehaviour, IScrollHandler {
    private ScrollRect scrollRect;
    public void OnScroll(PointerEventData eventData) {
        if (scrollRect == null) {
            scrollRect = GetComponent<ScrollRect>();
            if (scrollRect == null) {
                D.Err("ConditionalScrollPassthrough is attached to something that doesn't have a ScrollRect! {0}",
                    D.GOPath(transform));
                return;
            }
        }

        bool contentFits = scrollRect.content.rect.height <= scrollRect.viewport.rect.height;

        if (contentFits) {
            PassToParent(eventData);
        } else {
            scrollRect.OnScroll(eventData);
        }
    }

    void PassToParent(PointerEventData eventData) {
        Transform t = transform.parent;

        while (t != null) {
            var parentScroll = t.GetComponent<ScrollRect>();
            if (parentScroll != null) {
                parentScroll.OnScroll(eventData);
                return;
            }

            t = t.parent;
        }
    }
}
