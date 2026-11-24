using System;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButton : WBase {
        [SerializeField] private Button button;
        private Action onClick;
        private void Awake() {
            if (button == null) {
                D.Err("WButton doesn't have button bound: {0}", D.GOPath(transform));
                button = GetComponent<Button>();
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        public void Load(Action act) {
            SetInteractable(true);
            SetOnClick(act);
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

        public void SetInteractable(bool v) {
            button.interactable = v;
        }
        public override string ToString() {
            return $"WButton[{onClick?.Target}]";
        }
    }
}
