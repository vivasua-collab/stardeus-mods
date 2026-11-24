#if !PRODUCTION_BUILD
// #define DEBUG_FOCUSABLE
#endif
using System;
using Game.Data;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;

using Game.Utils;
namespace Game.UI {
    [DisallowMultipleComponent]
    [Obsolete("Remove")]
    public sealed class UIFocusablePanel : MonoBehaviour, IPointerDownHandler {
        [SelfInit]
        private static void Register() {
            overrideSorting = 0;
            lastFocusedId = 0;
        }
        private static int overrideSorting;
        private static int lastFocusedId;
        private int id;
        private bool isFocused;
        private Canvas canvas;
        private bool originalCanvasOverrideSorting;
        private int originalCanvasSorting;
        private bool isSubscribed;
        private bool focusOnEnable;
        private GameState S;

        private void Awake() {
            id = GetInstanceID();
            canvas = GetComponent<Canvas>();
            if (canvas == null) {
                D.Err("UIFocusablePanel requires Canvas! {0}", D.GOPath(transform));
                Destroy(this);
                return;
            }
            originalCanvasOverrideSorting = canvas.overrideSorting;
            originalCanvasSorting = canvas.sortingOrder;
        }

        private void Start() {
            overrideSorting = Mathf.Max(overrideSorting, originalCanvasSorting + 1);
            #if DEBUG_FOCUSABLE
            D.Warn("Initializing focusable panel: override:{0}, sorting: {1}.OS: {2} -> {3}",
                originalCanvasOverrideSorting,
                originalCanvasSorting,
                overrideSorting,
                D.GOPath(transform));
            #endif
        }
        public void FocusIfUnfocused() {
            if (isFocused && lastFocusedId == id) {
                return;
            }
            // for center panel hack
            if (!enabled) { return; }
            S.Sig.FocusUIPanel.Send(this);
        }
        private void OnEnable() {
            if (canvas == null) {
                D.Err("Canvas is null in on enable! {0}", D.GOPath(transform));
            }
            S = The.R?.State;
            Subscribe();
            if (focusOnEnable) {
                focusOnEnable = false;
                SendFocusSignal();
            }
        }

        private void Subscribe() {
            if (!isSubscribed && S != null) {
                isSubscribed = true;
                S.Sig.FocusUIPanel.AddListener(OnFocusPanel, gameObject);
                S.Sig.DefocusAllPanels.AddListener(OnDefocusAll, gameObject);
            }
        }

        private void OnDefocusAll() {
            ResetFocus();
        }

        private void Unsubscribe() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.FocusUIPanel.RemoveListener(OnFocusPanel);
                S.Sig?.DefocusAllPanels.RemoveListener(OnDefocusAll);
            }
        }
        public static void DefocusAll() {
            The.R?.Sig?.DefocusAllPanels.Send();
        }
        public static bool TryFocus(GameObject obj) {
            #if DEBUG_FOCUSABLE
            D.Warn("Trying to focus panel: {0}", obj);
            #endif
            if (obj == null) {
                D.Err("Trying to find UIFocusablePanel on NULL!");
                return false;
            }
            if (obj.TryGetComponent<UIFocusablePanel>(out var focusable)) {
                focusable.SendFocusSignal();
                return true;
            } else {
                D.Err("Couldn't find UIFocusablePanel on {0}",
                    D.GOPath(obj.transform));
                return false;
            }
        }

        private void OnFocusPanel(UIFocusablePanel panel) {
            if (panel == this) {
                Focus();
            } else {
                Unfocus();
            }
        }

        /// <summary>
        /// Never call this anywhere except from OnFocusPanel!
        /// Otherwise it will focus without regard to other panels!
        /// </summary>
        private void Focus() {
            // for center panel hack
            if (!enabled) { return; }
            if (isFocused) {
                if (lastFocusedId == id) {
                    return;
                }
                D.Err("Panel is focused but last focused id does not match! {0}",
                    D.GOPath(transform));
            }
            #if DEBUG_FOCUSABLE
            D.Warn("Focusing panel: {0}", D.GOPath(transform));
            #endif
            Focus(overrideSorting);
        }

        private void Focus(int sorting) {
            isFocused = true;
            lastFocusedId = id;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sorting;
        }

        public void Unfocus() {
            if (!isFocused) { return; }
            #if DEBUG_FOCUSABLE
            D.Warn("Defocusing panel: {0}", D.GOPath(transform));
            #endif
            ResetFocus();
        }

        public void ResetFocus() {
            focusOnEnable = false;
            isFocused = false;
            canvas.overrideSorting = originalCanvasOverrideSorting;
            canvas.sortingOrder = originalCanvasSorting;
        }

        private void OnDisable() {
            Unfocus();
            Unsubscribe();
            S = null;
        }

        private void OnDestroy() {
            Unsubscribe();
            S = null;
        }

        public void OnPointerDown(PointerEventData eventData) {
            SendFocusSignal();
        }

        public void SendFocusSignal() {
            if (isSubscribed) {
                #if DEBUG_FOCUSABLE
                D.Warn("Will focus on panel: {0}", D.GOPath(transform));
                #endif
                S?.Sig?.FocusUIPanel.Send(this);
            } else {
                if (S == null) {
                    #if DEBUG_FOCUSABLE
                    D.Warn("Focusing without game state: {0}", D.GOPath(transform));
                    #endif
                    Focus(20000);
                } else {
                    D.Err("Trying to focus unsubscribed panel! {0}", D.GOPath(transform));
                }
            }
        }

    }
}
