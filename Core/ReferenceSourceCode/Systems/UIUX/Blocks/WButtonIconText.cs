using System;
using Game.Constants;
using Game.Utils;
using KL.UI;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonIconText : WIconText, ICustomPoolable {
        [SerializeField] private Button button;
        private Action onClick;

        private void Awake() {
            if (button == null) {
                // D.Err("Button not rigged for {0}", D.GOPath(transform));
                return;
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        public void SetInteractable(bool interactable) {
            button.interactable = interactable;
        }

        public void Load(string icon, string text, Action onClick) {
            SetInteractable(true);
            SetOnClick(onClick);
            base.Load(icon, text);
        }

        public void SetOnClick(Action act) {
            onClick = act;
        }

        public void Click() {
            OnClick();
        }

        protected void OnClick() {
            if (onClick == null) {
                D.Err("OnClick not rigged for {0}", this);
            } else {
                onClick.Invoke();
            }
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            UIActivator.EnsureDeselected(gameObject);
        }

        public void PoolActivate() {
            SetInteractable(true);
            gameObject.SetActiveFast(true);
        }
        public override string ToString() {
            return $"WButtonIconText[{lastIcon}|{lastText}|{button.onClick}]";
        }
    }
}
