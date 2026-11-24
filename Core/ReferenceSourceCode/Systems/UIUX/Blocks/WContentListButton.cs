using System;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WContentListButton : WGroup, ICustomPoolable {
        /// <summary>
        /// Container, will show, was loaded before. Returns true if content was loaded.
        /// </summary>
        public delegate bool OnToggleDelegate(Lifetime life, RectTransform container, bool willShow, bool wasLoaded);
        private OnToggleDelegate onToggle;
        /// <summary>
        /// Disposing this is the responsibility of the caller!
        /// </summary>
        private Lifetime toggleLife;

        [SerializeField] private WButtonIconText2 wButton;
        [SerializeField] private RectTransform arrow;
        public WGroup PopupArea;
        private bool wasLoaded;
        public WButtonIconText2 Button => wButton;

        private void Start() {
            wButton.SetOnClick(ToggleContainer);
        }

        /// <summary>
        /// Only use this for widget that have no icon!
        /// </summary>
        public void Load(string text, string text2) {
            if (wButton.Image != null) {
                D.Throw("Can't use Load(text, ..) on this button, image component set up: {0}", D.GOPath(transform));
            }
            wButton.SetText(text);
            wButton.SetText2(text2);
            wButton.SetOnClick(ToggleContainer);
        }

        /// <summary>
        /// Only use this for widget that have an icon!
        /// </summary>
        public void Load(string icon, string text, string text2) {
            if (wButton.Image == null) {
                D.Throw("Can't use Load(icon, ..) on this button, no image component set up: {0}", D.GOPath(transform));
            }
            wButton.Load(icon, text, text2, ToggleContainer);
        }

        /// <summary>
        /// Params: Lifetime of the toggle, Container, will show, was loaded before
        /// </summary>
        public void LoadOnToggle(Lifetime life, OnToggleDelegate onToggle, bool expanded = false) {
            wasLoaded = false;
            arrow.localRotation = expanded ? Constants.Consts.Q0 : Constants.Consts.Q90;
            this.toggleLife = life;
            this.onToggle = onToggle;
            if (expanded) {
                SetContainerActive(true, animateArrow: false);
            } else {
                Container.SetActiveFast(false);
            }
        }

        private void ToggleContainer() {
            ToggleContainer(animateArrow: true);
            var scroll = GetComponentInParent<ScrollRect>();
            if (scroll != null) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            }
        }
        private void SetContainerActive(bool makeActive, bool animateArrow) {
            if (onToggle != null) {
                wasLoaded = onToggle(toggleLife, Container, makeActive, wasLoaded);
            }
            Container.SetActiveFast(makeActive);
            if (animateArrow) {
                // D.Warn("Rotating arrow");
                TweenRZ.Add(arrow.gameObject, 0.1f, makeActive ? 0f : 90f);
            } else {
                arrow.localRotation = makeActive ? Constants.Consts.Q0 : Constants.Consts.Q90;
            }
        }
        private void ToggleContainer(bool animateArrow) {
            var makeActive = !Container.gameObject.activeSelf;
            SetContainerActive(makeActive, animateArrow);
        }

        public override string ToString() {
            return "WContentListButton[]";
        }

        public void RebuildContainer() {
            var needsShow = Container.gameObject.activeSelf;
            wasLoaded = false;
            if (needsShow) {
                SetContainerActive(true, false);
            }
        }

        public void SetText2(string num) {
            wButton.SetText2(num);
        }

        public void PoolDeactivate() {
            toggleLife = null;
            onToggle = null;
            gameObject.SetActiveFast(false);
        }

        public void PoolActivate() {
            Container.SetActiveFast(false);
            gameObject.SetActiveFast(true);
        }
    }
}
