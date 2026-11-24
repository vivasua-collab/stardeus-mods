#if UNITY_EDITOR
// #define DEBUG_UINAV
#endif
using Game.Constants;
using Game.UI;
using KL.Algos;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Input {
    public static class UINav {
        public class PrioritizedSelectable : IPrioritizable {
            public int QueueIndex { get; set; }
            public float QueuePriority { get; set; }
            public RectTransform SelectableRect;
            public Selectable Selectable;
            public Vector2 Center;
        }

        public static bool FindClosestButtonFrom(Vector2 start, Vector2 dir, out Vector2 pos) {
            // FIXME rewrite this to have a pool of candidates instead of global search
            var selectables = GameObject.FindObjectsByType<Selectable>(FindObjectsSortMode.InstanceID);
            var closest = FindClosestSelectable(selectables, start, dir, out pos);
            #if DEBUG_UINAV
            D.Warn("Found closest button: {0}", closest);
            #endif
            if (closest != null) {
                return true;
            }
            pos = start;
            return false;
        }
        private static readonly HeapPriorityQueue<UINav.PrioritizedSelectable> queue =
                new(500);

        private static Selectable FindClosestSelectable(Selectable[] selectables,
                Vector2 start, Vector2 dir, out Vector2 center) {

            dir = dir.normalized;
            center = Consts.V2Zero;

            int selCount = selectables.Length;

            //// D.Log("Evaluating {0} selectables", selCount);
            int discardedRaycastInvisible = 0;
            int notInteractable = 0;
            int discardedWrongDir = 0;
            int offScreen = 0;
            int passedEvaluation = 0;
            queue.Clear();

            for (int i = 0; i < selCount; ++i) {
                Selectable sel = selectables[i];

                if (!sel.IsInteractable() ||sel.GetComponent<ControllerNavExempt>() != null) {
                    #if DEBUG_UINAV
                    D.Log("Non interactable: {0}", D.ObjPath(sel.gameObject));
                    #endif
                    notInteractable++;
                    continue;
                }

                var selRect = sel.transform as RectTransform;
                if (!selRect.IsCenterVisibleTo(dir, out var closestPoint, out var selCenter)) {
                    #if DEBUG_UINAV
                    D.Log("Off screen: {0} {1}", selCenter, D.ObjPath(sel.gameObject));
                    #endif
                    offScreen++;
                    continue;
                }

                var myVector = closestPoint - start;
                // This scoring function has two priorities:
                // - Score higher for positions that are closer.
                // - Score higher for positions that are located in the right direction.
                // This scoring function combines both of these criteria.
                // It can be seen as this:
                //   Dot (dir, myVector.normalized) / myVector.magnitude
                // The first part equals 1 if the direction of myVector is the same as dir, and 0 if it's orthogonal.
                // The second part scores lower the greater the distance is by dividing by the distance.
                // The formula below is equivalent but more optimized.
                //
                // If a given score is chosen, the positions that evaluate to that score will form a circle
                // that touches pos and whose center is located along dir. A way to visualize the resulting functionality is this:
                // From the position pos, blow up a circular balloon so it grows in the direction of dir.
                // The first Selectable whose center the circular balloon touches is the one that's chosen.

                // Value that is the distance out along the direction.
                float dot = Vector2.Dot(dir, myVector);

                // Skip elements that are in the wrong direction or which have zero distance.
                // This also ensures that the scoring formula below will not have a division by zero error.
                if (dot <= 0) {
                    #if DEBUG_UINAV
                    D.Log("Wrong dir: {0}", D.ObjPath(sel.gameObject));
                    #endif
                    discardedWrongDir++;
                    continue;
                }

                float score = (dot) / (myVector.sqrMagnitude);
                queue.Enqueue(new() {
                    Selectable = sel,
                    SelectableRect = selRect,
                    Center = selCenter,
                }, -score);
            }

            while (queue.Count > 0) {
                var pSel = queue.Dequeue();
                var selRect = pSel.SelectableRect;
                var selCenter = pSel.Center;

                bool isVisibleToRaycast = true;
                // if (UIShowing.TutorialMask) {
                //     var ignoreMask = selRect.GetComponentInParent<ControllerNavIgnoreTutorialMask>();
                //         // this is not supposed to be blockable by tutorial mask (i.e. tutorial panel)
                //     if (ignoreMask == null) {
                //         if (The.R.Query.IsBlockedByTutorialMask.Ask(selCenter)) {
                //             isVisibleToRaycast = false;
                //         }
                //     } else if (!selRect.IsPointVisibleToRaycast(selCenter)) {
                //         isVisibleToRaycast = false;
                //     }
                // } else {
                //     if (!selRect.IsPointVisibleToRaycast(selCenter)) {
                //         isVisibleToRaycast = false;
                //     }
                // }
                if (!isVisibleToRaycast) {
                    #if DEBUG_UINAV
                    D.Log("Invisible to raycast: {0}", D.ObjPath(pSel.Selectable.gameObject));
                    #endif
                    discardedRaycastInvisible++;
                    continue;
                }
                #if DEBUG_UINAV
                D.Log("Winner: {0}", D.ObjPath(pSel.Selectable.gameObject));
                #endif

                passedEvaluation++;
                center = selCenter;
                #if DEBUG_UINAV
                D.Log("UINavStats: Raycast Discard: {0}, Not interactable: {1}, Wrong dir: {2}, PassedEval: {3}. Off screen: {4}. Total raycasts: {5}",
                    discardedRaycastInvisible,
                    notInteractable,
                    discardedWrongDir,
                    passedEvaluation,
                    offScreen,
                    discardedRaycastInvisible + passedEvaluation);
                #endif
                return pSel.Selectable;

            }
            return null;
        }
    }
}
