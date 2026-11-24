using System;
using Game.Constants;
using Game.Data;
using Game.Tools;
using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonIconText2 : WButtonIconText {
        [SerializeField] protected UITextCollisionResolver resolver;
        [SerializeField] private TMP_Text text2;
        public TMP_Text Text2 => text2;
        protected string lastText2 = ".";
        public void Load(string icon, string text, string text2, Action onClick) {
            base.Load(icon, text, onClick);
            SetText2(text2);
            resolver?.AutoResize();
        }

        public new void Load(string icon, string text, Action onClick) {
            Load(icon, text, null, onClick);
        }

        public void SetText2(string txt, bool disableOnBlank = true) {
            if (UIUtils.SetText(text2, txt, ref lastText2, disableOnBlank)) {
                resolver?.AutoResize();
            }
        }
        public override string ToString() {
            return $"WButtonIconText2[{lastIcon}|{lastText}|{lastText2}]";
        }
    }
}
