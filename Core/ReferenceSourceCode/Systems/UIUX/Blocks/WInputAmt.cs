using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine.EventSystems;

namespace Game.Systems.UIUX {
    public sealed class WInputAmt : WBase, IPointerEnterHandler {
        public WInput Input;
        public WButtonIcon Plus;
        public WButtonIcon Minus;
        private Action<int> onChange;
        private Func<int, int> validateChange;
        private int curValue;

        public void Load(int amt, Action<int> onChange, Func<int, int> validateChange = null) {
            this.onChange = onChange;
            this.validateChange = validateChange;
            var amtValid = validateChange != null ? validateChange(amt) : amt;
            if (amtValid != amt) {
                D.Err("Passing invalid initial amt to WInputAmt: {0} -> {1} | {2}",
                    amt, amtValid, this);
            }
            curValue = amtValid;
            Input.Load(null, Units.Num(amtValid), OnChangeInput);
            Input.SetOnSubmit(OnSubmitInput);
            Plus.Load(IconId.WPlus, () => {
                ChangeValueBy(+1);
            });
            Minus.Load(IconId.WMinus, () => {
                ChangeValueBy(-1);
            });
        }

        // For a value that changed elsewhere. Writing Input directly left curValue behind, so
        // the next +/- counted from the old value (STA-1925)
        public void SetValue(int amt) {
            curValue = validateChange != null ? validateChange(amt) : amt;
            Input.SetValue(Units.Num(curValue), false);
        }

        public void ChangeValueBy(int delta) {

            if (The.Bindings.IsPressed(ActionType.CtrlModifier)) {
                delta *= 50;
            } else if (The.Bindings.IsPressed(ActionType.ShiftModifier)) {
                delta *= 10;
            }

            var val = curValue;
            val += delta;
            if (validateChange != null) {
                val = validateChange(val);
            }
            curValue = val;
            onChange?.Invoke(val);
            Input.SetValue(Units.Num(val), false);
        }

        private void OnSubmitInput(string v) {
            D.Warn("Submitting input: {0}", v);
            OnChangeInput(v);
            Input.SetValue(Units.Num(curValue), false);
            A.S.UIQuery.CancelQuickChoice.Ask();
        }

        private void OnChangeInput(string v) {
            var oldVal = curValue;
            if (int.TryParse(v, out var val)) {
                curValue = validateChange != null ? validateChange(val) : val;
            } else {
                if (curValue != 0) {
                    curValue = 0;
                }
                Input.SetValue(Units.Num(curValue), false);
                Input.Focus();
            }
            if (oldVal != curValue) {
                onChange?.Invoke(curValue);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            A.S.UIQuery.HideCodexTooltip.Ask();
        }
    }
}
