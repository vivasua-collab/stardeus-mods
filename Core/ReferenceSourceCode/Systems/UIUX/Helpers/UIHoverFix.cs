using System;
using System.Collections.Generic;
using KL.Utils;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public static class UIHoverFix {
        private static readonly List<RaycastResult> raycastResults = new();
        private static PointerEventData cachedPointerData;

        /// <summary>
        /// Must be called on next frame
        /// </summary>
        public static void RefreshHover() {
            // D.Log("Refreshing UI hover");
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            if (cachedPointerData == null) {
                cachedPointerData = new PointerEventData(eventSystem);
            } else {
                cachedPointerData.Reset();
            }
            cachedPointerData.position = The.Bindings.Mouse.ScreenPosition;

            raycastResults.Clear();
            eventSystem.RaycastAll(cachedPointerData, raycastResults);

            var newTarget = raycastResults.Count > 0
                ? raycastResults[0].gameObject : null;

            if (eventSystem.currentSelectedGameObject != newTarget) {
                if (eventSystem.currentSelectedGameObject != null) {
                    D.Log("Target {0} is different from current {1}, refreshing hover", newTarget, eventSystem.currentSelectedGameObject);
                    try {
                        ExecuteEvents.Execute(
                            eventSystem.currentSelectedGameObject,
                            cachedPointerData,
                            ExecuteEvents.pointerExitHandler);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed dispatching pointer exit during hover refresh: {0}",
                            eventSystem.currentSelectedGameObject);
                    }
                }

                if (newTarget != null) {
                    // D.Log("Setting hover on new target {0}", newTarget);
                    try {
                        ExecuteEvents.Execute(
                            newTarget,
                            cachedPointerData,
                            ExecuteEvents.pointerEnterHandler);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed dispatching pointer enter during hover refresh: {0}", newTarget);
                    }
                }
            }
        }
    }
}
