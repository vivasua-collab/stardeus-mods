using System;
using Game.Systems.Creatures;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WToggleBeingListItem : WBeingListItem, ICustomPoolable {
        [SerializeField] private WToggle toggle;
        private Action<bool> onToggle;
        public void Load(BeingData being, bool showToggle) {
            toggle.Load(!being.HideAvatarButton, OnToggle);
            toggle.SetActiveFast(showToggle);
            base.Load(being);
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
            toggle.SetValue(on);
        }

        public override string ToString() {
            return $"WToggleBeingListItem[{being}]";
        }
    }
}
