using System;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WProgressText : WText, ICustomPoolable {
        [SerializeField] protected Slider slider;
        [SerializeField] protected Image fillImage;
        private Color? originalFill;

        public void Load(string text, float pct) {
            SetText(text);
            SetValue(pct);
        }

        public void SetFillColor(Color color) {
            if (originalFill == null) {
                originalFill = fillImage.color;
            }
            fillImage.color = color;
        }

        public void SetValue(float v) {
            slider.SetValueWithoutNotify(v);
        }

        public void SetPercent(float progress, bool autoText = true) {
            slider.value = progress;
            if (autoText) {
                SetText(Units.Percentage(progress));
            }
        }

        public override string ToString() {
            return $"WProgressText[{lastText}|{slider.value}]";
        }

        public void PoolDeactivate() {
            gameObject.SetActiveFast(false);
            if (originalFill.HasValue) {
                fillImage.color = originalFill.Value;
                originalFill = null;
            }
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }
    }
}
