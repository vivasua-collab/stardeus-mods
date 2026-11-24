using System;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using TMPro;
using UnityEngine;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WDropdown : WBase {
        [SerializeField] protected TMP_Dropdown dropdown;
        [SerializeField] protected GameObject arrowIcon;
        private void Awake() {
            RigButton();
        }

        public int Value => dropdown.value;
        public TMP_Dropdown Dropdown => dropdown;

        private bool wasExpanded;
        private Action<int> onSelect;

        private void Update() {
            if (dropdown.IsExpanded != wasExpanded) {
                wasExpanded = dropdown.IsExpanded;
                TweenRZ.Add(arrowIcon, Tunables.UITooltipFadeInSeconds,
                    wasExpanded ? 180f : 0f);
            }
        }

        private void OnDisable() {
            wasExpanded = false;
            arrowIcon.transform.localRotation = Consts.QId;
        }

        protected void RigButton() {
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(OnSelect);
        }

        private void OnSelect(int idx) {
            onSelect?.Invoke(idx);
            // A hack to rebuild subpanels properly
            var baseW = GetComponentInParent<BaseWidget>();
            if (baseW != null) {
                baseW.RebuildLayout();
            }
        }

        public void Clear() {
            dropdown.ClearOptions();
        }

        public void Load(List<string> options, int selected, Action<int> onSelect) {
            dropdown.interactable = true;
            SetOptions(options, selected);
            this.onSelect = onSelect;
        }

        public void SetOptions(List<string> options, int selected) {
            Clear();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(selected);
        }

        public void SetValue(int idx, bool notify = true) {
            if (notify) {
                dropdown.value = idx;
            } else {
                dropdown.SetValueWithoutNotify(idx);
            }
        }

        public void SetInteractable(bool yes) {
            dropdown.interactable = yes;
        }

        public override string ToString() {
            return $"WDropdown[{string.Join(',', dropdown.options)}]";
        }
    }
}
