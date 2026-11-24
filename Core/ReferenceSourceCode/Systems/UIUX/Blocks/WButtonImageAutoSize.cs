using System;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonImageAutoSize : WBase {
        [SerializeField] private Button button;
        [SerializeField] private Image image;
        [SerializeField] private AutoSizeImageLayoutElement autoSizer;
        private Action onClick;
        private void Awake() {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
        public void Load(string icon, Action act) {
            SetInteractable(true);
            SetImage(icon);
            SetTint(Consts.ColorWhite);
            onClick = act;
        }
        public void Load(Sprite sprite, Action act, int heightLimit = 512) {
            SetInteractable(true);
            SetSprite(sprite, heightLimit);
            SetTint(Consts.ColorWhite);
            onClick = act;
        }
        public void SetInteractable(bool yes) {
            button.interactable = yes;
        }
        public void SetTint(Color tint) {
            image.color = tint;
        }
        public void SetSprite(Sprite sprite, int heightLimit = 512) {
            image.sprite = sprite;

            // var w = Rect.sizeDelta.x;
            // var h = Images.FindHeightForUI(sprite, w);
            // if (heightLimit > 0 && h > heightLimit) {
            //     h = heightLimit;
            // }

            // Rect.sizeDelta = new Vector2(w, h);
            autoSizer.Calculate();
            if (heightLimit != 0) {
                Coroutines.OnNextFrame(() => autoSizer.CalculateWithLimit(heightLimit));
            } else {
                Coroutines.OnNextFrame(autoSizer.Calculate);
            }
        }
        public void SetImage(string icon, int heightLimit = 0) {
            if (!Images.TrySprite(icon, out var sprite)) {
                D.Err("Trying to set unavailable sprite on WButtonIcon: {0} -> {1} | {2}",
                    icon, this, D.GOPath(transform));
                return;
            }
            SetSprite(sprite, heightLimit);
        }

        public void SetOnClick(Action act) {
            onClick = act;
        }

        protected void OnClick() {
            if (onClick == null) {
                D.Err("OnClick not rigged for {0} -> {1}", this, D.GOPath(transform));
            } else {
                onClick.Invoke();
            }
        }

        public override string ToString() {
            return "WButtonImageAutoSize[]";
        }
    }
}
