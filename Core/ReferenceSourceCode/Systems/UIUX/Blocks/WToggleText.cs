using System;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WToggleText : WText, ICustomPoolable {
        [SerializeField] protected Toggle toggle;
        private Action<bool> onToggle;
        private void Awake() {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(OnToggle);
        }

        public void Load(string label, bool isOn, Action<bool> onToggle) {
            Load(label);
            SetOnToggle(onToggle);
            SetValue(isOn, false);
        }

        private void OnToggle(bool on) {
            if (onToggle == null) {
                D.Err("OnToggle not bound: {0}", this);
            } else {
                onToggle.Invoke(on);
            }
        }

        public void SetOnToggle(Action<bool> act) {
            onToggle = act;
        }

        public void SetValue(bool on, bool notify = true) {
            if (notify) {
                toggle.isOn = on;
            } else {
                toggle.SetIsOnWithoutNotify(on);
            }
        }

        public override string ToString() {
            return $"WToggleText[on:{toggle.isOn}|{lastText}";
        }

        public void PoolDeactivate() {
            this.SetActiveFast(false);
            toggle.SetIsOnWithoutNotify(false);
        }

        public void PoolActivate() {
            SetInteractable(true);
            this.SetActiveFast(true);
        }

        public void SetInteractable(bool v) {
            toggle.interactable = v;
        }
    }
}
