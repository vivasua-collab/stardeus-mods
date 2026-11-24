using System;
using Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WText : WBase {
        [SerializeField] protected TMP_Text textMain;
        public TMP_Text Text => textMain;

        protected string lastText = ".";

        public void Load(string text) {
            SetText(text);
        }

        public bool SetText(string text, bool disableOnBlank = true) {
            return UIUtils.SetText(textMain, text, ref lastText, disableOnBlank);
        }

        public void RebuildTextLayout() {
            LayoutRebuilder.ForceRebuildLayoutImmediate(textMain.rectTransform);
        }

        public override string ToString() {
            return $"WText[{lastText}]";
        }
    }
}
