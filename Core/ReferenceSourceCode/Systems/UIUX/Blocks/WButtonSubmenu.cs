using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using KL.UweenExt;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonSubmenu : WIconText {
        [SerializeField] private Button button;
        [SerializeField] private GameObject submenu;
        [SerializeField] private bool destroyChildrenOnAwake = true;
        [SerializeField] private bool animateY;
        [SerializeField] private float animateYAmt = -64f;
        [SerializeField] private bool createBlocker;
        public Action OnCollapse;
        private GameObject blocker;
        public RectTransform Container;
        public Image Image => image;
        public bool IsExpanded => isExpanded;
        private bool isExpanded;
        private void Awake() {
            if (button != null) {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnClick);
            }
            DisableSubmenu();
            if (destroyChildrenOnAwake) {
                MiscUtils.DestroyChildren(Container);
            }
        }

        public void HideSubmenu() {
            if (isExpanded) {
                isExpanded = false;
                if (blocker != null) {
                    GameObject.Destroy(blocker);
                    blocker = null;
                }
                OnCollapse?.Invoke();
                if (animateY) {
                    TweenRectY.Add(submenu, 0.1f, animateYAmt).Delay(0.05f);
                }
                TweenCGA.Add(submenu, 0.1f, 0f).Then(DisableSubmenu);
            }
        }
        public void DisableSubmenu() {
            if (blocker != null) {
                GameObject.Destroy(blocker);
                blocker = null;
            }
            if (isExpanded) {
                isExpanded = false;
                OnCollapse?.Invoke();
            }
            submenu.SetActiveFast(false);
            if (animateY) {
                // snap back to 0 so it can animate back again
                submenu.GetComponent<RectTransform>().anchoredPosition = Consts.V2Zero;
            }
        }
        public void ShowSubmenu() {
            if (isExpanded) { return; }
            if (createBlocker) {
                if (blocker != null) {
                    D.Err("Trying to create blocker more than once!");
                } else {
                    blocker = PopoutBlocker.Create(submenu, Container, HideSubmenu);
                }
            }
            submenu.SetActiveFast(true);
            isExpanded = true;
            if (animateY) {
                TweenRectY.Add(submenu, 0.1f, 0f);
            }
            TweenCGA.Add(submenu, 0.1f, 1f);
        }

        private void OnDisable() {
            if (isExpanded) {
                DisableSubmenu();
            }
        }

        // The open submenu's top right corner in screen space where it comes to rest, also
        // while it is still sliding in, so whatever docks above it does not land on it
        public Vector3 RestingTopRight() {
            Container.GetWorldCorners(Caches.Corners);
            var topRight = Caches.Corners[2];
            if (animateY && submenu.transform is RectTransform rt
                    && rt.parent is RectTransform parent) {
                topRight.y -= rt.anchoredPosition.y * parent.lossyScale.y;
            }
            return topRight;
        }

        protected virtual void OnClick() {
            if (isExpanded) {
                HideSubmenu();
            } else {
                ShowSubmenu();
            }
        }

        public override string ToString() {
            return "WButtonSubmenu[]";
        }
    }
}
