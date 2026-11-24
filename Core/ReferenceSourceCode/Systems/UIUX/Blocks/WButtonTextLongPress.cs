using System;
using Game.UI;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonTextLongPress : WText {
        [SerializeField] protected UIButtonLongPress button;
        public bool IsHeldDown => button.IsHeldDown;
        public void Load(string text, float holdTime, Action act, Action<float> onProgress = null,
                bool ignorePointerExit = false) {
            UIActivator.EnsureDeselected(gameObject);
            // panel headers use this and some of them won't have text
            if (textMain != null) {
                SetText(text);
            }
            button.Arm(holdTime, act, onProgress, ignorePointerExit);
        }

        public void Click() {
            button.SimulateLongPress();
        }

        public void SimulateHold(bool isDown) {
            button.SimulateHold(isDown);
        }

        public void ResetSimulatedHold() {
            button.ResetSimulatedHold();
        }

        public void SetInteractable(bool yes) {
            button.interactable = yes;
        }
        public override string ToString() {
            return $"WButtonTextLongPress[{lastText}]";
        }
    }
}
