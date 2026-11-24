using System;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WProgressText2 : WText2, ICustomPoolable {
        [SerializeField] protected Slider slider;
        [SerializeField] protected Image fillImage;
        [SerializeField] protected UITextCollisionResolver resolver;
        private Color? originalFill;

        public void Load(string text, string text2, float pct) {
            Load(text, text2);
            if (resolver != null) {
                resolver.AutoResize();
            }
            SetValue(pct);
        }

        public void SetFillColor(Color color) {
            if (originalFill == null) {
                originalFill = fillImage.color;
            }
            fillImage.color = color;
        }

        public void SetText2(string txt, bool autoResize = true, bool disableOnBlank = true) {
            if (UIUtils.SetText(text2, txt, ref lastText2, disableOnBlank) && autoResize) {
                resolver.AutoResize();
            }
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
            return $"WProgressText2[{lastText}|{slider.value}]";
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
