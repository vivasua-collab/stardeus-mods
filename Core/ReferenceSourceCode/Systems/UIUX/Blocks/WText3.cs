using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WText3 : WText2 {
        [SerializeField] private TMP_Text text3;
        protected string lastText3;

        public void Load(string text, string text2, string text3) {
            Load(text, text2);
            SetText3(text3);
        }

        public bool SetText3(string text, bool disableOnBlank = true) {
            return UIUtils.SetText(text3, text, ref lastText3, disableOnBlank);
        }

        public override string ToString() {
            return $"WText3[{lastText}|{lastText2}|{lastText3}]";
        }
    }
}
