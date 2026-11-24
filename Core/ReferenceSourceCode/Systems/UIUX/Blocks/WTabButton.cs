using System;
using System.ComponentModel;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WTabButton : WButtonText, ICustomPoolable {
        [SerializeField] private string themedButtonId_Active;
        [SerializeField] private UIThemedButton themedButton;
        [SerializeField] private GameObject overlay;
        [SerializeField] private LayoutGroup layout;
        [SerializeField] private RectOffset selectedShift;
        [SerializeField] private RectOffset neutralShift;
        private bool isOn;
        public bool IsOn => isOn;
        private Action<bool> onToggle;

        private void Awake() {
            if (!isOn) {
                overlay.SetActiveFast(false);
            }
            // Since this awake will override the parent, we must call this
            RigButton();
        }

        public void Load(string tabName, Action onTabClick, Action<bool> onTabToggle) {
            Load(tabName, onTabClick);
            SetOnToggle(onTabToggle);
            RebuildTextLayout();
            UIUtils.RebuildLayoutRecursive(Rect);
            UIActivator.EnsureDeselected(button.gameObject);
        }

        public void SetOnToggle(Action<bool> onToggle) {
            this.onToggle = onToggle;
        }

        public void SetSelected(bool selected) {
            if (selected == isOn) { return; }
            DoSetSelected(selected);
            UIUtils.RebuildLayoutRecursive(Rect);
            onToggle?.Invoke(selected);
        }

        private void DoSetSelected(bool selected) {
            isOn = selected;
            if (themedButtonId_Active != null) {
                themedButton.Override(selected ? themedButtonId_Active : null);
            }
            overlay.SetActiveFast(selected);
            if (layout != null) {
                if (selected) {
                    layout.padding = selectedShift;
                } else {
                    layout.padding = neutralShift;
                }
            }
        }

        public void PrepareLayout() {
            if (!isOn && layout != null) {
                layout.padding = neutralShift;
            }
        }

        public void PoolDeactivate() {
            onToggle = null;
            gameObject.SetActiveFast(false);
            if (isOn) {
                DoSetSelected(false);
            }
        }

        public void PoolActivate() {
            PrepareLayout();
            SetInteractable(true);
            gameObject.SetActiveFast(true);
        }
        public override string ToString() {
            return $"WTabButton[{isOn}]";
        }
    }
}
