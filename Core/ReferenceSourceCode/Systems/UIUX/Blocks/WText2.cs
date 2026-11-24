using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WText2 : WText {
        [SerializeField] protected TMP_Text text2;
        protected string lastText2 = ".";
        // Popup header text that flows around the icon, looked up once since subclasses
        // already own Awake
        private UITextWrapAroundImage text2Wrap;
        private bool isText2WrapChecked;

        public void Load(string text, string text2) {
            Load(text);
            SetText2(text2);
        }

        public bool SetText2(string text, bool disableOnBlank = true) {
            bool isChanged = UIUtils.SetText(text2, text, ref lastText2, disableOnBlank);
            if (!isText2WrapChecked) {
                isText2WrapChecked = true;
                text2.TryGetComponent(out text2Wrap);
            }
            if (text2Wrap != null) {
                // Also when unchanged: a reused popup can have a new title above the same text
                text2Wrap.SetText(text);
            }
            return isChanged;
        }

        public override string ToString() {
            return $"WText2[{lastText}|{lastText2}]";
        }
    }
}
