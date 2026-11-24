using System;
using Game.UI;
using KL.Utils;
using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WButtonText2 : WButtonText {
        [SerializeField] private TMP_Text text2;
        protected string lastText2 = ".";

        public void Load(string text, Action act, string text2 = null) {
            SetText(text);
            SetText2(text2);
            SetOnClick(act);
        }

        public bool SetText2(string txt, bool disableOnBlank = true) {
            return UIUtils.SetText(text2, txt, ref lastText2, disableOnBlank);
        }

        public override string ToString() {
            return $"WButtonText2[{lastText}|{lastText2}]";
        }
    }
}
