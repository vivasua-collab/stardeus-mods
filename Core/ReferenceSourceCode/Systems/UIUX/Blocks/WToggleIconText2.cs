using System;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WToggleIconText2 : WIconText2, ICustomPoolable {
        [SerializeField] private Toggle toggle;
        public Toggle Toggle => toggle;
        private Action<bool> onToggle;
        private void Awake() {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(OnToggle);

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

        public void LoadToggle(bool on) {
            toggle.SetIsOnWithoutNotify(on);
        }

        public override string ToString() {
            return $"WToggleIconTextText[on:{toggle.isOn}|{lastIcon}|{lastText}|{lastText2}]";
        }

        public void PoolDeactivate() {
            this.SetActiveFast(false);
            toggle.SetIsOnWithoutNotify(false);
        }

        public void PoolActivate() {
            this.SetActiveFast(true);
        }
    }
}
