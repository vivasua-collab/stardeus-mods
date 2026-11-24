using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Borrowed from TMP_Dropdown implementation
    /// </summary>
    public static class PopoutBlocker {
        /// <summary>
        /// Create a blocker that blocks clicks to other controls while the dropdown list is open.
        /// </summary>
        /// <remarks>
        /// Override this method to implement a different way to obtain a blocker GameObject.
        /// </remarks>
        /// <param name="rootCanvas">The root canvas the dropdown is under.</param>
        /// <returns>The created blocker object</returns>
        public static GameObject Create(GameObject forObj, RectTransform parent,
                Action hidePopout) {
            // Create blocker GameObject.
            GameObject blocker = new GameObject("Blocker");


            var rootCanvas = GetRootCanvasFor(forObj);
            // Set the game object layer to match the Canvas' game object layer, as not doing this can lead to issues
            // especially in XR applications like PolySpatial on VisionOS (UUM-62470).
            blocker.layer = rootCanvas.gameObject.layer;

            // Setup blocker RectTransform to cover entire root canvas area.
            RectTransform blockerRect = blocker.AddComponent<RectTransform>();
            blockerRect.SetParent(rootCanvas.transform, false);
            blockerRect.anchorMin = Vector3.zero;
            blockerRect.anchorMax = Vector3.one;
            blockerRect.sizeDelta = Vector2.zero;

            // Make blocker be in separate canvas in same layer as dropdown and in layer just below it.
            Canvas blockerCanvas = blocker.AddComponent<Canvas>();
            blockerCanvas.overrideSorting = true;
            Canvas goCanvas = forObj.GetComponent<Canvas>();
            blockerCanvas.sortingLayerID = goCanvas.sortingLayerID;
            blockerCanvas.sortingOrder = goCanvas.sortingOrder - 1;

            // Find the Canvas that this dropdown is a part of
            Canvas parentCanvas = null;
            Transform parentTransform = parent;
            while (parentTransform != null) {
                parentCanvas = parentTransform.GetComponent<Canvas>();
                if (parentCanvas != null) {
                    break;
                }

                parentTransform = parentTransform.parent;
            }

            // If we have a parent canvas, apply the same raycasters as the parent for consistency.
            if (parentCanvas != null) {
                Component[] components = parentCanvas.GetComponents<BaseRaycaster>();
                for (int i = 0; i < components.Length; i++) {
                    Type raycasterType = components[i].GetType();
                    if (blocker.GetComponent(raycasterType) == null) {
                        blocker.AddComponent(raycasterType);
                    }
                }
            } else {
                // Add raycaster since it's needed to block.
                GetOrAddComponent<GraphicRaycaster>(blocker);
            }

            // Add image since it's needed to block, but make it clear.
            Image blockerImage = blocker.AddComponent<Image>();
            blockerImage.color = Color.clear;

            // Add button since it's needed to block, and to close the dropdown when blocking area is clicked.
            Button blockerButton = blocker.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(() => {
                hidePopout();
            });

            //add canvas group to ensure clicking outside the dropdown will hide it (UUM-33691)
            CanvasGroup blockerCanvasGroup = blocker.AddComponent<CanvasGroup>();
            blockerCanvasGroup.ignoreParentGroups = true;
            return blocker;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component {
            T comp = go.GetComponent<T>();
            if (!comp)
                comp = go.AddComponent<T>();
            return comp;
        }

        public static Canvas GetRootCanvasFor(GameObject go) {
            var list = ListPool<Canvas>.Get();
            go.GetComponentsInParent(false, list);
            if (list.Count == 0) {
                return null;
            }

            Canvas rootCanvas = list[^1];
            for (int i = 0; i < list.Count; i++) {
                if (list[i].isRootCanvas) {
                    rootCanvas = list[i];
                    break;
                }
            }

            ListPool<Canvas>.Release(list);
            return rootCanvas;
        }
    }
}
