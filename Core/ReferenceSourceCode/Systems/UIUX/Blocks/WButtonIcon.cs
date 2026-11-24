using System;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonIcon : WBase {
        [SerializeField] private Button button;
        [SerializeField] private Image image;
        private Action onClick;
        private string lastIcon = ".";
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
        public void SetInteractable(bool yes) {
            button.interactable = yes;
        }
        public void SetTint(Color tint) {
            image.color = tint;
        }
        public void SetImage(string icon) {
            if (lastIcon == icon) {
                return;
            }
            lastIcon = icon;
            if (!Images.TrySprite(icon, out var sprite)) {
                D.Err("Trying to set unavailable sprite on WButtonIcon: {0} -> {1} | {2}",
                    icon, this, D.GOPath(transform));
                return;
            }
            image.sprite = sprite;
        }
        public void SetSprite(Sprite sprite) {
            image.sprite = sprite;
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
            return $"WButtonIcon[{lastIcon}]";
        }
    }
}
