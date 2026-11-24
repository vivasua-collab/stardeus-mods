using System;
using Game.Constants;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WToggle : WBase, ICustomPoolable {
        [SerializeField] protected Toggle toggle;
        [SerializeField] private Image checkmark;
        private Action<bool> onToggle;
        private void Awake() {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(OnToggle);
        }

        public void SetCheckmarkOverride(string icon, Color tint) {
            checkmark.sprite = Images.Sprite(icon);
            checkmark.color = tint;
        }
        public void ClearCheckmarkOverride() {
            checkmark.sprite = Images.Sprite(GraphicsId.UIButtonsCheckmark);
            checkmark.color = Consts.ColorWhite;
        }

        private void OnToggle(bool on) {
            if (onToggle == null) {
                D.Err("OnToggle not bound: {0}", this);
            } else {
                onToggle.Invoke(on);
            }
        }

        public void Load(bool value, Action<bool> onToggle) {
            ClearCheckmarkOverride();
            SetOnToggle(onToggle);
            SetValue(value, false);
            SetInteractable(true);
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
            return $"WToggle[on:{toggle.isOn}]";
        }

        public void PoolDeactivate() {
            this.SetActiveFast(false);
            ClearCheckmarkOverride();
            toggle.SetIsOnWithoutNotify(false);
        }

        public void PoolActivate() {
            this.SetActiveFast(true);
        }

        public void SetInteractable(bool v) {
            toggle.interactable = v;
        }
    }
}
