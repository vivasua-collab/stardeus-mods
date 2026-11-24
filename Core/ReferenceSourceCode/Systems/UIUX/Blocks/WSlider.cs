using System;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WSlider : WBase {
        [SerializeField] protected Slider slider;
        [SerializeField] protected TMP_Text valueText;
        [SerializeField] protected UITextCollisionResolver collisionResolver;
        private bool isPercentage = true;
        private Func<float, string> valueTextFunc;
        private Action<float> onChange;
        public float Value => slider.value;

        public void SetInteractable(bool yes) {
            slider.interactable = yes;
        }

        private void OnChange(float val) {
            UpdateLabel(val);
            if (onChange == null) {
                D.Err("OnChange not bound on WSlider: {0}", D.GOPath(transform));
                return;
            }
            onChange.Invoke(val);
        }


        public void Load(float value, Action<float> onChange, float from = 0f, float to = 1f) {
            valueTextFunc = null;
            slider.onValueChanged.RemoveAllListeners();
            SetInteractable(true);
            SetRange(from, to);
            SetValue(value, false);
            SetOnChange(onChange);
            if (valueText != null) {
                UpdateLabel(value);
                valueText.SetActiveFast(true);
            }
            slider.onValueChanged.AddListener(OnChange);
        }

        public void SetRange(float from = 0f, float to = 1f) {
            isPercentage = from == 0f && to == 1f;
            slider.minValue = from;
            slider.maxValue = to;
        }

        public void SetValueTextFunc(Func<float, string> func) {
            valueTextFunc = func;
            var v = func(slider.value);
            if (v != null) {
                valueText?.SetText(v);
            }
        }

        public void SetOnChange(Action<float> onChange) {
            this.onChange = onChange;
        }

        public void SetValue(float v, bool notify = true) {
            if (notify) {
                slider.value = v;
            } else {
                slider.SetValueWithoutNotify(v);
                UpdateLabel(v);
            }
        }

        private void UpdateLabel(float v) {
            if (valueText == null) { return; }
            string txt;
            if (valueTextFunc != null) {
                txt = valueTextFunc.Invoke(v);
                if (txt == null) { return; }
            } else if (isPercentage) {
                txt = Units.Percentage(v);
            } else {
                txt = v.ToString("0.##");
            }
            valueText.SetText(txt);
            if (collisionResolver != null) {
                collisionResolver.AutoResize();
            }
        }

        public override string ToString() {
            return $"WSlider[{slider.value}|{onChange?.Target}]";
        }
    }
}
